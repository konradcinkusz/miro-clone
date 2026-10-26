# 0045 — The auth rate limit covers the account forms, answers with a redirect, and leaves sign-out alone

- **Status:** Accepted. Applies the kernel's `RateLimitPolicies.Auth` to the web app for the first time (COMPLIANCE R8). Deviates from the kernel default in two places: the web app has no catch-all limiter, and a rejected form is redirected rather than answered with JSON.
- **Date:** 2026-10-26
- **Principles:** service-api-patterns §1 (the auth policy is the brute-force control on sign-in endpoints); ADR 0006 (the origin guard is a mitigation, not authentication, so the password endpoints need their own limit); ADR 0018 (the forwarded client address is the one the limit keys on); ADR 0022 (every account step answers with a redirect and a message, never a bare error); blazor-web-app §8 (the origin guard)

## Context

The account forms (`/account/sign-in`, `sign-up`, `verify-email`, `resend-verification`, `forgot-password`, `reset-password`, `consent`, `sign-out`) had no rate limit (COMPLIANCE R8). The kernel defines the auth policy (10 requests per client per minute by default) and no service used it.

Three things had to be decided:

1. **The catch-all limiter.** The kernel's `AddStandardRateLimiting` also installs a global limiter (300 requests per minute per user or client) that applies to every request. The web app serves pages, static assets and the circuit's requests. A browser loading a board makes many of them, and the browser suite runs from one loopback address. One budget for all of them would throttle normal use.
2. **The response to a rejected form.** The kernel answers a rejection with JSON `{ error: "rate_limited" }`. A form cannot read that, and ADR 0022 says every account step answers with a redirect and a message.
3. **Sign-out.** Ending a session must work even when the user has made many sign-in attempts.

## Decision

- `AddStandardRateLimiting` takes `globalLimit: false` in the web app. The three policies are registered; the global limiter is not. The API services keep the default, so nothing else changes.
- The `/account` group requires `RateLimitPolicies.Auth`. Sign-out opts out with `DisableRateLimiting()`.
- A rejection of one of the form endpoints redirects to the form's page with the message "Too many attempts. Wait a minute, then try again." Any other rejection keeps the kernel's JSON.
- Clients are keyed by the address the forwarded-headers middleware resolves (ADR 0018), not by the raw connection, so one edge proxy does not put every visitor in one bucket.
- The directly started E2E estate raises `RateLimiting:AuthPermitLimit` to 1000, because its journeys sign in many times from one loopback address. The production default is covered by `AccountRateLimitingTests`.

## Consequences

- A shared address (a NAT or an office) shares the sign-in budget of 10 per minute. That is the price of keying on the address, and it is accepted: the limit exists to slow password guessing, and a user who is refused can wait a minute.
- Sign-out is not limited, so an attacker can always end a session they hold (no new risk: sign-out only ends the caller's own session).
- The web app has no global limit. A flood of page or circuit requests is not throttled by the app. The edge proxy is the place for that, and it is an owner decision at deployment (see DEPLOYMENT.md). This is recorded here so it is not assumed to exist.
- Tests: `AccountRateLimitingTests` pins the redirect, the message and the sign-out exemption at the production shape. The kernel's policy is covered by `RateLimitingTests`. Neither runs against the real edge proxy.
