# 0013 — A deleted board's canvas is purged in the request, best effort, with the caller's token; the orphan is harmless

- **Status:** Accepted. The sweep this record describes is not built; ADR 0044 corrects that claim and the logs say so.
- **Date:** 2026-10-25
- **Principles:** P3 (one database per service; cross-context reads go over HTTP); P11 (anti-corruption at the edge);
  service-api-patterns §5 (forward the inbound bearer, no auto-redirect on service clients, timeouts by criticality, retry only
  idempotent calls); the frozen contract in `docs/implementation-plan/README.md` (DELETE publishes nothing; the purge runs after commit)

## Context

Deleting a board touches two stores: the `boards` database (BoardsService) and the canvas snapshot (CanvasService, its own `canvas`
database). No transaction spans both (ADR 0003). The contract says the purge is best effort, that an orphan snapshot is harmless, and
that a hosted service sweeps orphans. It does not say how the sweep authenticates to CanvasService.

## Decision

- `DELETE /api/boards/{id}` deletes the board and its favourites in one `SaveChanges` transaction, then calls
  `DELETE /api/canvas/{boardId}` once. The call is made through the typed `CanvasPurgeClient`.
- The caller's `Authorization` header is forwarded. CanvasService authorizes the purge itself, so BoardsService never acts as a
  privileged caller.
- The purge is advisory. Any failure after commit, whether an error status, an unreachable service, a timeout or an open circuit, is
  logged and the delete still answers 204. The only exception not swallowed is the caller's own cancellation.
- The client does not follow redirects, so a redirect between services is a visible failure, not a silent DELETE-to-GET.
- `HttpClient.Timeout` is 8 seconds. Per the guide, it caps the kernel's resilience budget, so the purge cannot hold a response for the
  kernel's 30-second default.
- The kernel's standard resilience handler may retry the purge. That is safe because DELETE is idempotent.
- No outbox table and no sweeper in BoardsService. A retry that runs after the request has ended has no caller token to forward, and a
  stored token would be a credential at rest. The orphan snapshot is swept by CanvasService (its hosted service), which owns the data.

## Consequences

- Between a committed delete and a successful purge, a snapshot can exist with no board. Nothing reads it: the access check
  (`GET /api/boards/{id}/access`) answers 404 first, so a deleted board's canvas is unreachable.
- A purge that is never retried leaves its snapshot until the CanvasService sweep removes it. That sweep is not built in this PR; it is
  listed under "Not done" in the PR body, and it needs a service credential that the frozen contract does not define.
- `boards-service` references `canvas-service` in the AppHost without a `WaitFor`. A wait in both directions would be a startup cycle
  (ADR 0008). CanvasService's own calls to `/access` will need the same care when WP05 wires them.
