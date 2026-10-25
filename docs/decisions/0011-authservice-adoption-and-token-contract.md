# 0011 — authservice runs as a pinned image with its own database; the estate uses its claim names and one audience

- **Status:** Accepted
- **Date:** 2026-10-25
- **Principles:** P5 (configuration through the environment, secrets through the platform, JWT/OIDC with JWKS); P3 (a service owns its schema, no shared database); identity-and-accounts guide §1 (claims enriched at issuance) and §10 (key material belongs to the identity service); shared-service-reuse (adopt a shared service as published, never copy its source); ADR 0002 (authservice is the only identity provider); ADR 0007 (token validation, amended here)

## Context

WP03 adopts authservice. The facts below were read from authservice at its release tag `v0.3.4` (commit `dfcce91`), not assumed:

- The release publishes `ghcr.io/konradcinkusz/authservice:v0.3.4` (`docs/DEPLOYMENT.md`).
- A session token carries `sub`, `email_verified` (on sign-in and refresh), one `organization` claim per membership and one `organization:{id}:role` claim per membership (`TokenService.BuildClaimsAsync`). **There is no `workspace` claim and no active-organization claim.** ADR 0007 named `workspace`, which is wrong.
- A token carries one `aud`, taken from `Jwt:Audience`. One login therefore yields one audience, so a per-service audience cannot reach boards, canvas and web from the same token.
- The discovery document's `issuer` is `Jwt:Issuer`, and its keys are at `/.well-known/jwks.json`. `Jwt:Algorithm=RS256` with a PKCS#8 key selects RS256. An explicit algorithm wins over the `SecretKey` in authservice's `appsettings.Development.json`.
- Without a configured email provider, the dev outbox (`NoOpEmailService`) logs verification and invitation tokens in Development. `RequireConfirmedEmail` defaults to "only when mail can be sent", so registration issues tokens at once here.
- authservice user and organization ids are GUID strings, so `WorkspaceClaims` parses them as `Guid`.

## Decision

1. **Pinned image.** The AppHost runs `ghcr.io/konradcinkusz/authservice:v0.3.4` as a container (`EstateExtensions.AuthServiceTag`). It is not built from the sibling source. Moving to a newer release means changing that constant after reading its release notes.
2. **Its own database.** authservice gets the `authservice` database on the estate's PostgreSQL server (resource `authservice-db`), passed as `ConnectionStrings__DefaultConnection`. Development uses authservice's default `EnsureCreated`. Deployment uses `Database__SchemaMode=Migrate` (WP13).
3. **Signing key.** authservice signs RS256 with `Jwt__PrivateKeyPem`, supplied by the Aspire secret parameter `authservice-jwt-private-key`, which is read from user secrets. No shared secret exists in the estate. The `SecretKey` in authservice's Development settings is never used, because `Jwt__Algorithm=RS256` is set.
4. **One estate audience.** Issuer `MiroClone` and audience `miro-clone`, shared by every service. This **amends ADR 0007**: authservice mints one audience per token, so a per-service audience would need token exchange, which authservice does not offer. A token minted for another product is still refused, because its audience differs.
5. **Workspace claim is `organization`.** `WorkspaceClaims.WorkspaceId` returns an id only when a token carries exactly one organization. With two or more it returns null and the caller refuses, which fails closed. This **amends ADR 0007**: the name is `organization`, and the open item is resolved.
6. **Development relaxations live only in the AppHost.** `Auth__RequireHttpsMetadata=false` covers the http authority, and `ASPNETCORE_ENVIRONMENT=Development` makes the dev outbox log tokens. Deployments keep https metadata and do not run in Development.
7. **OAuth providers are off.** No OAuth credentials are configured or stored in the tree. authservice's optional no-op path applies.

## Consequences

- **Owner decision needed before WP06.** authservice has no notion of an active organization. A user in two organizations therefore has no workspace claim here, and board endpoints answer 404 for them. The workspace switcher needs either an active-organization claim from authservice or an organization-scoped token refresh. Either is a change to authservice, so it is an owner action.
- Invitation tokens reach a developer only through the dev outbox log. `scripts/seed-dev-users.sh` reads them from a log file or takes them from `INVITATION_TOKEN`.
- Before the first run the AppHost needs the RS256 key in user secrets. `docs/AUTH.md` gives the command.
- **Verification boundary.** This session has no Docker daemon, so the container path is verified at the model level (`EstateWiringTests`: image, wait edges, environment). Live behaviour was verified by running authservice `v0.3.4` from its tagged source against a local PostgreSQL 16, with boards-service on the same settings the AppHost passes. The image itself was not run.
