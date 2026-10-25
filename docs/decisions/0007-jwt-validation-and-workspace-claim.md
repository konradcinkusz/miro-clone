# 0007 — Services validate authservice tokens against its JWKS, RS256 only, with the estate audience (amended by 0011)

- **Status:** Accepted. Amended by ADR 0011 (claim name and audience).
- **Date:** 2026-10-25
- **Principles:** P5 (JWT/OIDC with JWKS; configuration through the environment); identity-and-accounts guide §1 (claims enriched at
  issuance), §10 (key material: the private half belongs to the identity service only); ADR 0002 (authservice is the only identity provider)

## Context

Every service must validate tokens without holding a signing key. The claim names and the audience are not fixed by the
contracts: the README says only "claims give user id + active workspace (`WorkspaceClaims`)". ADR 0011 read the real names from authservice.

## Decision

- `AddJwtValidation` takes keys from `Auth:Authority`'s discovery document and JWKS. No signing key is configured.
- Tokens must be RS256 (`ValidAlgorithms`). An HS256 token is refused, even with a valid audience.
- The audience must equal `Auth:Audience`. ADR 0011 amends this from per service to one audience for the estate, because authservice mints one audience per token.
- `MapInboundClaims` is off, so claims keep their issued names: `sub` for the user, and `organization` for the workspace (ADR 0011).
- Clock skew defaults to 30 seconds and is capped at 5 minutes. `RequireHttpsMetadata` defaults to true.
- Invalid configuration (no absolute authority, empty audience) fails at startup.
- The JWKS path is tested end to end with a generated RSA key served from a local authority in the test host. No network is used.

## Consequences

- **Resolved by ADR 0011:** authservice stamps `organization`, not `workspace`. The claim name is aligned in `WorkspaceClaims`. Two organizations give no active workspace, which is an owner decision (ADR 0011, Consequences).
- Rotation is handled by the discovery and JWKS refresh, with no redeploy. The retired key remains the identity service's responsibility (identity guide §10).
