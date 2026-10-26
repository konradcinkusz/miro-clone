# Compliance with the reference architecture

As of 2026-10-26, for `main` after WP15 (PR #21, with the review in `docs/ARCHITECTURE_REVIEW.md` and `docs/SECURITY_REVIEW.md`).

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

WP15 had a .NET 10 SDK, a PostgreSQL 16 server, Chromium and a Docker daemon. It ran the unit, bUnit, TestServer and PostgreSQL
suites, and a browser against a Release build of `MiroClone.Web` with real requests. It did **not** run the AppHost, the pinned
authservice image or any authenticated browser flow: the registry refused the image and Docker Hub rate-limited the PostgreSQL
image (register R1, R6). The Fly apps have never been deployed (R5, R11).

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
| 17 | Has a test project; the logic-bearing layer is covered; characterisation tests are written before behaviour is migrated | Partial | Test projects for each service and for the Web app (`tests/`); about 500 test methods (WP15 count). WP15 local run: 0 failed with `TEST_POSTGRES` set (`TESTING.md` §2) | No characterisation tests were written before the reference's behaviour was ported. Parity was checked by hand (PR #14 table). A browser suite runs in CI for the page journeys; the authenticated journeys are opt-in (R6, R7) |
| 18 | Built by the tag-driven workflow with path-based change detection | Not met | `.github/workflows/flyio.yml` has its change detection, but its only trigger is `workflow_dispatch` (`on:` at line 9). `pr-validation.yml` runs on every pull request, with no path filter | Owner decision, ADR 0026 (R5) |
| 19 | Its architectural decisions are recorded in `docs/` | Met | `docs/decisions/` holds 0001–0047. The defects of WP14 (two files numbered 0011, no 0029, headings that disagreed with file names) were fixed in WP15 (R13) | — |

## 2. Repo-baseline checks, §8 (documentation staleness)

These are not in the checklist, and they are part of every review (`repo-baseline` §8).

| Document | Claim | Result | Action |
|---|---|---|---|
| `README.md` | Status "skeleton (WP02)"; test command | Stale | Rewritten in this change |
| `docs/ARCHITECTURE.md` | — | New | Written from the code |
| `AGENTS.md` | Layout table statuses (`WP00`, "partial"); `dotnet test MiroClone.sln` | Fixed in WP15 | Register R12, R14 (closed) |
| `docs/AUTH.md` | Claims, routes, development sign-in | Matches the code | — |
| `docs/DEPLOYMENT.md` | Deployment disabled; owner steps | Matches ADR 0026 | — |
| `docs/decisions/0022-…` | Setting name `DataProtection:KeyDirectory` | Fixed in WP15: the setting is `DataProtection:KeysPath` (`Web/ServiceCollectionExtensions.cs`) | Register R13 (closed) |
| `docs/decisions/0013-…` | A sweep of orphan snapshots exists | Corrected by ADR 0044: no sweep is built, and the logs say so | Register R9 (decision open) |

Environment variables: one authoritative place per variable is the `flyio/*.fly.toml` for Fly and `EstateExtensions.cs` for
local development. Both are named in `DEPLOYMENT.md` and `AUTH.md`.

## 3a. Open register

Every row is dated. A row goes away when it is fixed, and stays with its reasoning when it is accepted. Rows closed in WP15 stay here,
marked closed, with the evidence that closed them.

| # | Repo | Deviation | Principle | Since | Plan or owner |
|---|---|---|---|---|---|
| R1 | miro-clone | **Open.** The AppHost and the authservice container have never been started. WP15 had a Docker daemon, but Docker Hub returned 429 for `postgres:18.3`, the ghcr blob for the authservice image was refused (403), and building authservice from its source was refused by the session's policy | P1; ADR 0011 (verification boundary) | 2026-10-25 (WP02) | A session or the owner with registry access runs `scripts/run-local-stack.sh` once and records it |
| R2 | miro-clone | **Partly closed (ADR 0047).** The hub's `access_token` is removed from span URL tags before export, and a test proves it. Still open: log messages and exception text are not scrubbed, and the OTLP export has not run live | P15, P5; ADR 0016, ADR 0047 | 2026-10-26 (WP10) | Verify the export in a run; decide the socket ticket (R18) |
| R3 | miro-clone | Canvas snapshot table is hand-written DDL, not EF migrations | P4; `state-snapshot-persistence` §6 | 2026-10-25 (WP05) | Accepted: ADR 0015 |
| R4 | miro-clone | User-facing strings in the kernel are not machine-checked | P2; ADR 0005 | 2026-10-25 (WP01) | Accepted: review enforces it (ADR 0005) |
| R5 | miro-clone | Tag-driven deploy and preview workflows are disabled, so there is no path-filtered build or deploy | P12, P7; `fly-io-deployment` | 2026-10-25 (WP13) | Owner decision, ADR 0026. Re-enabling needs a new ADR |
| R6 | miro-clone | **Partly closed.** A browser suite runs in CI (`e2e` job): the page journeys, including the account forms after the circuit connects (ADR 0042). **Open:** the 12 authenticated journeys (sign-up to dashboard, boards, live two-browser sessions) skip unless `MIROCLONE_E2E_AUTHENTICATED=1`, because the authservice image could not be run in WP15 | P13; ADR 0041 | 2026-10-26 (WP08–WP12) | Run them in CI with the pinned authservice; then remove the opt-in gate |
| R7 | miro-clone | Behaviour ported from the reference was not characterised by tests before the port. Parity was checked by hand (PR #14) | P13 (characterisation before migration) | 2026-10-26 (WP08–WP11) | Accepted for now. Characterise before any further port |
| R8 | miro-clone | **Closed (WP15, ADR 0045).** The account forms require the kernel's auth policy (10 per client per minute) and redirect with a message when rejected; sign-out is exempt. Evidence: `AccountRateLimitingTests`; live, the 11th and 12th sign-ins redirected. Boards and Canvas APIs keep the global per-user limit (S-10, below) | `service-api-patterns` §1 | 2026-10-25 (WP06) | Closed |
| R9 | miro-clone | **Corrected, decision open (WP15, ADR 0044).** No sweep of orphan canvas snapshots exists, and the logs and docs now say so. Orphans are kept indefinitely; they are not read, because the access check answers 404 first | ADR 0013; `state-snapshot-persistence` §5; PRIVACY.md §5 | 2026-10-25 (WP04, WP05) | Owner decision on reclaim (listing or service credential), PRIVACY.md §6 |
| R10 | miro-clone | Erasure in authservice is not handled. Board author ids and favourite rows are kept | `identity-and-accounts` §8 | 2026-10-26 | Owner decision, PRIVACY.md §4 |
| R11 | miro-clone | Fly deploy has never run. Web and Canvas reach `boards-service` and `canvas-service` by discovery names, and `flyio/web.fly.toml` sets no boards address | `fly-io-deployment`; ADR 0017 | 2026-10-25 (WP13) | Verify at the owner's first deploy, and set the addresses |
| R12 | miro-clone | **Closed (WP15).** `dotnet test MiroClone.sln` is rejected by the pinned SDK (10.0.113: "should be via `--solution`"). `AGENTS.md`, `PROTOCOL.md`, the PR template and `TESTING.md` now use `--solution`. Correction: `pr-validation.yml` never ran the solution form; it runs each project with `--project`. The disabled `flyio.yml` still writes the solution form (outside this change, per the Fly constraint) | `repo-baseline` §8 | 2026-10-26 (WP10) | Closed. Owner may update `flyio.yml` |
| R13 | miro-clone | **Closed (WP15).** ADR numbering: the canvas-core record is `0029` (it was a second `0011`); ten headings now match their file names; the README index lists every ADR; ADR 0022 names the setting the code reads (`DataProtection:KeysPath`) | P14; `repo-baseline` §8 | 2026-10-25 (WP02–WP10) | Closed |
| R14 | miro-clone | **Closed (WP15).** `AGENTS.md` layout table now shows the work package that built each path, with no "partial" statuses | `repo-baseline` §8 | 2026-10-26 | Closed |
| R15 | miro-clone | Sessions, circuits and the offline outbox live in one web instance's memory | `blazor-web-app` §14; ADR 0022, ADR 0019 | 2026-10-25 (WP06) | Shared session store, decided before any scale-out |
| R16 | miro-clone | Consent is checked at sign-in only, and two-factor sign-in is refused | `identity-and-accounts` §9; ADR 0025 | 2026-10-25 (WP06) | Follow-up under ADR 0025 |
| R17 | miro-clone | **New (WP15).** Sign-up in authservice answers a duplicate email with Identity's "already taken" text, which the web app shows. A script can list the emails that have accounts (SECURITY_REVIEW S-8). The forgot-password answer is generic | `identity-and-accounts` (enumeration) | 2026-10-26 | Owner or authservice change: one answer for new and taken addresses |
| R18 | miro-clone | **New (WP15).** The hub's access token travels in the WebSocket URL (ADR 0016). It can appear in edge request logs | ADR 0016; SECURITY_REVIEW S-7 | 2026-10-26 | Owner decision: a short-lived ticket or a header-capable transport (needs an ADR) |
| R19 | miro-clone | **New (WP15).** The Web app has no request limit except the account forms (ADR 0045). The Boards and Canvas APIs have only the global per-user limit, with no endpoint policy (SECURITY_REVIEW S-9, S-10) | `service-api-patterns` §1; security-review §9 (blocker 3) | 2026-10-26 | Owner decision: an edge limit at deploy, and the `Api` policy's numbers on the list and create endpoints |

R3, R4, R5, R7 and R16 are accepted deliberately, with their ADRs or rows. R8, R12, R13 and R14 are closed with evidence. R9, R10, R17, R18 and R19 are owner decisions; each stays open until the owner decides. Each of the others is drift until its plan is carried out.
