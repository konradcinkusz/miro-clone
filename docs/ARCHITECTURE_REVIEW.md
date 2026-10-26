# Architecture review (WP15)

As of 2026-10-26, for `main` after PR #21 (WP15). Method: the reference architecture's principles P1–P15 and its §3 compliance checklist (`konradcinkusz/architecture-standards`, `docs/architecture/00-REFERENCE-ARCHITECTURE.md`, read in this session), plus the repository's own register in [`COMPLIANCE.md`](architecture/COMPLIANCE.md). The skill named in the brief (`architecture-core:architecture-review`) is not available in this session, so the standards repository was read directly, as the brief allowed.

Verification boundary: everything below was checked by reading the code and by running the unit, integration (TestServer and PostgreSQL-free) tests, the browser suite against a locally started Web app, and a live Chromium session against the Release build of `MiroClone.Web`. **The AppHost was not started** (see A-9), and the authenticated flows were not run (the authservice image could not be obtained in this session).

## 1. Ranked findings

Status key: **Fixed** (merged, with the PR and ADR), **Open-decision** (needs the owner), **Open-accepted** (recorded deviation, ADR or COMPLIANCE row), **Open-unverified** (could not be run here).

| # | Sev | Finding | Principle / guide | Status |
|---|---|---|---|---|
| A-1 | Blocking | Account pages were excluded from the interactive router, so `/sign-up` and `/sign-in` showed "Page not found" once the circuit connected. The WP12 journeys could not reach a dashboard | blazor-web-app §3, §13; ADR 0022 | **Fixed**, PR #18, ADR 0042. Reproduced and fixed in Chromium; a page-only browser journey guards it |
| A-2 | Should-fix | Any workspace member could purge a live board's canvas through CanvasService (ADR 0016 rule was ownership only) | service-api-patterns §5; ADR 0003 | **Fixed**, PR #19, ADR 0043 |
| A-3 | Should-fix | ADR numbering: two `0011`, no `0029`, ten headings disagreed with file names; README index incomplete | P14; repo-baseline §8 | **Fixed**, PR #21 |
| A-4 | Should-fix | Documents and the PR template named a test command the pinned SDK rejects; COMPLIANCE R12 misstated `pr-validation.yml` | repo-baseline §8; P14 | **Fixed**, PR #21 |
| A-5 | Should-fix | The auth policy (`RateLimitPolicies.Auth`) was defined and used by no service; account forms unlimited (R8) | service-api-patterns §1 | **Fixed**, PR #20, ADR 0045 |
| A-6 | Should-fix | An unreachable authservice made sign-in answer 500, against ADR 0022's rule that account steps redirect with a message | ADR 0022; P8 (degrade, do not fail) | **Fixed**, PR #20 |
| A-7 | Should-fix | Drag committed after a remote delete re-created the layer for the whole room (ADR 0035 gap) | ADR 0031, ADR 0035 | **Fixed**, PR #21 |
| A-8 | Should-fix | The offline outbox was unbounded (ADR 0035 named the cap as the first change) | ADR 0035; P15 (no unbounded state) | **Fixed**, PR #21, ADR 0046 |
| A-9 | Blocking for first deploy | The AppHost has never been started (R1). This session has a Docker daemon, but Docker Hub returned 429 for `postgres:18.3` and the authservice image blob was refused (403) by the egress policy; building authservice from its source was refused by this session's policy | P1; ADR 0011 | **Open-unverified**. A session or owner with registry access runs `scripts/run-local-stack.sh` once and records it |
| A-10 | Medium | Canvas snapshots: the ADR 0013 sweep was described but never built (R9). Orphans are kept indefinitely | state-snapshot-persistence §5 | **Open-decision** (orphan reclaim needs a listing or service credential). Logs and docs corrected, ADR 0044 |
| A-11 | Medium | Erasure: board author id and name and favourites survive erasure in authservice (R10) | identity-and-accounts §8 | **Open-decision** (PRIVACY.md §6) |
| A-12 | Medium | Sessions, circuits and the outbox live in one web instance's memory (R15) | blazor-web-app §14; ADR 0019, ADR 0022 | **Open-accepted** until a second instance is planned. Must be decided before any scale-out |
| A-13 | Medium | Behaviour ported from the reference was never characterised by tests before the port (R7) | P13 | **Open-accepted** (ADR-less; parity was checked by hand in PR #14) |
| A-14 | Medium | Two-factor sign-in is refused and consent is checked only at sign-in (R16) | identity-and-accounts §9; ADR 0025 | **Open-accepted**; follow-up under ADR 0025 |
| A-15 | Low | Canvas schema is hand-written DDL, not EF migrations (R3) | P4; ADR 0015 | **Open-accepted** (ADR 0015). The DDL is a constant with no interpolation, checked by grep |
| A-16 | Low | Kernel user-facing strings are not machine-checked (R4) | P2; ADR 0005 | **Open-accepted** (review enforces it) |
| A-17 | Low | Boards references Canvas without `WaitFor` (startup cycle) | P1; ADR 0008, ADR 0013 | **Open-accepted** |
| A-18 | Low | Fly service discovery names are unverified; the Web sets no boards address (R11) | P7; ADR 0017 | **Open-unverified** until the owner's first deploy |
| A-19 | Low | Documentation drift: test counts, the ADR index, the README status, AGENTS statuses | P14 | **Fixed** in the docs PR of this package (see the register) |
| A-20 | Info | The Web test host and the E2E estate raise or lift limits (`AuthPermitLimit=1000` for the directly started estate; no global limiter in the Web) | service-api-patterns §1 | **Accepted**, ADR 0045 |

## 2. Detail for the blocking and the should-fix findings

**A-1 (fixed).** `Components/Pages/Account/*.razor` carried `@attribute [ExcludeFromInteractiveRouting]`. `App.razor` renders `<Routes @rendermode="InteractiveServer" />`, so the circuit that starts on `/sign-up` has no route for it and renders `NotFound` over the server-rendered form. Reproduced in Chromium against the Release build before and after the fix. Fix: attribute removed (ADR 0042, which amends ADR 0022). The pin test asserts the opposite now.

**A-9 (open, blocking for the first deploy).** The AppHost composes the estate. Its wiring is tested at model level only (`EstateWiringTests`). In this session: Docker starts, PostgreSQL 16 runs on the host, but the pinned containers could not be pulled. The attempt to build authservice from its tagged source and run it was refused by this session's policy, so it is not done here. The direct-process estate (services from built output, without the AppHost) was run for the page-only journeys, and the authenticated journeys were not.

## 3. Positive findings (checked, and sound)

- Database per service and HTTP-only cross-service reads: `EstateExtensions.cs` and `CanvasService/Boards/BoardsAccessClient.cs`; no service opens another's connection string.
- The kernel holds no entity, DTO or enum, and an architecture test enforces it. Kernel size 672 of 800 lines (`check-kernel-size.sh`).
- One signing key (authservice), RS256 only, JWKS validation in every service; one audience for the estate (ADR 0007, ADR 0011).
- Tokens never reach the browser: the cookie carries a session id only; a test asserts no token in the principal (ADR 0022).
- Outbound clients have explicit timeouts and do not follow redirects (`CanvasPurgeClient`, `BoardsAccessClient`).
- Forwarded headers: one hop, known proxies cleared, so the client address the limiter uses is the one the edge saw (ADR 0018).
- Dockerfiles: multi-stage, `USER app`, port 8080; `docker-build` jobs green on every PR.
- Secret scanning in CI and pre-commit (gitleaks); NuGet audit in `Directory.Build.props` (`NuGetAuditMode=all`, `TreatWarningsAsErrors`).
- Warnings are errors; the solution builds with 0 warnings.

## 4. Not applicable (with the evidence)

| Category | Why it does not apply | Evidence |
|---|---|---|
| XML parsing (XXE) | No XML is parsed | `grep -rln "XmlDocument\|XmlReader\|XDocument\|XmlSerializer" MiroClone.*` → no source |
| Raw SQL with user input | Raw SQL is only the constant canvas DDL; all reads use EF or the snapshot store's parameters | `grep -rn "FromSqlRaw\|ExecuteSqlRaw" MiroClone.*` → `CanvasSnapshotStore.cs` 54–55, constant `CreateTableSql` |
| Untrusted deserialisation | No binary or polymorphic deserialisation | `grep -rln "BinaryFormatter\|TypeNameHandling"` → none |
| File upload | No upload endpoint or browser-file input | `grep -rln "IFormFile\|IBrowserFile"` → none |
| CORS | The web app is a same-origin BFF; no CORS policy is registered | `grep -rn "AddCors\|UseCors"` → none |
| WebAssembly | Interactive Server only; a test pins it | `grep -rln WebAssembly` → only `MiroClone.Web.csproj` (a comment in the render-mode test) |

## 5. Readiness ledger

| Window | Items |
|---|---|
| Immediately (before any deploy) | A-9: run the AppHost once and record it. A-18: verify the discovery names at the owner's first deploy |
| Within a week | A-11 and A-10 decisions (PRIVACY.md §6). Authservice duplicate-email answer (SECURITY_REVIEW S-8) |
| Later | A-12 before any second instance. A-13, A-14, A-15, A-16 as recorded |

## 6. Verified live, and not

Verified live (a running process and a real browser): the account pages render after the circuit connects, with and without the fix (A-1); the auth limit redirects the 11th sign-in and the endpoint is not reached (A-5); an unreachable authservice redirects with a message (A-6); the security headers are on the response and no CSP violation is logged on the account pages.

Not verified live: the AppHost (A-9); the authenticated journeys (sign-up to dashboard, boards, live two-browser sessions); OTLP export; the board page's CSP; the cross-service purge after a board delete (ADR 0043).
