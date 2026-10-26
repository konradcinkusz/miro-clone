# AGENTS.md — miro-clone

Instructions for coding agents and contributors. `CLAUDE.md` imports this file.

## Layout

The last column says which work package built the path. Every path listed here exists on `main`.

| Path | What it is | Built in |
|---|---|---|
| `MiroClone.sln` | The solution: every project in the table below | WP00, WP01 |
| `Directory.Build.props` | Shared TFM, nullable, warnings-as-errors, NuGet audit | WP00 |
| `Directory.Packages.props` | **The only place package versions are written** | WP00 |
| `global.json` | Pins the .NET 10 SDK (`10.0.100`, `latestFeature`); verified with 10.0.113 | WP00 |
| `docs/implementation-plan/` | Work packages, sub-session protocol, frozen contracts | WP00 |
| `docs/decisions/` | ADRs (`NNNN-title.md`, number in the file name and the heading), each citing the principle it applies | WP00 onward |
| `docs/ARCHITECTURE.md`, `docs/UI-UX.md`, `docs/PRIVACY.md`, `docs/TESTING.md`, `docs/architecture/COMPLIANCE.md` | The design as built, the open UI backlog, personal data, what each test proves, the reference-architecture register | WP14 |
| `docs/ARCHITECTURE_REVIEW.md`, `docs/SECURITY_REVIEW.md` | The final audit findings and their status | WP15 |
| `docs/AUTH.md` | Claims, token rules and the dev sign-in flow against authservice | WP03 |
| `scripts/seed-dev-users.sh` | Seeds two dev users in one authservice organization over its API | WP03 |
| `.github/workflows/` | `pr-validation` (kernel guard, restore, build, per-project tests, E2E job, Docker builds), `secret-scan` (gitleaks) | WP00, WP12, WP13 |
| `flyio/` | Fly.io config, one `*.fly.toml` per app, `SECRETS.md`, `INFRASTRUCTURE-ANALYSIS.md` | WP13 |
| `MiroClone.*/Dockerfile`, `.dockerignore` | Production images; build context is the repository root | WP13 |
| `.github/workflows/flyio*.yml` | Fly workflows, **disabled by owner decision (ADR 0026)**: no automatic triggers, manual dispatch only. Previously tag deploy (`flyio.yml`), PR previews and teardown. Secrets come from environment `dev-fly` | WP13 |
| `scripts/flyio-pr-database.sh` | Creates or drops a preview's databases and role on the dev PostgreSQL | WP13 |
| `docs/DEPLOYMENT.md` | Owner steps, verification after the first deploy, operations | WP13 |
| `.githooks/` + `scripts/install-hooks.sh` | Pre-commit gitleaks hook (fails closed) | WP00 |
| `scripts/check-kernel-size.sh`, `scripts/run-local-stack.sh` | The kernel ceiling guard; the AppHost for development (needs Docker) | WP01, WP02 |
| `MiroClone.AppHost` | Aspire composition root: PostgreSQL (`boards`, `canvas`, `authservice`), pinned authservice image, services, web | WP02, WP03 |
| `MiroClone.ServiceDefaults` | Shared kernel (no domain types), ceiling 800 lines (ADR 0005) | WP01 |
| `MiroClone.BoardsService` | Boards and favourites, PostgreSQL `boards`: `/api/boards`, migrations, canvas purge client | WP04 |
| `MiroClone.CanvasService` | Canvas aggregate (`Canvas/`), write-behind JSON snapshot (`Persistence/`, ADR 0015), SignalR `/hubs/canvas` and REST `/api/canvas` (`Realtime/`, `Api/`, ADR 0016) | WP05 |
| `MiroClone.Web` | Blazor Web App, interactive Server; the BFF. Sign-in, server-side sessions, dashboard, board canvas, visual token contract (`wwwroot/app.css`) | WP02, WP06 to WP11 |
| `tests/` | One xUnit project per service; `Web.Tests` (bUnit, TestServer); `Web.E2E` (Playwright, starts the estate itself: ADR 0039, ADR 0041; skips or fails per ADR 0040); `AppHost.Tests`; `BoardsService.Tests`; `CanvasService.Tests`; `ServiceDefaults.Tests`; `TestSupport` (local authservice stand-in and token factory, offline) | WP01 to WP12 |

## Build and test

```bash
dotnet build MiroClone.sln                 # must finish with 0 warnings
dotnet test --solution MiroClone.sln       # PostgreSQL tests run only with TEST_POSTGRES set
```

The SDK 10.0.113 rejects `dotnet test MiroClone.sln` ("should be via `--solution`"), so use `--solution`. CI runs each unit
test project on its own with `dotnet test --project` (see `pr-validation.yml`), so a filter never leaves a project with zero tests.

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
