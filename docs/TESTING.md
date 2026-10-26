# Testing

As of 2026-10-26. This page says what each layer proves, what runs in CI, what runs only on a developer machine, and what
has never run. It was written in a session with no .NET SDK and no Docker daemon, so **nothing in this document was re-run
here**. The results quoted are from the merged pull requests and their CI runs.

## 1. Commands

```bash
dotnet restore MiroClone.sln
dotnet build MiroClone.sln                    # 0 warnings: warnings are errors (Directory.Build.props)
dotnet test --solution MiroClone.sln          # the form the local SDK (10.0.113) accepted in the WP10 run
```

`AGENTS.md`, `docs/implementation-plan/PROTOCOL.md` and `.github/workflows/pr-validation.yml` write
`dotnet test MiroClone.sln`. WP10's PR body says the local SDK rejects that form. CI on the same PR still passed, so the
difference is unexplained (see [COMPLIANCE.md](architecture/COMPLIANCE.md#3a-open-register)).

PostgreSQL tests run only when `TEST_POSTGRES` holds a server connection string, for example
`Host=localhost;Username=postgres;Password=…`. Each test class creates its own database on that server (`boards_test`,
`canvas_test`, and per-class names), so classes can run in parallel. Without the variable they report `skipped`.

## 2. Test projects

The counts are test methods declared with `[Fact]` or `[Theory]`, not executed cases. Theories expand.

| Project | Methods | What it proves | Needs |
|---|---|---|---|
| `MiroClone.ServiceDefaults.Tests` | 37 | The kernel: architecture rules (no `MiroClone.*` reference, no enum, no `DbContext`), JWT validation against a local JWKS, origin guard, forwarded headers, rate limits, health endpoints, workspace claim | None (PostgreSQL case gated) |
| `MiroClone.BoardsService.Tests` | 38 | Board rules, list paging and search, the HTTP surface, favourites, the access check, the canvas purge client, the session endpoint | None; PostgreSQL migration and board tests gated |
| `MiroClone.CanvasService.Tests` | 62 | Canvas aggregate rules and the 100-layer limit, JSON round trip, snapshot store and registry, the hub (identity stamped from the token, the layer limit, presence rate limit), the REST read and purge by ownership, the access client | None; snapshot PostgreSQL test gated |
| `MiroClone.AppHost.Tests` | 9 | Estate wiring at model level: the authservice image and tag, wait edges, the environment each service receives | None. The containers are never started |
| `MiroClone.Web.Tests` | 320 | Canvas Core rules and their isolation from Blazor types, the editor, the live session with a fake transport, two sessions converging on the real canvas hub on `TestServer`, pages in bUnit, account flows, session single-flight refresh, the stylesheet and cover contract, host pins, Data Protection keys | None; no browser |
| `MiroClone.TestSupport` | 0 | A local authority stand-in and token factory, shared by the tests | None |

Totals: about 466 methods. The PR #15 body reports a local run of 562 tests: 557 passed, 5 skipped, 0 failed (the skips are the
PostgreSQL-gated tests). CI passed on the same commit, but its test log was not read for this page. The PR #13 body reports
512 tests, 507 passed, 5 skipped before WP10.

### What the architecture tests enforce

| Rule | Test | Source |
|---|---|---|
| The kernel holds no entity, enum or `DbContext`, and references no `MiroClone.*` assembly | `ServiceDefaults.Tests/ArchitectureTests.cs` | ADR 0005 |
| Canvas Core has no Blazor or JS types | `Web.Tests/Canvas/Core/CoreIsolationTests.cs` (reads the sources) | ADR 0011 (canvas) |
| The pointer module is one file of at most 60 lines, no import, no network | `Web.Tests/Canvas/Interop/CanvasPointerModuleTests.cs` | ADR 0030 |
| Colours come only from the token blocks; both themes define the same tokens | `Web.Tests/Style/StyleContractTests.cs` | `blazor-web-app` §9 |
| Cover pictures use only token colours and no colour keyword | `Web.Tests/Style/PlaceholderContractTests.cs` | ADR 0028 |
| The kernel stays under 800 lines | `scripts/check-kernel-size.sh`, run first in `pr-validation` | ADR 0005 |
| Pages render in the interactive server mode only | `Web.Tests/RenderModePinTests.cs`, `HostPinTests.cs` | ADR 0022 |

Not machine-checked: user-facing strings in the kernel (ADR 0005 says review enforces it).

### Notable behaviour tests

- **Single-flight refresh:** ten concurrent callers cause one refresh (`SessionServiceTests`, ADR 0023).
- **Two sessions on the real hub converge** on edits and presence, on `TestServer` (`Web.Tests/Canvas/Realtime/CanvasHubConvergenceTests.cs`, ADR 0035).
- **Reconnect replays unsent edits** and re-reads the board (`HubCanvasSessionTests`).
- **Restart keeps layers:** a test checks that a restart after a flush keeps the layers (ADR 0015).
- **Migration on start-up** is checked where a relational provider runs (`PostgreSqlMigrationTests`, gated).
- **Clock:** the auth harness now starts from the real clock. Pinning it to a fixed date broke tokens once the date moved
  past the pin (PR #12, PR #13).

## 3. Layer rules

- Logic is tested at the layer that holds it, without network (`protocol §1`, P13). Canvas rules are plain C# with tests and
  no browser.
- The hub is tested on `TestServer` with long polling and server-sent events. WebSocket upgrade is not exercised by the unit
  tests (ADR 0016, ADR 0037).
- bUnit tests use a fake boards service (`FakeBoardsService`) and a fake pointer source (`FakeCanvasPointer`). They cannot
  show focus movement, layout or dark mode.

## 4. What runs where

| Check | Runs | Result on the last merged PR |
|---|---|---|
| `pr-validation`: kernel size, restore, build, test | Every PR and push to `main` | Success (PR #15) |
| `pr-validation`: `docker-build` for boards, canvas and web (no push) | Every PR | Success on PR #14 and #15 |
| `secret-scan`: gitleaks over history | Every PR and push, weekly, on demand | Success (PR #14 and #15) |
| Pre-commit gitleaks | Developer machine, after `scripts/install-hooks.sh` | Refuses to commit without gitleaks installed |
| Fly.io deploy and preview workflows | **Not run.** Manual dispatch only (ADR 0026) | Disabled |
| End-to-end browser suite (Playwright; AGENTS.md calls it `Web.E2E`) | **Not in the repository.** WP12 is in progress | Not run |

## 5. What has never run

These are the verification gaps. Each one is also in the backlog in [UI-UX.md](UI-UX.md#5-ranked-backlog).

1. **The AppHost has never been started.** It needs Docker for PostgreSQL and the authservice image. Its wiring is tested at
   model level only (ADR 0011, ADR 0008).
2. **Service discovery between the web app and the services has not run** outside TestServer. The circuit's hub client uses
   the `canvas-hub` handler through discovery (ADR 0037).
3. **No browser has driven a board.** The two-browser latency target (under 1 s) is unmeasured. Phone width, dark mode and
   focus returning to the opener have not been seen in a browser.
4. **The authservice image has not run.** Its behaviour was checked from its tagged source against a local PostgreSQL 16, not
   from the image (ADR 0011).
5. **Core line coverage** has an acceptance target of 90% (ADR 0011). The repository holds no measured figure.
6. **Fly.io** has never deployed (ADR 0026). `DEPLOYMENT.md` lists the checks that wait for an owner-run deploy.

## 6. Adding a test

- Put the test with the code it proves, in the matching project and folder.
- Use `[Fact]` for one case and `[Theory]` for a table. A PostgreSQL test takes its database name from its class and is gated
  on `TEST_POSTGRES`. Skip with `Assert.Skip`, not by hiding the test.
- No warning may be suppressed to make a test build (AGENTS.md). Fix it or record it in an ADR.
- Never skip, disable or quarantine a failing test to get green (PROTOCOL.md).
