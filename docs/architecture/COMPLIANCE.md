# Compliance with the reference architecture

As of 2026-10-26, for `main` at WP10 (#15).

The checklist is §3 of `docs/architecture/00-REFERENCE-ARCHITECTURE.md` in `konradcinkusz/architecture-standards`, at commit
`1f5ad43` (read in this session with a shallow clone). It has 19 items. Each row cites the evidence in this repository,
gives a status, and says what is missing. A row marked **Met** has code the item asks for. **Met, with a note** has that
code and a limit. **Partial**, **Deviation (recorded)** and **Not met** are open, and they are repeated in the register at
the end of this page (§3a of the standard requires that register).

## Verification boundary

This page rests on four kinds of evidence, and no others:

1. Reading the files named in each row.
2. The AppHost wiring test (`tests/MiroClone.AppHost.Tests/EstateWiringTests.cs`), which checks the model and not the running stack.
3. CI on PR #14 and PR #15, both green: `pr-validation` (including `docker-build` for the three images, which builds without pushing), and `secret-scan`.
4. The PR bodies for #12–#15.

No session that wrote this page had a Docker daemon, a .NET SDK or a browser, so **nothing below has been run live**. The
AppHost, the authservice container, the Fly apps and every browser flow are unverified (register R1, R6, R12).

## 1. Checklist

| # | Item (standard §3) | Status | Evidence | Note |
|---|---|---|---|---|
| 1 | Declared in the AppHost with `WithReference`, `WaitFor` and `WithHttpHealthCheck` | Met, with a note | `MiroClone.AppHost/EstateExtensions.cs` lines 54–59 (canvas), 64–70 (boards), 73–81 (web); authservice `WaitFor` at 47 and `WithHttpHealthCheck("/health/ready")` at 48 | Boards references Canvas but does not `WaitFor` it, to avoid a startup cycle (ADR 0013, ADR 0008). Model-tested in `EstateWiringTests.cs` only. Never started (R1) |
| 2 | Calls `AddServiceDefaults()` and `MapDefaultEndpoints()` | Met | `MiroClone.BoardsService/Program.cs` 6 and 18; `MiroClone.CanvasService/Program.cs` 7 and 18; `MiroClone.Web/Program.cs` 7 and 15 | — |
| 3 | Exposes `/health` and `/alive`; the platform health check points at `/health` | Met, with a note | `MiroClone.ServiceDefaults/ServiceDefaultsExtensions.cs` lines 51–52; `flyio/web.fly.toml` line 47, `boards-service.fly.toml` line 39, `canvas-service.fly.toml` line 38 use `/health` | The platform check has not run: Fly is disabled (ADR 0026, R5) |
| 4 | Emits OTLP traces, metrics and logs | Partial | `MiroClone.ServiceDefaults/ServiceDefaultsExtensions.cs`, `ConfigureOpenTelemetry`: logs, metrics and traces are added; the OTLP exporter is added only when `OTEL_EXPORTER_OTLP_ENDPOINT` is set | Not checked in a running stack. Whether the hub's `access_token` query value is recorded is open (R2) |
| 5 | Owns its database; no other service connects to it | Met | `EstateExtensions.cs` 29–32 (one database per service); `MiroClone.BoardsService/Persistence/BoardsDbContext.cs`; `MiroClone.CanvasService/Persistence/CanvasDbContext.cs`; cross-service calls are HTTP only: `CanvasService/Boards/BoardsAccessClient.cs`, `BoardsService/Canvas/CanvasPurgeClient.cs` | ADR 0003 |
| 6 | Schema applied by `MigrateAsync` from provider-specific migrations, in a hosted service | Deviation (recorded) for Canvas; Met for Boards | Boards: `MiroClone.ServiceDefaults/DatabaseMigrationExtensions.cs` line 31 (`MigrateAsync` in `StartingAsync`), `MiroClone.BoardsService/Persistence/Migrations/`. Canvas: idempotent DDL in `MiroClone.CanvasService/Persistence/CanvasSnapshotStore.cs` lines 38 and 48; its EF `Initial` migration is empty (`Persistence/Migrations/20261025134722_Initial.cs` lines 11–16) | Canvas stores one JSON snapshot table by design, not an ORM model (ADR 0015). Recorded; see R3 |
| 7 | All configuration from environment variables; no secret in source, config or comment; a secret scanner in CI | Met | Secret parameter: `EstateExtensions.cs` line 35 (`AddParameter("authservice-jwt-private-key", secret: true)`). Scanner in CI: `.github/workflows/secret-scan.yml` line 27 (gitleaks). Pre-commit: `.githooks/pre-commit`. Fixture with `gitleaks:allow`: `scripts/seed-dev-users.sh` | The seed password is a development value and is marked as such |
| 8 | Exactly one service holds a signing key; all others validate against its JWKS | Met | Only authservice receives the private key: `EstateExtensions.cs` line 35 and `Jwt__PrivateKeyPem`. Services validate through the discovery document: `MiroClone.ServiceDefaults/AuthenticationExtensions.cs` line 74 (`Authority`), line 84 (`ValidAlgorithms = RS256`). No signing credential in product code | Test signing keys live in `tests/MiroClone.TestSupport` only |
| 9 | Shared kernel holds no entity, DTO, enum, seed dataset, pricing constant or user-facing string; an architecture test and a CI size check | Partial | Architecture test: `tests/MiroClone.ServiceDefaults.Tests/ArchitectureTests.cs` lines 15 (no `MiroClone.*` reference), 26 (no enum), 34 (no entity or `DbContext`). Size check: `scripts/check-kernel-size.sh`, run first in `.github/workflows/pr-validation.yml` line 28; 633 of 800 lines | The test covers types, not strings. ADR 0005 says review enforces the string rule (R4) |
| 10 | Every optional integration has a working no-op or fallback | Met, with a note | The only conditional integration is the OTLP exporter, which is simply not added when its variable is unset (`ServiceDefaultsExtensions.cs`) | No service registers an optional integration: `AddOptionalIntegration` (`MiroClone.ServiceDefaults/OptionalIntegrations.cs` line 19) has no caller |
| 11 | `/health` reports the state of every optional integration; the startup banner prints the same list | Met, with a note | `OptionalIntegrations.cs` line 34 (`OptionalIntegrationsHealthCheck`), line 49 (`OptionalIntegrationBanner`), registered by `AddDefaultHealthChecks` and `AddHostedService` in `ServiceDefaultsExtensions.cs` | The list is empty today, so the report is empty. Nothing to report yet |
| 12 | Multi-stage Dockerfile; runtime image major version equals the TFM major; listens on `:8080`; non-root where the base allows | Met | `MiroClone.{BoardsService,CanvasService,Web}/Dockerfile`: `FROM …/sdk:10.0` then `…/aspnet:10.0` (multi-stage); `EXPOSE 8080`; `USER app`. TFM `net10.0` in `Directory.Build.props` line 9. `docker-build` jobs green on PR #14 and #15 | The authservice image is pinned and not built here (ADR 0011). Not run as a container (R1) |
| 13 | One `fly.toml`; `min_machines_running = 1` if another service calls it in-request | Met, with a note | `flyio/{web,boards-service,canvas-service,authservice}.fly.toml` at lines 35, 27, 26 and 30 (`min_machines_running = 1`); `flyio/postgres.fly.toml` has no HTTP service | Config only. Not deployed (ADR 0026) |
| 14 | Outbound `HttpClient`s carry the standard resilience handler with explicit timeouts | Met, with a note | Kernel: `ServiceDefaultsExtensions.cs` lines 34–37 (`AddStandardResilienceHandler`, attempt 10 s, total 30 s). Purge client: `BoardsService/Canvas/CanvasPurgeClient.cs` line 68 (8 s), line 70 (`AllowAutoRedirect = false`). Access client: `CanvasService/ServiceCollectionExtensions.cs` line 52 | The Web's typed clients (`Web/ServiceCollectionExtensions.cs` lines 97, 105, 107) take the kernel's timeouts and set none of their own |
| 15 | `Program.cs` is a manifest; wiring lives in `ServiceCollectionExtensions` | Met | `BoardsService/Program.cs` line 10 `AddBoardsService`; `CanvasService/Program.cs` line 10 `AddCanvasService`; `Web/Program.cs` line 10 `AddWebApp`. Wiring in `*/ServiceCollectionExtensions.cs` | `Program.cs` also orders the middleware, which the standard allows |
| 16 | Extension points are interfaces registered in DI, not base classes | Met | `ICanvasSession` (`Web/Canvas/Editing/ICanvasSession.cs`), `IRealtimeCanvasSession` and `ICanvasHubConnection` (`Web/Canvas/Realtime/`), `ICanvasPointerSource` (`Web/Canvas/Interop/`), `IBoardAccess` (`CanvasService/Boards/IBoardAccess.cs`), `IOptionalIntegration` (`ServiceDefaults/OptionalIntegrations.cs`) | No base class is used as an extension point in the files read |
| 17 | Has a test project; the logic-bearing layer is covered; characterisation tests are written before behaviour is migrated | Partial | Test projects for each service and for the Web app (`tests/`); 466 test methods, 562 cases run in CI on PR #15 (`TESTING.md` §2) | No characterisation tests were written before the reference's behaviour was ported. Parity was checked by hand (PR #14 table). No E2E project yet (R6, R7) |
| 18 | Built by the tag-driven workflow with path-based change detection | Not met | `.github/workflows/flyio.yml` has its change detection, but its only trigger is `workflow_dispatch` (`on:` at line 9). `pr-validation.yml` runs on every pull request, with no path filter | Owner decision, ADR 0026 (R5) |
| 19 | Its architectural decisions are recorded in `docs/` | Met, with defects | `docs/decisions/` holds 0001–0037 and 0038 (this record). Defects: no 0029; two files numbered 0011; headings disagree with file names for 0015, 0022–0025, 0035–0037 | Recorded in R13. Renumbering is not done here, because it would move every link |

## 2. Repo-baseline checks, §8 (documentation staleness)

These are not in the checklist, and they are part of every review (`repo-baseline` §8).

| Document | Claim | Result | Action |
|---|---|---|---|
| `README.md` | Status "skeleton (WP02)"; test command | Stale | Rewritten in this change |
| `docs/ARCHITECTURE.md` | — | New | Written from the code |
| `AGENTS.md` | Layout table statuses (`WP00`, "partial"); `dotnet test MiroClone.sln` | Partly stale | Register R14, R15 |
| `docs/AUTH.md` | Claims, routes, development sign-in | Matches the code | — |
| `docs/DEPLOYMENT.md` | Deployment disabled; owner steps | Matches ADR 0026 | — |
| `docs/decisions/0022-…` | Setting name `DataProtection:KeyDirectory` | Stale: the code uses `DataProtection:KeysPath` (`Web/ServiceCollectionExtensions.cs` line 73) | Register R13 |
| `docs/decisions/0013-…` | A sweep of orphan snapshots exists | Stale: no sweep is in the repository | Register R9 |

Environment variables: one authoritative place per variable is the `flyio/*.fly.toml` for Fly and `EstateExtensions.cs` for
local development. Both are named in `DEPLOYMENT.md` and `AUTH.md`.

## 3a. Open register

Every row is dated. A row goes away when it is fixed, and stays with its reasoning when it is accepted.

| # | Repo | Deviation | Principle | Since | Plan or owner |
|---|---|---|---|---|---|
| R1 | miro-clone | The AppHost and the authservice container have never been started. No session has had Docker | P1; ADR 0011 (verification boundary) | 2026-10-25 (WP02) | Owner or a session with Docker runs `scripts/run-local-stack.sh` once and records the result |
| R2 | miro-clone | OTLP export is unverified. The hub's `access_token` query value is not confirmed redacted in telemetry | P15, P5; ADR 0016 consequences | 2026-10-26 (WP10) | Verify in a run; add redaction if the instrumentation records the query |
| R3 | miro-clone | Canvas snapshot table is hand-written DDL, not EF migrations | P4; `state-snapshot-persistence` §6 | 2026-10-25 (WP05) | Accepted: ADR 0015 |
| R4 | miro-clone | User-facing strings in the kernel are not machine-checked | P2; ADR 0005 | 2026-10-25 (WP01) | Accepted: review enforces it (ADR 0005) |
| R5 | miro-clone | Tag-driven deploy and preview workflows are disabled, so there is no path-filtered build or deploy | P12, P7; `fly-io-deployment` | 2026-10-25 (WP13) | Owner decision, ADR 0026. Re-enabling needs a new ADR |
| R6 | miro-clone | No browser end-to-end suite is in the repository; no board has been driven in a browser | P13 | 2026-10-26 (WP08–WP11) | WP12 in progress |
| R7 | miro-clone | Behaviour ported from the reference was not characterised by tests before the port. Parity was checked by hand (PR #14) | P13 (characterisation before migration) | 2026-10-26 (WP08–WP11) | Accepted for now. Characterise before any further port |
| R8 | miro-clone | The web sign-in endpoints have no rate limit. The kernel's auth policy is defined and used by no service | `service-api-patterns` §1 | 2026-10-25 (WP06) | Apply `RateLimitPolicies.Auth` to `/account/*`. Needs a decision on the limit |
| R9 | miro-clone | Orphan canvas snapshots are never swept. The logs say "left for the sweep" | ADR 0013; `state-snapshot-persistence` §5 | 2026-10-25 (WP04, WP05) | Build the sweep, with a decision on the credential it uses |
| R10 | miro-clone | Erasure in authservice is not handled. Board author ids and favourite rows are kept | `identity-and-accounts` §8 | 2026-10-26 | Owner decision, PRIVACY.md §4 |
| R11 | miro-clone | Fly deploy has never run. Web and Canvas reach `boards-service` and `canvas-service` by discovery names, and `flyio/web.fly.toml` sets no boards address | `fly-io-deployment`; ADR 0017 | 2026-10-25 (WP13) | Verify at the owner's first deploy, and set the addresses |
| R12 | miro-clone | `dotnet test MiroClone.sln` is written in `AGENTS.md`, `PROTOCOL.md` and `pr-validation.yml`. The WP10 run's local SDK rejected it. CI passed on the same form | `repo-baseline` §8 | 2026-10-26 (WP10) | Confirm the CI SDK. Then fix `AGENTS.md` |
| R13 | miro-clone | ADR numbering: two files numbered 0011; no 0029; headings disagree with file names for 0015, 0022–0025 and 0035–0037; ADR 0022 names a setting the code does not use | P14; `repo-baseline` §8 | 2026-10-25 (WP02–WP10) | Renumber and correct the headings in one PR with their references |
| R14 | miro-clone | `AGENTS.md` layout table still shows WP00 and "partial" statuses | `repo-baseline` §8 | 2026-10-26 | Update in the next PR that touches `AGENTS.md` |
| R15 | miro-clone | Sessions and circuits live in one web instance's memory | `blazor-web-app` §14; ADR 0022, ADR 0019 | 2026-10-25 (WP06) | Shared session store, decided before any scale-out |
| R16 | miro-clone | Consent is checked at sign-in only, and two-factor sign-in is refused | `identity-and-accounts` §9; ADR 0025 | 2026-10-25 (WP06) | Follow-up under ADR 0025 |

R3, R4, R5 and R16 are accepted deliberately, with their ADRs. Each of the others is drift until its plan is carried out.
