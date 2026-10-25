# AGENTS.md — miro-clone

Instructions for coding agents and contributors. `CLAUDE.md` imports this file.

## Layout

| Path | What it is | Status |
|---|---|---|
| `MiroClone.sln` | Solution (empty until WP01) | WP00 |
| `Directory.Build.props` | Shared TFM, nullable, warnings-as-errors, NuGet audit | WP00 |
| `Directory.Packages.props` | **The only place package versions are written** | WP00 |
| `global.json` | Pins the .NET 10 SDK | WP00 |
| `docs/implementation-plan/` | Work packages, sub-session protocol, frozen contracts | WP00 |
| `docs/decisions/` | ADRs (`NNNN-title.md`), each citing the principle it applies | WP00 |
| `docs/AUTH.md` | Claims, token rules and the dev sign-in flow against authservice | WP03 |
| `scripts/seed-dev-users.sh` | Seeds two dev users in one authservice organization over its API | WP03 |
| `.github/workflows/` | `pr-validation` (restore/build/test), `secret-scan` (gitleaks) | WP00 |
| `.githooks/` + `scripts/install-hooks.sh` | Pre-commit gitleaks hook (fails closed) | WP00 |
| `MiroClone.AppHost` | Aspire composition root: PostgreSQL (`boards`, `canvas`, `authservice`), pinned authservice image, services, web | WP02, WP03 |
| `MiroClone.ServiceDefaults` | Shared kernel (no domain types), ceiling 800 lines (ADR 0005) | WP01 |
| `MiroClone.BoardsService` | Boards and favourites, PostgreSQL `boards`: `/api/boards`, migrations, canvas purge client | WP04 |
| `MiroClone.CanvasService` | Canvas aggregate, JSON snapshot, SignalR `/hubs/canvas`. Skeleton: `/health`, migrations | WP02 (aggregate WP05) |
| `MiroClone.Web` | Blazor Web App, interactive Server; the BFF. Sign-in, server-side sessions, dashboard shell, visual token contract (`wwwroot/app.css`) | WP02, WP06 |
| `tests/` | One xUnit project per service; `Web.Tests` (bUnit); `Web.E2E` (Playwright, WP07+). `AppHost.Tests`, `BoardsService.Tests`, `CanvasService.Tests`, `ServiceDefaults.Tests` (WP01/WP02). `TestSupport`: local authservice stand-in and token factory, offline (WP03) | partial |

## Build and test

```bash
dotnet build MiroClone.sln     # must finish with 0 warnings
dotnet test MiroClone.sln      # PostgreSQL tests run only with TEST_POSTGRES set
```

Run `scripts/install-hooks.sh` once per clone so that secrets are caught before they are committed.

Run the estate for development with `scripts/run-local-stack.sh` (needs a Docker daemon for PostgreSQL).

## Rules that are easy to break

- **Versions only in `Directory.Packages.props`.** Projects reference packages without a `Version`.
- **Warnings are errors.** Do not suppress a warning to get green; fix it or record why in an ADR.
- **No secrets in the tree.** Local secrets live in gitignored `.env` or `dotnet user-secrets`. A fixture that looks like a secret carries `gitleaks:allow` on its line.
- **Tokens never reach the browser.** The Blazor circuit is the BFF; the cookie is httpOnly and SameSite=Strict. Do not add Blazor WebAssembly.
- **Another workspace's row answers 404, not 403.**
- **Kernel purity.** `MiroClone.ServiceDefaults` holds no entity, DTO, enum or user-facing string.
- **One database per service.** No service opens another service's database; cross-context reads go over HTTP.
- **Frozen contracts** (`docs/implementation-plan/README.md`) change only through an ADR, with both sides in one PR.
- **Reference repositories are read-only.** `AureliusMundus` is for learning; never copy from it.
- **Lists paginate** (`page`, `pageSize<=50`). JSON is camelCase.

## Commits and pull requests

- Commit prefixes: `auth:`, `boards:`, `canvas:`, `web:`, `build:`, `ci:`, `docs:`. Imperative summary; the body says why.
- PRs use `.github/pull_request_template.md`. Tick acceptance items only with evidence.
- A deviation from a standard, or an interpretation of one, gets a short ADR in `docs/decisions/`.
