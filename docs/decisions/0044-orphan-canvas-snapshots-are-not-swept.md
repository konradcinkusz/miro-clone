# 0044 — Orphan canvas snapshots are not swept; the code and the documents say so

- **Status:** Accepted, as a deviation from ADR 0013. The sweep ADR 0013 describes is not built, and this record replaces its claim that one exists.
- **Date:** 2026-10-26
- **Principles:** P14 (a document states what the code does); state-snapshot-persistence §5 (orphans are reclaimed); ADR 0003 (CanvasService owns the snapshot data); the frozen contract in `docs/implementation-plan/README.md` (a change to a contract needs an ADR with both sides in one PR); COMPLIANCE register R9

## Context

ADR 0013 says a failed purge leaves an orphan snapshot, and that CanvasService's hosted service sweeps orphans. The log messages in `CanvasPurgeClient` say "the orphan snapshot is left for the sweep". No sweep exists in the repository, and nothing removes an orphan. The audit found the mismatch (R9).

A sweep would have to find the snapshots whose board BoardsService no longer reports. That needs one of two things, neither of which exists:

- a listing of board ids from BoardsService, which the frozen contract does not define; or
- a service-to-service credential that CanvasService uses to ask BoardsService about each snapshot, which the frozen contract also does not define (ADR 0013 names this gap).

Either one is a contract change with a credential decision, and that is an owner decision (see PRIVACY.md §6). Building a sweep now would invent both.

## Decision

- No sweep is built in WP15. The log messages and the documents say that no sweep exists, and that the snapshot stays until a later decision adds one.
- The messages change from "left for the sweep" to "is not swept (ADR 0044)". The behaviour is unchanged: the purge still runs after the delete commit and is still advisory.
- Nothing reads an orphan. The access check answers 404 for a deleted board before any read (ADR 0013), so an orphan is data at rest, not an exposed one.
- The retention gap stays open as an owner decision: an orphan is kept **indefinitely** until it is purged (PRIVACY.md §5 and §6).

## Consequences

- ADR 0013 keeps its decision on the purge, and its statement that a sweep exists is superseded by this record.
- R9 in COMPLIANCE is no longer "drift": it is a recorded deviation with an owner decision attached. It is not closed.
- A failed purge after a successful delete stays unreclaimed until the owner decides how orphans are reclaimed. ADR 0043's purge rule does not change that: a purge now also needs the board to be deleted, so a purge that is refused for a live board leaves no orphan.
