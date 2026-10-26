# 0025 — The account lifecycle runs through authservice: verification, the current consent versions, reset, and consent at sign-in

- **Status:** Accepted
- **Date:** 2026-10-25
- **Principles:** identity-and-accounts §5 (enumeration safety: the forgot-password answer is the same whether or not the address exists),
  §9 (versioned legal consent: the versions come from configuration, acceptance is recorded, and the session says when one is owed);
  blazor-web-app §5 (errors as state); ADR 0002 (authservice owns accounts; this repository builds none of them)

## Context

authservice owns sign-up, verification, password reset and consent (ADR 0002). Its register request requires the current terms and privacy
versions. The web app is the only place a person sees these pages, so it must take the versions from authservice, not from its own
configuration, which would drift.

## Decision

- **Sign-up sends the versions authservice currently requires,** read at the moment of submission (`GET api/v1/auth/consents/versions`),
  and refuses without both acceptances. The page shows the same versions.
- **A registration that needs verification creates no session.** The user is sent to the verification page with the address; the token
  arrives by the authservice email (in development, the authservice log).
- **Verification, resend, forgot-password and reset are thin pass-throughs.** Forgot-password always answers with the generic line, as
  authservice does, so the page never reveals an account.
- **Consent is checked at sign-in.** When authservice says consent is owed (`requiresConsent` on `api/v1/auth/me`), the user gets a session
  and is sent to the consent page, which records acceptance of both documents. A failure to read the flag ends the sign-in (fail closed).
- **Consent is not re-checked on every page.** It is checked at sign-in only, so a user who keeps a session after a version changes keeps
  it until they next sign in. This is a deliberate narrowing, recorded here, and a candidate for the dashboard work (WP08).
- **Reset-password carries the token in the page address.** It is the token from the email, the same one the email link carries, and it
  is spent by use. The address then names the form that retries.

## Consequences

- An account created before a version change must accept the new versions at its next sign-in.
- Two-factor is refused, with a message, until a step for it is designed.
