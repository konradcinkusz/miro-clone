# 0047 — Every web response carries framing and sniffing headers; account forms have a body limit; a bearer token is removed from exported telemetry

- **Status:** Accepted. Closes two parts of COMPLIANCE R2 (the hub token in telemetry), and adds the headers and body limit that the security review found missing.
- **Date:** 2026-10-26
- **Principles:** security-review §7 (output and rendering: a page is never framed by another origin, and content is not sniffed), §1 (the method: each finding names an attack scenario), §5 (tokens are credentials, not identifiers); P5 (tokens never reach the browser, and a credential is not logged); P15 (telemetry is a build-time decision, and it must not carry secrets); ADR 0006 (the origin guard is a mitigation, not authentication), ADR 0022 (the account forms are the password surface)

## Context

The security review of WP15 (`docs/SECURITY_REVIEW.md`) found three gaps that are cheap to close and were not closed by any earlier ADR:

1. **No security headers.** The web app sent no `X-Frame-Options`, no `X-Content-Type-Options` and no `Referrer-Policy`, and no `Content-Security-Policy` of its own. Attack scenario: a hostile page frames `/sign-in` in an invisible iframe and asks the user to click where the password field sits (clickjacking of the credential form).
2. **No body limit on the account forms.** The forms read the whole request body before the antiforgery check. Attack scenario: an unauthenticated client posts a multi-megabyte body to `/account/sign-in`, and the server buffers it before it can reject the request.
3. **The hub's bearer token in telemetry.** The canvas hub takes its access token in the WebSocket query string, because a browser WebSocket cannot send a header. The ASP.NET Core instrumentation records the URL of each request as a span tag, so the token reached any exporter that was configured (COMPLIANCE R2). Attack scenario: whoever can read the traces reads a live access token and uses it until it expires.

## Decision

- **Headers on every response** (`SecurityHeaders`): `Content-Security-Policy` with `default-src 'self'`, `script-src 'self'`, `style-src 'self' 'unsafe-inline'` (the markup and the board use inline style attributes; there is no inline script), `img-src 'self' data:`, `connect-src 'self'`, `object-src 'none'`, `base-uri 'self'`, `form-action 'self'` and `frame-ancestors 'none'`; `X-Frame-Options: DENY`; `X-Content-Type-Options: nosniff`; `Referrer-Policy: strict-origin-when-cross-origin`. The policy was checked in Chromium: the account pages load with no violation reported.
- **Body limit on `/account`:** 16 KiB. The endpoint filter refuses a declared length over the limit with 413 before any read, and sets the server's body-size feature for a body without a length. Minimal APIs do not enforce `IRequestSizeLimitMetadata`, so the metadata alone does nothing; this was found by a failing test.
- **Token redaction** (`QueryTokenRedactor`): before a span is exported, the value of `access_token` is replaced with `REDACTED` in `url.full`, `url.query`, `http.url` and `http.target`. The rest of the URL stays, so a trace still shows the route.

## Consequences

- The framework also sets `frame-ancestors 'self'` on some responses. Browsers enforce both policies, so the stricter one (`'none'`) applies. Two headers say the same thing; that is accepted until the framework's own header can be removed.
- The board page is not yet checked against the policy: it needs a signed-in session, which this session cannot create (the authservice image was not available). The page uses inline style attributes, which the policy allows, and no inline script. This is the first thing the authenticated E2E run should confirm.
- The redaction covers the span tags only. It does not cover log messages or exception text, and it does not stop the token from being in the WebSocket URL, which is where the browser requires it. Shortening that lifetime or moving to a ticket is an owner decision (ADR 0016).
- The OTLP export is still not run here, so the redaction is verified as a processor, not as an export. COMPLIANCE R2 stays open for the live check.
- Tests: `SecurityHeadersTests` (headers on a real response; the 413 from the filter, on a sample group) and `QueryTokenRedactorTests` (the token removed from every URL tag, and a URL without a token left alone).
