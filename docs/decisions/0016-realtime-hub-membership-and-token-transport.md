# 0016 — The canvas hub checks membership on every join, reads its token from the query string for websockets, and purges by workspace ownership

- **Status:** Accepted
- **Date:** 2026-10-25
- **Principles:** `service-api-patterns` §1 (rate limits per caller, immediate rejection, no queueing), §5 (forward the caller's bearer token,
  `AllowAutoRedirect = false`, a redirect is a configuration bug); `blazor-web-app` §8 (origin guard on the hub, ADR 0006); ADR 0003 (membership
  is decided by the owner of the boards table, reached over HTTP); P5 (tokens never reach the browser); `docs/decisions/0011-authservice-adoption-and-token-contract.md` on `main`, the authservice token
  contract (one estate audience, one `organization` claim per membership), which amends ADR 0007

## Context

The hub is the only realtime surface. Its callers are the Blazor circuit (the BFF), which holds the user's token server-side. CanvasService owns no
board data, so it cannot decide who may see a board. Browsers cannot set headers on a websocket, so the token must travel some other way.

BoardsService purges the canvas after it deletes a board, and forwards the caller's token to do it (WP04). By then the board row is gone, so an
access check against BoardsService cannot authorise the purge. The purge needs another basis.

## Decision

1. **Token transport.** The hub accepts the JWT in the `Authorization` header for HTTP transports and in the `access_token` query parameter for
   websockets. The query parameter is the SignalR convention, and it is read only for `/hubs/canvas`.
2. **Membership on every join.** `JoinBoard` and the REST read ask BoardsService `GET /api/boards/{id}/access` with the caller's token forwarded.
   `404` answers `board_not_found` (or the REST `404`), the same answer as a missing board. Any other failure answers `canvas_unavailable` (`503`) and
   fails closed. A redirect from BoardsService is `502` and is never followed.
3. **One workspace per caller.** The caller's workspace is its single `organization` claim, read by `WorkspaceClaims.WorkspaceId`. A token with two or
   more organizations has no active workspace, and the caller is refused as not found. A new canvas is recorded under that workspace.
4. **Ordered joins and broadcasts.** `BoardCanvas.Gate` serialises a join's snapshot with the broadcasts of that board. A joiner therefore sees each
   change either inside its snapshot or as a broadcast after it, never neither.
5. **Presence identity comes from the token.** The server stamps the user id and the colour, which is chosen from the user id. The client sends only the
   cursor and selection. authservice issues no name claim, so the label is `Member` and the first four hex digits of the user id. An email address is never
   shown to other participants. A `name` claim, if authservice ever issues one, is used and capped at 64 characters.
6. **Rate limits per connection**, as token buckets with no queue. Mutations allow 120 in a burst, refilling at 60 a second. Presence allows 60 in a
   burst, refilling at 30 a second. A rejection is immediate (`rate_limited`).
7. **Message size.** The receive cap is 128 KB, so a legal 2000-point stroke is accepted (the default 32 KB rejects it).
8. **Purge by ownership.** `DELETE /api/canvas/{boardId}` needs no BoardsService call. Each snapshot records the workspace it belongs to (ADR 0015).
   The purge proceeds only when the caller's single organization matches that workspace:
   - a snapshot of the caller's workspace is purged, even when the board row is already gone: `204`;
   - a snapshot of another workspace is refused as not found, so a caller learns nothing about another workspace's board: `404`;
   - no snapshot is stored: `204`, so a retry from BoardsService is harmless;
   - a caller without a single organization is refused as not found: `404`.

## Why ownership, and not the alternatives

The frozen contract orders the purge after the board's deletion. Three ways to authorise a purge were considered:

- **A service-to-service credential for BoardsService.** Sound, but it adds a credential and a contract change on both sides, which needs its own ADR.
- **A sweep of snapshots whose board BoardsService no longer reports.** Leaves orphans until the sweep runs, and needs a listing from BoardsService that does
  not exist.
- **Purging before BoardsService's commit.** Reverses the contract's ordering, and a failed commit would leave the canvas gone.

Ownership needs none of these. The workspace is already the authority the estate uses (`docs/decisions/0011-authservice-adoption-and-token-contract.md` on `main`), so the purge needs only the token it already has.
The estate audience means BoardsService's forwarded token is valid here as well, so the purge call in WP04 works without a change.

## Consequences

- The token appears in the websocket URL, which browsers require. Exposure is bounded by TLS and by the token's short lifetime. Telemetry must not record that
  query value. The OpenTelemetry settings are to be checked in WP13.
- The origin guard (ADR 0006) applies to the handshake, because it runs before the hub. The websocket upgrade path itself is not exercised by the tests, which
  use long polling on TestServer. Real websocket behaviour is for WP12's browser tests.
- The token is validated when the connection opens. A connection that outlives its token keeps working until it closes. Re-validating mid-session needs a
  push from the caller or a shorter-lived connection, and is not in this package.
- `InitialState` lists presence without a connection id, as the frozen contract spells it. A client can match presence to a connection only by user id, so two
  tabs of one user collapse for the client. WP10 must handle that, or the contract needs a connection id added through an ADR.
- A purge removes the snapshot for everyone in the workspace. That is the intent: the board is deleted for them too.
- A load that completes after a purge could bring the board back into memory for a moment. The access check runs first and refuses a deleted board, so the window
  is a few milliseconds. Noted here rather than engineered away.
