# miro-clone

A Miro-style collaborative whiteboard: sign in, keep boards on a dashboard, and draw and co-edit on a canvas with
live cursors and presence. This is a .NET 10 rebuild of the Next.js/Convex/Liveblocks original, built to the estate's
architecture standards.

**Status:** repository baseline (WP00). The solution is empty; nothing runs yet. The implementation plan is in
[`docs/implementation-plan/`](docs/implementation-plan/README.md).

## Prerequisites

- .NET SDK 10 (the version is pinned in [`global.json`](global.json))
- Git hooks (recommended): `scripts/install-hooks.sh`, then install [gitleaks](https://github.com/gitleaks/gitleaks#installing)

## Build and test

```bash
dotnet restore MiroClone.sln
dotnet build MiroClone.sln
dotnet test MiroClone.sln
```

With no projects in the solution yet, CI skips these steps.

## Layout and conventions

See [`AGENTS.md`](AGENTS.md) for the layout table and the rules that are easy to break.

## Decisions

Architecture decision records live in [`docs/decisions/`](docs/decisions/):

- [0001](docs/decisions/0001-blazor-web-app-over-nextjs.md) Blazor Web App over Next.js
- [0002](docs/decisions/0002-authservice-as-only-identity-provider.md) authservice as the only identity provider
- [0003](docs/decisions/0003-service-and-database-per-context.md) One service and database per bounded context
- [0004](docs/decisions/0004-scope-excludes-agents-chat-and-simulation.md) Scope excludes agents, chat and simulation

## Licence

Not yet chosen. This repository has never contained a LICENSE file; the owner decides.
