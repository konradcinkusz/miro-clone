# 0009 — Schema migration on startup is one kernel hosted service, run before the host listens

- **Status:** Accepted
- **Date:** 2026-10-25
- **Principles:** P4 (persistence is provider-portable and its schema is migrated, never "ensured"); P2 (the kernel holds plumbing,
  not domain); ADR 0005 (the kernel's contents and ceiling)

## Context

Every data service migrates its own database at startup (WP02: `MigrateAsync` in a hosted service). Writing that loop in each service
would copy the same code into each one. The kernel already holds the database provider extension (ADR 0005), which is the same kind of
plumbing.

A plain `IHostedService` is not enough. The web server is a hosted service too, and it starts before services registered later. A
probe could answer before the migration has run, and Aspire's `WaitFor` would then release dependants early.

## Decision

- `AddMigrateOnStartup<TContext>()` is a kernel extension. It registers a hosted service that implements `IHostedLifecycleService`.
- The migration runs in `StartingAsync`, which the host calls for every hosted service before any `StartAsync`, so before Kestrel
  binds. A failed migration stops startup.
- The migration runs only when `Database.IsRelational()`. The InMemory provider has no schema and is skipped.
- The kernel gains no entity, DTO, enum or user-facing string. The line count stays under the ceiling.

## Consequences

- A service that cannot reach its database does not listen. Its `/health` is never reached, so the dependant waits, which is the intended
  behaviour.
- Migrations run in the process that serves traffic. A deployment with several instances runs the same migration concurrently. Fly.io
  deployment (WP13) must run one instance first, or serialise migrations, and that decision is recorded there.
- Tests use the SQLite provider with a file (no network, no Docker) to exercise this path. The PostgreSQL path is covered by tests
  gated on `TEST_POSTGRES`.
