# 0011 — Canvas state is a write-behind JSON snapshot, with idempotent DDL and no ORM model

- **Status:** Accepted
- **Date:** 2026-10-25
- **Principles:** `state-snapshot-persistence` §2 (one table, one JSON document, idempotent DDL), §1 (mark dirty, flush on a timer),
  §3 (bounded rehydrate), §4 (a dead database degrades), §5 (deletions reach the store), §6 (record why this shape and not an ORM
  schema); P4 (the migrated-schema rule, from which a snapshot store is excluded by the guide); ADR 0009 (migration on startup)

## Context

A board is a small aggregate of at most 100 layers that changes many times a second while people co-edit. It is held in memory and
must survive a restart. Writing on every mutation would turn a burst of edits into a burst of writes, so the state is flushed on a
timer. The state is opaque to queries: nothing searches inside a layer.

## Decision

- One table, `canvas_snapshots`: `board_id uuid` (primary key), `version bigint`, `updated_at_ms bigint` and `snapshot text`.
  `updated_at_ms` is promoted only because rehydrate orders by it (§3). `snapshot` holds the camelCase JSON of `CanvasState`,
  the same shape REST returns.
- The DDL is `CREATE TABLE IF NOT EXISTS` and `CREATE INDEX IF NOT EXISTS`, run at startup. It is idempotent and hand-written,
  and the upsert is `INSERT … ON CONFLICT (board_id) DO UPDATE`, safe to repeat (§2).
- **Why not an EF Core entity model (§6):** the document, not a column set, is the contract, and the layer shape changes with the code
  that writes it. A migration for every field would cost more than the document saves. The `CanvasDbContext` is kept only as the
  connection and the empty migration history that ADR 0009 already runs.
- **`text` and not `jsonb`:** nothing queries inside the document, and `text` keeps the DDL identical on PostgreSQL and on the SQLite
  file the tests use, so the store is tested without Docker (ADR 0009).
- **Writer:** `CanvasSnapshotWriter` is a plain `IHostedService` that owns its loop, not a `BackgroundService`. Its final flush runs in
  `StopAsync`, which the host always awaits. Under parallel test hosts, a `BackgroundService` did not always start its `ExecuteAsync`,
  and a clean stop then lost the last interval of edits (10 of 216 writers in one measured run, all on the stop path). A test now
  checks that a restart keeps the layers, and 15 consecutive runs pass.
- **Degrade rules (§4, §5):**
  - A failed write re-queues its board for the next tick. The health probe reports `Degraded`, which still answers 200 (P8).
  - A failed read on a board not in memory is reported as unavailable (503) and never as an empty board. An empty board would be
    flushed over the stored one.
  - A failed delete stays pending and is retried each tick, so a deleted board cannot return as a ghost on a later rehydrate.
  - Layer deletes live inside the document, so a deleted layer is absent from the next snapshot and cannot come back.
- **Startup:** a failing migration still stops startup, as ADR 0009 decides. §4 of the guide says a dead database at boot should degrade;
  ADR 0009 takes precedence for the migration. The snapshot bootstrap, which runs after the migration, logs and continues.
- **Bounded rehydrate:** the most recent `Canvas:RehydrateLimit` boards (default 1000) load at startup. Older boards load on first access.

## Consequences

- A crash loses at most `Canvas:FlushInterval` (default 2 s) of edits. This is the trade the guide's §6 names.
- A board at the caps is roughly 4 MB of JSON (100 layers of 2000 path points). Each flush rewrites the whole document, so a busy board
  costs that much per interval. Acceptable for this audience; revisit if boards grow.
- A snapshot format change is a JSON compatibility change, not a DDL change. Readers must tolerate missing optional fields.
