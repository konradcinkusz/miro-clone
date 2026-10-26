# 0043 — A canvas purge needs the board to be deleted, as BoardsService reports it

- **Status:** Accepted. Amends the purge rule of ADR 0016 (decision 8, "purge by ownership"): ownership is still required, and the board must now also be gone.
- **Date:** 2026-10-26
- **Principles:** service-api-patterns §5 (the caller's token is forwarded, and the receiving service enforces the rule itself); the frozen contract in `docs/implementation-plan/README.md` (another workspace's row answers 404, never 403); ADR 0003 (BoardsService owns the boards table, CanvasService owns no board data); ADR 0013 (the purge is best effort and runs after the delete commit); the audit's (B) finding

## Context

`DELETE /api/canvas/{boardId}` in CanvasService purged a snapshot when the caller's workspace owned it (ADR 0016). It never asked whether the board still existed. The test suite pinned this: a member purging a board that was still live got 204 and the canvas was gone.

So any member of a workspace could wipe the live canvas of any board in it by calling CanvasService directly. The board row stayed, the live hub sessions were not told, and the board opened empty. Board deletion is also open to every member of the workspace, so the privilege gained is small, but the operation itself was never meant to exist for a live board, and no client reaches it.

ADR 0016 rejected asking BoardsService because "the board row is gone, so an access check against BoardsService cannot authorise the purge". That reasoning is about the answer for a deleted board, which is 404. The same answer also tells the purge that the board is deleted, if the caller is known to be in the same workspace, and the ownership check establishes that.

## Decision

- `DELETE /api/canvas/{boardId}` first asks BoardsService whether the caller may see the board (`GET /api/boards/{id}/access`, with the caller's token, as the hub does).
  - `Allowed`: the board is live. The purge answers `409 Conflict` and the snapshot stays.
  - `NotFound`: the board is deleted, or it is not in the caller's workspace. The existing ownership check then decides: a snapshot of another workspace answers `404`, and a snapshot of the caller's workspace is purged (`204`), or `204` when none is stored.
  - BoardsService cannot answer: `503` (or `502` for a redirect), and nothing is purged. The purge fails closed.
- BoardsService is unchanged: its purge still runs after its own commit and still treats a failure as advisory (ADR 0013).
- The alternatives were a service-to-service credential for BoardsService (a contract change on both sides, and a credential to manage) and purging before the commit (it reverses the frozen ordering). Neither is needed when the caller's own token already answers the question.

## Consequences

- A purge costs one more request to BoardsService. A cycle is avoided: BoardsService has committed and holds no transaction open while its purge call waits, so its `/access` answer does not wait on the purge.
- A purge that fails because BoardsService is unreachable leaves an orphan snapshot. Nothing reads it, because `/access` answers 404 first (ADR 0013). Its removal is R9 in COMPLIANCE, which is closed by correcting the log messages and docs rather than building a sweep (see the R9 entry there).
- Tests: `CanvasEndpointTests` pins the three answers (live board 409 and the snapshot kept; deleted board 204 and the snapshot removed; BoardsService down, 503 and the snapshot kept). The cross-service path (BoardsService deletes, then its purge reaches CanvasService, which reads the 404 from BoardsService) has not run in a live estate. It is not covered by any test here, and it needs the authenticated E2E run (COMPLIANCE R6).
