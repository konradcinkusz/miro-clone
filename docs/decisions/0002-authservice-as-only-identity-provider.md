# 0002 — authservice is the only identity provider, consumed as an external service

- **Status:** Accepted
- **Date:** 2026-10-25
- **Principles:** P5 (configuration through the environment; secrets through the platform; JWT/OIDC with JWKS);
  P3 (a service owns its schema); identity-and-accounts guide

## Context

The estate already runs `konradcinkusz/authservice`, which owns users, organizations and signing keys. Copying its
code into this tree would fork identity, and a second identity store would split accounts.

## Decision

- authservice is the only identity provider. It is adopted as-is, pinned by image, and never copied into this repository.
- It runs with its own database and signing key. This repository holds no user table and no signing key.
- Workspace = authservice organization. Claims carry the user id and the active workspace (`WorkspaceClaims`).
- Services validate tokens against authservice's JWKS (`MiroClone.ServiceDefaults`). The Web app keeps tokens server-side (ADR 0001).

## Consequences

- Local development needs authservice running (Aspire AppHost, WP01+). Until it is wired, identity is not testable end to end.
- Account features (sign-up, password reset, profile) are authservice's; this repository never builds them.
