# 0007 — Services validate authservice tokens against its JWKS, RS256 only, with a per-service audience

- **Status:** Accepted
- **Date:** 2026-10-25
- **Principles:** P5 (JWT/OIDC with JWKS; configuration through the environment); identity-and-accounts guide §1 (claims enriched at
  issuance), §10 (key material: the private half belongs to the identity service only); ADR 0002 (authservice is the only identity provider)

## Context

Every service must validate tokens without holding a signing key. The claim names and the audience are not fixed by the
contracts: the README says only "claims give user id + active workspace (`WorkspaceClaims`)".

## Decision

- `AddJwtValidation` takes keys from `Auth:Authority`'s discovery document and JWKS. No signing key is configured.
- Tokens must be RS256 (`ValidAlgorithms`). An HS256 token is refused, even with a valid audience.
- The audience must equal `Auth:Audience`, which is per service. A token minted for one service is refused by another.
- `MapInboundClaims` is off, so claims keep their issued names: `sub` for the user, and `workspace` for the active workspace.
- Clock skew defaults to 30 seconds and is capped at 5 minutes. `RequireHttpsMetadata` defaults to true.
- Invalid configuration (no absolute authority, empty audience) fails at startup.
- The JWKS path is tested end to end with a generated RSA key served from a local authority in the test host. No network is used.

## Consequences

- **Open item for the owner:** the workspace claim name `workspace` is this repository's choice. It must match the claim authservice actually stamps. Align it in authservice, or change `WorkspaceClaims.WorkspaceClaimType` in a single PR with both sides.
- Rotation is handled by the discovery and JWKS refresh, with no redeploy. The retired key remains the identity service's responsibility (identity guide §10).
