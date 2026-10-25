# 0012 — The canvas hub checks membership on every join, reads its token from the query string for websockets, and has one open decision on purge

- **Status:** Accepted, with one open decision (purge authorisation, below)
- **Date:** 2026-10-25
- **Principles:** `service-api-patterns` §1 (rate limits per caller, immediate rejection, no queueing), §5 (forward the caller's bearer token,
  `AllowAutoRedirect = false`, a redirect is a configuration bug); `blazor-web-app` §8 (origin guard on the hub, ADR 0006); ADR 0003 (membership
  is decided by the owner of the boards table, reached over HTTP); ADR 0007 (RS256, per-service audience); P5 (tokens never reach the browser)

## Context

The hub is the only realtime surface. Its callers are the Blazor circuit (the BFF), which holds the user's token server-side. CanvasService owns no
board data, so it cannot decide who may see a board. Browsers cannot set headers on a websocket, so the token must travel some other way.

## Decision

1. **Token transport.** The hub accepts the JWT in the `Authorization` header for HTTP transports and in the `access_token` query parameter for
   websockets. The query parameter is the SignalR convention, and it is read only for `/hubs/canvas`.
2. **Membership on every join.** `JoinBoard` asks BoardsService `GET /api/boards/{id}/access` with the caller's token forwarded.
   `404` answers `board_not_found`, the same answer as a missing board. Any other failure answers `canvas_unavailable` and fails closed.
   A redirect from BoardsService is `502` and is never followed.
3. **Ordered joins and broadcasts.** `BoardCanvas.Gate` serialises a join's snapshot with the broadcasts of that board. A joiner therefore
   sees each change either inside its snapshot or as a broadcast after it, never neither.
4. **Presence identity comes from the token.** The server stamps the user id, name and colour. The name is capped at 64 characters, the colour is
   chosen from the user id, and the client sends only the cursor and selection.
5. **Rate limits per connection**, as token buckets with no queue. Mutations allow 120 in a burst, refilling at 60 a second. Presence allows 60 in
   a burst, refilling at 30 a second. A rejection is immediate (`rate_limited`).
6. **Message size.** The receive cap is 128 KB, so a legal 2000-point stroke is accepted (the default 32 KB rejects it).
7. **Purge authorisation (DELETE `/api/canvas/{boardId}`).** The caller must still be allowed to see the board. Without that check, any authenticated
   user could purge another workspace's canvas.

## Open decision (owner)

The frozen contract says BoardsService calls `DELETE /api/canvas/{boardId}` after it commits the board's deletion. By then the board no longer exists,
so the `/access` check answers `404`, and the purge is refused. The orphan snapshot is then left in place. The contract calls an orphan "harmless and
swept later", but no sweep is in this package. Options, in order of preference:

- (a) A service-to-service credential for the purge, issued to BoardsService only, with the caller checked by audience. This is a contract change and
  needs an ADR on both sides (README: frozen contracts).
- (b) A sweep that deletes snapshots whose board BoardsService no longer reports, run by CanvasService on a timer.
- (c) BoardsService purges before its commit. This reverses the contract's ordering.

This package does not choose. It keeps the safe default, and WP04 and WP15 must settle it.

## Consequences

- The token appears in the websocket URL, which browsers require. Exposure is bounded by TLS and by the token's short lifetime. Telemetry must not record that
  query value. The OpenTelemetry settings are to be checked in WP13.
- The origin guard (ADR 0006) applies to the handshake, because it runs before the hub. The websocket upgrade path itself is not exercised by the tests, which
  use long polling on TestServer. Real websocket behaviour is for WP12's browser tests.
- `InitialState` lists presence without a connection id, as the frozen contract spells it. A client can therefore match presence to a connection only by user
  id, so two tabs of one user collapse for the client. WP10 must handle that, or the contract needs a connection id added through an ADR.
- The token is validated when the connection opens. A connection that outlives its token keeps working until it closes. Re-validating mid-session needs a
  push from the caller or a shorter-lived connection, and is not in this package.
- A load that completes after a purge could bring the board back into memory for a moment. The access check runs first and refuses a deleted board, so the window
  is a few milliseconds. Noted here rather than engineered away.
