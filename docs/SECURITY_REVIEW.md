# Security review (WP15)

As of 2026-10-26, for `main` after PR #21. Method: `konradcinkusz/architecture-standards`, `docs/guides/SECURITY-REVIEW.md` (the category skeleton, the finding format, the N/A rule, and the four recurring launch blockers in §9), read in this session. The skill named in the brief (`quality-and-process:security-review`) is not available in this session, so the guide was applied directly.

**This is a static code review plus runs of the test suites and a live browser session. It is not a penetration test.** Treat it as the list of what the code does and does not do, not as an assurance that the running system is secure.

## 1. Ranked findings

Format per the guide §2: Location, Issue, Current code, Risk, Attack scenario, Impact, Recommendation. Status: **Fixed**, **Open-decision** (owner), **Open-accepted** (ADR or register row), **Open-unverified**.

### S-1 — No framing or sniffing headers (Medium) — **Fixed**, ADR 0047
- **Location:** `MiroClone.Web/Program.cs` (no header middleware before this change).
- **Current code:** no `X-Frame-Options`, no `Content-Security-Policy`, no `X-Content-Type-Options`, no `Referrer-Policy` in the web app.
- **Attack scenario:** a hostile site places `https://…/sign-in` in an invisible iframe over a decoy button. The user types the password into the framed form, or clicks through a decoy that signs them into the attacker's account.
- **Impact:** credential entry under a hostile overlay.
- **Recommendation / fixed:** `SecurityHeaders` middleware on every response (`frame-ancestors 'none'`, `X-Frame-Options: DENY`, `nosniff`, a referrer policy and a CSP with no inline script). Checked in Chromium on the account pages: no violation logged.

### S-2 — Account forms read an unbounded body before the antiforgery check (Low) — **Fixed**, ADR 0047
- **Location:** `MiroClone.Web/Account/AccountEndpoints.cs` (`ReadFormAsync` before `antiforgery.IsRequestValidAsync`).
- **Current code:** the form is read first, then the token is checked.
- **Attack scenario:** an unauthenticated client posts a large multipart or urlencoded body to `/account/sign-in` at the rate the network allows; the server buffers it before rejecting it.
- **Impact:** memory pressure on the web instance, shared with every session.
- **Recommendation / fixed:** the `/account` group refuses a declared length over 16 KiB before any read, and sets the server body-size limit for chunked bodies. Minimal APIs do not enforce the request-size metadata by themselves; this was found by a failing test, and the filter is what enforces it.

### S-3 — The hub's bearer token was recorded in exported telemetry (Medium) — **Fixed for span tags; live export open**, ADR 0047, COMPLIANCE R2
- **Location:** `MiroClone.Web/Canvas/Realtime/*` (the hub connection takes `access_token` in the WebSocket query string) and `MiroClone.ServiceDefaults/ServiceDefaultsExtensions.cs` (ASP.NET Core instrumentation records URLs).
- **Attack scenario:** whoever can read the exported traces (the telemetry backend, its operators, a leaked dashboard) copies a live `access_token` from a span URL and calls the boards or canvas APIs until the token expires.
- **Impact:** the token's full lifetime as a bearer credential for every board the user can see.
- **Recommendation / fixed:** `QueryTokenRedactor` removes the value from `url.full`, `url.query`, `http.url` and `http.target` before export; tested. Still open: log messages and exception text are not scrubbed, and the OTLP path has not run live. Better: a short-lived ticket for the socket, or a transport that can carry a header (S-7).

### S-4 — The account forms had no rate limit (High) — **Fixed**, ADR 0045, COMPLIANCE R8
- **Location:** `MiroClone.Web/Account/AccountEndpoints.cs`. The kernel's auth policy existed and no service used it.
- **Attack scenario:** an attacker tries a list of common passwords against one account from one address at thousands of attempts a minute.
- **Impact:** account takeover for weak passwords, with no brake.
- **Recommendation / fixed:** `RateLimitPolicies.Auth` on the `/account` group (10 per client per minute by default), a redirect with a message when rejected, and sign-out exempt. Verified in a live process: the 11th and 12th sign-ins redirect; the endpoint is not reached.

### S-5 — Any workspace member could purge a live board's canvas (Medium) — **Fixed**, ADR 0043
- **Location:** `MiroClone.CanvasService/Api/CanvasEndpoints.cs`, `DeleteCanvasAsync`.
- **Current code (before):** `registry.PurgeAsync(boardId, workspace)` with no check that the board is gone.
- **Attack scenario:** a member calls `DELETE /api/canvas/{id}` on a board that is still live. The board stays, its canvas is erased, and everyone opening it sees an empty board.
- **Impact:** data loss for a live board, by a user who was allowed to delete boards but not to delete their canvas through this path.
- **Recommendation / fixed:** the purge asks BoardsService (`/access` with the caller's token) first; a live board answers 409, and a BoardsService that cannot answer fails closed with 503.

### S-6 — Canvas snapshots of deleted boards are kept indefinitely (Medium) — **Open-decision**, COMPLIANCE R9, ADR 0044
- **Location:** `MiroClone.BoardsService/Canvas/CanvasPurgeClient.cs` (best-effort purge), `MiroClone.CanvasService/Persistence/CanvasRegistry.cs`.
- **Attack scenario:** not an attack. A purge fails after a delete (BoardsService unreachable, or a timeout). The snapshot of a deleted board, with its layers, is kept with no reclaim path.
- **Impact:** personal content of a deleted board persists; it is never read (the access check answers 404 first), but it is stored.
- **Recommendation:** an owner decision on reclaim: a listing from BoardsService or a service credential (ADR 0013, PRIVACY.md §6). Logs and docs now say no sweep exists.

### S-7 — The hub token travels in the WebSocket URL (Medium) — **Open-decision**, ADR 0016
- **Location:** the canvas hub connection (`MiroClone.Web/Canvas/Realtime/SignalRCanvasHubConnection.cs`, `CanvasService/Realtime/`).
- **Attack scenario:** the URL, with the token, is written by any proxy or load balancer that logs request lines. The Fly edge logs are an example (not verified here).
- **Impact:** the same as S-3, at the edge rather than in telemetry.
- **Recommendation:** a short-lived single-use ticket for the socket, issued by the BFF on the server, or a transport that carries a header. Both need an ADR. Lifetime of the access token is the current bound.

### S-8 — Sign-up tells callers which emails have accounts (Low) — **Open-decision** (authservice)
- **Location:** authservice `Controllers/AuthController.cs` `Register`: `BadRequest(new { errors })` carries Identity's description, which for a duplicate email names the address as taken. The web app shows the text as-is (`AccountFlows`, `AuthorityClient`).
- **Attack scenario:** a script posts sign-ups for a list of emails and reads the answer; the ones that say "already taken" have accounts. The forgot-password answer is generic (`If an account with that email exists…`), so the leak is only in sign-up.
- **Impact:** account enumeration, which feeds phishing and credential stuffing.
- **Recommendation:** authservice returns the same answer for a new and a taken address (verification flow or a generic message). The web app cannot fix this on its own. The auth limit (S-4) now bounds the rate of such a scan.

### S-9 — No request limit on the web app except the account forms (Medium) — **Open-decision** (edge), ADR 0045
- **Location:** `MiroClone.Web/ServiceCollectionExtensions.cs` (`AddAccountRateLimiting` with `globalLimit: false`).
- **Attack scenario:** a flood of page or circuit requests from one address. The web app does not throttle it.
- **Impact:** availability of the web instance.
- **Recommendation:** an edge rate limit at deployment (Fly), an owner decision. ADR 0045 records that the app has none by design.

### S-10 — Boards and canvas APIs have only the global per-user limit (Low) — **Open-accepted**
- **Location:** `MiroClone.BoardsService/Program.cs`, `MiroClone.CanvasService/Program.cs` (`AddStandardRateLimiting`, `UseRateLimiter`). No endpoint carries `RequireRateLimiting(RateLimitPolicies.Api)`.
- **Attack scenario:** an authenticated user loops on a list or create endpoint up to the 300-per-minute global limit.
- **Impact:** load on the database for one user; no cross-user effect.
- **Recommendation:** apply the `Api` policy to the list and create endpoints (120 per user per minute), an owner decision on the numbers. This is the "partial coverage" blocker in the guide §9.

### S-11 — Erasure keeps board author ids, names and favourites (Medium) — **Open-decision**, COMPLIANCE R10
- **Location:** `MiroClone.BoardsService/Boards/` (author id and name on boards; `BoardFavourites`).
- **Impact:** personal data after an account is erased in authservice.
- **Recommendation:** an owner policy (PRIVACY.md §4 and §6).

### S-12 — Two-factor accounts cannot sign in (Info) — **Open-accepted**, ADR 0022, R16
- **Location:** `MiroClone.Web/Auth/AuthorityClient.cs`, `LoginAsync`: the second-factor answer is refused with a message.
- **Impact:** availability for those users; no bypass, because the answer is refused rather than skipped.

### S-13 — The authenticated paths are unverified (Medium) — **Open-unverified**
- **Location:** `tests/MiroClone.Web.E2E/Journeys/*` (12 journeys skip without `MIROCLONE_E2E_AUTHENTICATED=1`), and the session-refresh and logout code (`Auth/SessionService.cs`, `Auth/AccountFlows.cs`).
- **Impact:** single-flight refresh, sign-out revocation and the cross-workspace 404 are covered by unit tests with scripted handlers only. A live run might find what the scripted handlers hide.
- **Recommendation:** run the authenticated journeys against the pinned authservice in CI (`MIROCLONE_E2E_AUTHENTICATED=1`), which needs the image to pull in the CI runner.

### S-14 — Cookie and session flags (Info, positive)
- **Location:** `Auth/` and `ServiceCollectionExtensions.cs` (cookie `miroclone.session`: HttpOnly, SameSite=Strict, Secure outside Development). A test asserts no token value is in the principal.

## 2. Positive findings (checked, and sound)

- **Another workspace's rows answer 404**, not 403: `BoardsApiTests.AccessAnswersNoContentForABoardInTheWorkspaceAndNotFoundOtherwise` and `CanvasEndpointTests.ANonMemberIsAnsweredNotFoundNeverForbidden`. A token with no single workspace answers 403 (`BoardsApiTests.AValidTokenWithNoActiveWorkspaceIsForbiddenNotDefaulted`): that is a refusal of the token, not of a row, and it is deliberate (ADR 0024).
- **Antiforgery** on every account form; the origin guard on unsafe methods (ADR 0006: a mitigation, not authentication).
- **Open redirects:** the return URL is local only (`LocalReturnUrlTests` in `tests/MiroClone.Web.Tests/Auth/PureRulesTests.cs`).
- **Forgot-password** answer does not reveal whether the address has an account.
- **Secrets:** gitleaks in CI and pre-commit; the signing key is a secret parameter; fixtures carry `gitleaks:allow`.
- **Dependencies:** NuGet audit on every restore (`NuGetAuditMode=all`, `NuGetAuditLevel=low`, warnings as errors).
- **Containers:** non-root (`USER app`).
- **Fly deployment** is disabled (ADR 0026), so no deployed attack surface exists today.

## 3. The four recurring launch blockers (guide §9)

| Blocker | Status here |
|---|---|
| 1. Production CORS origins never configured | Not applicable: no CORS policy; the BFF is same-origin |
| 2. Email confirmation disabled "temporarily" | Authservice's dev outbox is development-only; the Fly authservice config sets its environment (`flyio/authservice.fly.toml`). Confirm at first deploy (owner) |
| 3. Rate limiting partial | Account forms: covered (S-4). Boards and canvas APIs: global limit only (S-10). Web app: no global limit (S-9). **Partial: owner decision** |
| 4. Token storage inconsistent between frontends | One frontend (the BFF). Tokens are server-side (ADR 0022); no localStorage or cookie token exists |

## 4. Not applicable (with the evidence)

The categories and the greps are the same as in `docs/ARCHITECTURE_REVIEW.md` §4: XML parsing, raw SQL with input, untrusted deserialisation, file upload, CORS, WebAssembly. Each was searched in the repository before it was marked N/A.

## 5. Readiness ledger

| Window | Items |
|---|---|
| Immediately | S-13: run the authenticated journeys in CI (needs the authservice image on the runner). S-7: decide the socket token design (its lifetime is the bound until then) |
| Within a week | S-8 (authservice answer), S-6 and S-11 (owner decisions), S-9 (edge rate limit at deploy) |
| Later | S-10 (Api policy numbers), S-12 (two-factor) |

## 6. Caveat

This review is a static analysis of the code plus the tests and one browser session. It does not replace a penetration test of a deployed estate, and it does not cover the authservice's own code except where it was read for the enumeration check (S-8).
