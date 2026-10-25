# 0006 — The origin guard is a mitigation, not authentication, and CORS stays unconfigured

- **Status:** Accepted
- **Date:** 2026-10-25
- **Principles:** `blazor-web-app` §8 (origin guard in the kernel on the web app and the API; CORS unconfigured); P2 (cross-cutting plumbing);
  P5 (tokens never reach the browser)

## Context

A websocket handshake is not subject to CORS. A foreign page could open a Blazor circuit or the canvas hub with the user's
cookie unless the server refuses it. The Blazor guide answers this with one middleware, not with CORS.

## Decision

- `OriginGuardMiddleware` refuses a request whose `Origin` is neither the app's own `scheme://host[:port]` nor listed in `OriginGuard:AllowedOrigins`.
- A request with `Sec-Fetch-Site: cross-site` is refused unless it is a top-level `GET`/`HEAD` navigation.
- A request with no `Origin` (curl, server-side `HttpClient`, probes, Playwright) is allowed.
- CORS is not configured anywhere. The guard refuses foreign origins and never grants any.
- The guard runs first in the pipeline, and forwarded headers are configured before it.
- Tests pin three refusals (foreign `Origin` on POST, foreign `Origin` on GET as the websocket stand-in, cross-site without `Origin`) and the allowed cases.

## Consequences

- **This is a mitigation, not authentication.** It stops a browser on another site. It does not stop code already on the machine, or a non-browser client that sends neither header. Authentication is still the JWT, and the cookie is still `SameSite=Strict`.
- The WebSocket refusal is tested through a plain GET in the test host, because TestServer does not perform a websocket upgrade. The upgrade path itself is covered by WP05's circuit tests.
