# miro-clone

A Miro-style collaborative whiteboard: sign in, keep boards on a dashboard, and draw and co-edit on a canvas with
live cursors and presence. This is a .NET 10 rebuild of the Next.js/Convex/Liveblocks original, built to the estate's
architecture standards.

**Status:** skeleton (WP02). The services start, migrate their databases and answer `/health`; the web app renders a placeholder page.
The implementation plan is in [`docs/implementation-plan/`](docs/implementation-plan/README.md).

## Prerequisites

- .NET SDK 10 (the version is pinned in [`global.json`](global.json))
- Git hooks (recommended): `scripts/install-hooks.sh`, then install [gitleaks](https://github.com/gitleaks/gitleaks#installing)

## Build and test

```bash
dotnet restore MiroClone.sln
dotnet build MiroClone.sln
dotnet test MiroClone.sln
```

To run the estate (PostgreSQL in Docker, both services, the web app):

```bash
scripts/run-local-stack.sh
```

## Layout and conventions

See [`AGENTS.md`](AGENTS.md) for the layout table and the rules that are easy to break.

## Decisions

Architecture decision records live in [`docs/decisions/`](docs/decisions/):

- [0001](docs/decisions/0001-blazor-web-app-over-nextjs.md) Blazor Web App over Next.js
- [0002](docs/decisions/0002-authservice-as-only-identity-provider.md) authservice as the only identity provider
- [0003](docs/decisions/0003-service-and-database-per-context.md) One service and database per bounded context
- [0004](docs/decisions/0004-scope-excludes-agents-chat-and-simulation.md) Scope excludes agents, chat and simulation
- [0005](docs/decisions/0005-servicedefaults-kernel-scope.md) ServiceDefaults holds plumbing only, under a mechanical ceiling
- [0006](docs/decisions/0006-origin-guard-is-a-mitigation-not-authentication.md) The origin guard is a mitigation, not authentication
- [0007](docs/decisions/0007-jwt-validation-and-workspace-claim.md) JWT validation against authservice's JWKS, RS256, per-service audience
- [0008](docs/decisions/0008-aspire-apphost-sdk-and-endpoints.md) The AppHost uses the Aspire AppHost SDK, pinned in global.json
- [0009](docs/decisions/0009-migrate-on-startup-in-the-kernel.md) Schema migration on startup is one kernel hosted service
- [0010](docs/decisions/0010-generated-migrations-excluded-from-style-rules.md) EF Core migration files are generated code

## Licence

Not yet chosen. This repository has never contained a LICENSE file; the owner decides.
