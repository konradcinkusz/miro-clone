# 0023 — Token refresh is single-flight per session; a refused refresh ends the session, an outage keeps it

- **Status:** Accepted
- **Date:** 2026-10-25
- **Principles:** identity-and-accounts §2 (refresh tokens are single-use and rotated; presenting a spent one is a signal, so global
  revocation follows); blazor-web-app §7 (a `DelegatingHandler` has its own scope, so the token service is resolved there, and the
  session is passed on the request, never injected into the handler); blazor-web-app §5 (exactly one resilience handler per client)

## Context

Several calls can need a refresh at once: a page loads two services in parallel, or a circuit renders twice. authservice rotates the refresh
token on every use. If two requests spend the same refresh token, the second is a replay, and authservice treats a replay as a theft signal.
A refresh that fails for reasons other than the session ending (an outage) must not log the user out.

## Decision

- **One refresh at a time per session.** `SessionService` takes a striped lock (32 stripes) before it refreshes, then reads the session again:
  a caller that waited finds the token already renewed and uses it. Ten concurrent callers cause one refresh (`SessionServiceTests`).
- **The rotated refresh token is stored at once**, with the new access token and the claims read from it.
- **400 or 401 from authservice ends the session**: authservice no longer accepts it. The stored session is removed, and the caller gets no
  token, so the circuit sends the user to sign in.
- **Any other failure keeps the session and throws.** The page shows its alert; the user is not signed out by an outage.
- **The refresh call is made from a scope of its own** (`IServiceScopeFactory`), because the refresh runs under a lock, outside any circuit's scope.
- **The refresh is never retried by the resilience handler.** It is a POST, which the standard handler does not retry; a retried spend of the
  same refresh token would be a replay.
- **The token is attached by `BearerTokenHandler`**, which reads the session id from the request options. A session that has ended gets a
  401 without leaving the process.

## Consequences

- The lock is per process. It is correct for one web instance (ADR 0022); a second instance needs a shared lock as well as a shared store.
- A token that authservice issues but this app cannot validate ends the session rather than being stored (`ARefreshedTokenThatFailsValidationEndsTheSession`).
