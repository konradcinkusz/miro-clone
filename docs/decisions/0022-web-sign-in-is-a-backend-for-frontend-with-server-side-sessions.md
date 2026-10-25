# 0017 — Sign-in is a backend-for-frontend: native account forms, tokens in a server-side session, a cookie that names it

- **Status:** Accepted
- **Date:** 2026-10-25
- **Principles:** blazor-web-app §7 (sessions and authentication: tokens stay server-side, the host validates the identity provider's tokens when the cookie is issued, no `HttpContext` in components, circuit revalidation), §8 (origin guard), §14 (state in server memory, shared Data Protection keys); identity-and-accounts §1 (claims stamped at issuance), §10 (key material); P5 (tokens never reach the browser); ADR 0001 (Blazor over Next.js: the circuit is the BFF)

## Context

authservice is a credential API, not an OpenID Connect provider: it takes an email and a password and returns a token pair
(`POST api/v1/auth/login`). A Blazor circuit cannot write a cookie, because the response has already started (and the circuit has no
`HttpContext`). A token must not reach the browser, so the host keeps it.

## Decision

- **Account forms are plain forms.** The sign-in, sign-up, verification, password and consent pages are static server pages, outside the
  interactive router (`[ExcludeFromInteractiveRouting]`). Each form posts to a minimal endpoint under `/account/` (`AccountEndpoints`),
  which checks the antiforgery token, calls the account step (`AccountFlows`), writes the cookie and redirects. A circuit is not needed to sign in.
- **Tokens live in a server-side session.** `SessionService` keeps the access and refresh tokens in `IDistributedCache`, keyed by a random
  session id. The cookie (`miroclone.session`: HttpOnly, SameSite=Strict, Secure outside Development) holds the subject, the email, the
  verification flag and the session id, and nothing else. Tests assert that no token value is in the cookie principal.
- **The host validates the token when it takes it in.** `AccessTokenValidator` checks the RS256 signature against authservice's discovery
  keys, and the issuer, audience and lifetime, with the rules the services use (ADR 0007, ADR 0011). A token that fails is never stored.
- **The workspace is read from the stored token, not from the cookie**, so it is never older than the last refresh (see ADR 0024).
- **A circuit asks the store again.** `SessionRevalidatingAuthenticationStateProvider` checks every five minutes that the session still exists;
  a session that ended ends the circuit's sign-in.
- **Sign-out ends the session everywhere it can.** It calls authservice's logout, which revokes every refresh token of the user (its own
  semantics), then removes the stored session. The network call is best effort; the local session ends regardless.
- **Data Protection keys are persisted** to a directory (`DataProtection:KeyDirectory`, default `App_Data/keys`, gitignored; a volume in
  deployment), so a restart does not invalidate open forms or cookies (blazor-web-app §14).

## Consequences

- **Sessions are per process.** The distributed cache is memory: a restart or a second instance ends sessions. The estate runs one web
  instance until a shared cache is chosen; that choice is the one that lifts the restriction (blazor-web-app §14). Deployment (WP13) must
  say so.
- The session lifetime is absolute (`Session:Lifetime`, eight hours by default). Refresh extends the tokens, not the session.
- **Two-factor accounts cannot sign in yet.** authservice answers a second-factor challenge; the web app refuses it with a message rather than
  running the step.
- **Verification of a bearer token's claims on the BFF is a second check, not a replacement.** The services still validate every call (P5).
