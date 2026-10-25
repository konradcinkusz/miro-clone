# 0017 — The Fly.io estate: one app per service, a consumed authservice instance, and one PostgreSQL with a role per database

- **Status:** Accepted
- **Date:** 2026-10-25
- **Principles:** P7 (deploy to Fly.io, one app per service); P12 (tag-driven pipeline); P3 (one database per service); P5 (configuration
  through the environment, secrets through the platform); P6 (multi-stage Dockerfile); `fly-io-deployment` §3–§9, §11;
  `shared-service-reuse` §1–§5 (independent instances, pinned artifact, consumer-named, database per instance)

## Context

WP13 deploys four services and their substrate to Fly.io: the Web BFF, BoardsService, CanvasService, a PostgreSQL server, and authservice.
The standards settle most of the shape. Four points were open and each is decided here.

1. **Who runs authservice.** `shared-service-reuse` says a consumer runs its own instance, with its own compute, database and signing key.
   The aurelius-promptus estate runs its own authservice, and sharing that instance would mean sharing a trust root.
2. **Where databases live.** `shared-service-reuse` §5 allows co-location on one PostgreSQL server, provided each database has its own role
   and there are no cross-grants.
3. **Names.** `fly-io-deployment` §4 names apps `<system>-<service>-<env>`, and `shared-service-reuse` §4 names a consumer's instance after
   the consumer.
4. **Images.** Services are built here. authservice is pulled as a pinned release image and never built.

## Decision

- **Apps:** `miroclone-postgres-dev`, `miroclone-authservice-dev`, `miroclone-canvas-dev`, `miroclone-boards-dev`, `miroclone-web-dev`, all in
  region `fra`. Each has one `flyio/<name>.fly.toml`. Names are globally unique on Fly. A clash is an owner action before the first tag.
- **authservice** is `ghcr.io/konradcinkusz/authservice:v0.3.4`, pinned by `flyio/authservice.fly.toml` and by `EstateExtensions.AuthServiceTag`.
  The `test` job of `flyio.yml` fails when the two differ. Its database is `authservice` on the estate's PostgreSQL, and its RS256 key is a
  secret, `AUTHSERVICE_JWT_PRIVATE_KEY`, that the owner generates for this instance alone. No key is taken from another estate.
- **PostgreSQL 17** (`postgres:17-alpine`) is private: no `[http_service]`, reached at `miroclone-postgres-dev.internal:5432`. The init script
  (run on first boot only) creates three login roles, `boards_app`, `canvas_app` and `authservice_app`, and three databases, each owned by its
  role, with `CONNECT` revoked from `PUBLIC`. Each role's password is a Fly secret. A service receives only its own connection string.
- **Build once, deploy by image.** Boards, Canvas and Web are built once in CI and deployed with `--image registry.fly.io/<app>:<tag>`.
  Every Dockerfile uses the repository root as its build context (`.dockerignore` at the root), with the restore layer separate from the
  source, the SDK and runtime images on .NET 10, and the non-root `app` user on port 8080.
- **Public or private.** Boards, Canvas, Web and authservice have public URLs, because the JWT issuer's JWKS must be reachable by each validator
  (`fly-io-deployment` §6). Only the database is private.

## Consequences

- **Owner actions before the first tag:** create the GitHub environment `dev-fly`, set `FLY_API_TOKEN` and the secrets in `flyio/SECRETS.md`,
  and confirm that the four app names are free. `docs/DEPLOYMENT.md` lists them.
- **Cost:** four always-on machines plus the database, because every application service is on a synchronous path (ADR 0020). `flyio/INFRASTRUCTURE-ANALYSIS.md` gives the figures.
- **Trade-off:** the database uses `SSL Mode=Disable`. The 6PN mesh is WireGuard-encrypted, and the image has no TLS certificates. Enabling TLS on the
  image is a later hardening step.
- **Rotating a role password** is an `ALTER ROLE` by the owner. The init script runs only on an empty volume, so a changed secret does not
  reach the database by redeploy.
- **Trust:** a consumer's authservice is trusted only by that consumer, which is the point of §1. Upgrading the pin is a reviewed change to one line
  and its ADR, not a live upgrade.
