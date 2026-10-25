# Work packages

Each WP = one branch `wp/NN-slug`, one PR, merged by its sub-session. "Standards" = read before coding.
Contracts are in [README.md](README.md) and are frozen.

---
## WP00 — Repository baseline  (deps: none)
Standards: `repo-baseline`, `reference-architecture` (P12, P14).
Do: `MiroClone.sln` (empty), `global.json` (.NET 10), `Directory.Build.props` (TFM, nullable, implicit usings, warnings-as-errors),
`Directory.Packages.props` (central versions, vulnerable-package restore failure), `.editorconfig`, `.gitignore`, `.gitattributes`,
`LICENSE` kept, `README.md` (what it is, how to run, links), `AGENTS.md` (layout table, build/test, rules easy to break — modelled on
aurelius-promptus), `CLAUDE.md` (`@AGENTS.md`), `.claude/settings.json` enabling `architecture-core` from `architecture-standards`,
`.github/pull_request_template.md`, `.github/workflows/pr-validation.yml` (restore/build/test; skips cleanly when no projects),
`secret-scan.yml` (gitleaks full history), `scripts/install-hooks.sh` + pre-commit gitleaks hook, `docs/decisions/0001..0004`
(0001 Blazor Web App over Next.js citing blazor-web-app §1; 0002 authservice as only IdP, external; 0003 service+db per context
Boards/Canvas; 0004 scope: no agents/chat/simulation). Enable nothing that needs repo settings; list such needs in the PR.
Accept: CI green on an empty solution; hook installs; ADRs cite principles.

## WP01 — ServiceDefaults kernel  (deps: WP00)
Standards: `reference-architecture` (P2, P8, P9, P15), `service-api-patterns`, `blazor-web-app` (origin guard, shared kernel).
Do: `MiroClone.ServiceDefaults`: `AddServiceDefaults()`/`MapDefaultEndpoints()` (`/health`, `/alive`, OTLP traces/metrics/logs, service
discovery, ONE standard resilience handler with explicit timeouts); `AddJwtValidation()` (JWKS from `Auth:Authority`, audience, clock skew,
no signing key); `WorkspaceClaims` helper (user id, workspace id); rate limiting extension; `OriginGuard` middleware (+ options, 3 tests);
database provider extension (Npgsql; in-memory/Sqlite for tests); `IOptionalIntegration` health reporting + startup banner.
Tests project `MiroClone.ServiceDefaults.Tests` incl. an **architecture test** (kernel has no entity/DTO/enum/seed/user-facing string)
and `scripts/check-kernel-size.sh` wired into CI. 
Accept: guards green; JWT validation tested with a locally generated key pair served as JWKS in the test host.

## WP02 — Skeleton: AppHost + stubs  (deps: WP01)
Standards: P1, P5, P6, P9, `blazor-web-app` (root components, render mode).
Do: `MiroClone.AppHost` (Postgres with two databases `boards`,`canvas`; `boards-service`, `canvas-service`, `web` with
`WithReference`/`WaitFor`/`WithHttpHealthCheck`); `MiroClone.BoardsService`, `MiroClone.CanvasService` (Program.cs = manifest,
`ServiceCollectionExtensions`, `/health` only, EF Core DbContext + empty initial migration applied by a hosted service `MigrateAsync`);
`MiroClone.Web` (Blazor Web App: `App.razor` with `HeadOutlet`/`Routes` carrying `InteractiveServer`, `<Assembly>.styles.css` linked,
`/` renders "Hello"); one test project per project; bUnit test pinning render mode + stylesheet link; `scripts/run-local-stack.sh`.
Accept: `dotnet run --project MiroClone.AppHost` shows all resources healthy (verify with the `run` skill or health calls).

## WP03 — Adopt authservice  (deps: WP02)
Standards: `identity-and-accounts`, `shared-service-reuse`, `master-delivery-prompt` (authservice phase), P1, P4.
Do: run authservice in AppHost as an independent instance (pinned image if published — check its `docs/DEPLOYMENT.md`; else document the
chosen build-from-sibling approach in an ADR; **never copy its source here**) with its own database, signing key from parameters/user-secrets,
dev email outbox on, OAuth providers off. Wire `Auth:Authority` into boards/canvas/web. Document claims (user id, workspace/org id,
roles) in `docs/AUTH.md`. Dev seed script `scripts/seed-dev-users.sh` creating two users in one organization via authservice's API.
Test helper in a test-support project issuing tokens against a fake JWKS (no network).
Accept: curl register → login → bearer call to a protected stub endpoint on boards-service returns 200; without token 401.

## WP04 — BoardsService  (deps: WP02)
Standards: `service-api-patterns`, P4, tenant isolation (see aurelius-promptus `tests/*Scoping*` pattern).
Do: entity `Board` + `BoardFavourite`, migration, endpoints exactly as the README contract (search = case-insensitive title contains,
favourites filter, pagination, `imageSeed` random 1–10 at create, title rules, delete cascades favourites, access endpoint, purge call
to Canvas via typed client + resilience, best-effort + sweep hosted service). Validation by one shared extension; problem-details errors.
Tests: unit, controller/endpoint tests, **cross-workspace tests for every id endpoint (404)**, PostgreSQL-gated migration test.
Accept: contract fidelity; every endpoint has a scoping test.

## WP05 — CanvasService  (deps: WP02)
Standards: `state-snapshot-persistence`, `service-api-patterns`, P4, P8.
Do: in-memory `BoardCanvas` aggregate (layers, order, version), dirty-set + timed flush to a one-table JSON snapshot (idempotent DDL / migration),
bounded rehydrate, degrade to memory when DB is down (health shows it), 100-layer cap, `GET /api/canvas/{boardId}`, `DELETE`,
SignalR hub per README contract (JWT from `access_token`, board access check through anti-corruption client to BoardsService
`/access`, presence tracker, groups per board, cleanup on disconnect, deleted layers purged so they do not return as ghosts).
Hub rate limits (mutations, presence). Tests: aggregate rules, snapshot round-trip, hub with `TestServer` (two clients see each other's
mutations; non-member refused), degraded mode.
Accept: restart keeps layers; two connections converge.

## WP06 — Web shell + sign-in  (deps: WP03)
Standards: `blazor-web-app` (whole guide), `frontend-bff` (concepts only), `identity-and-accounts`.
Do: layout, **frozen CSS token contract** (light/dark, phone width, AA contrast, focus, reduced motion) + stylesheet test banning stray
colours; cookie auth on the host mapped onto Blazor, tokens stored server-side (distributed-cache-backed ticket store or protected
cookie holding only a session id), refresh handled in a `DelegatingHandler` via a token service resolved correctly (handlers have their own
scope), circuit revalidation against cookie expiry, shared Data Protection keys persisted to the Web db/volume; pages `/sign-in`, `/sign-up`,
`/sign-out`, verify-email/forgot-password flows through authservice; workspace switcher (authservice organizations); typed clients
`BoardsClient`, `CanvasClient` (service discovery, one resilience handler); origin guard on; every load catches into state and renders
`role="alert"`; consent acceptance screen if authservice requires it. DTO copies with round-trip tests. `[Authorize]` is UX only.
Tests: bUnit for pages, handler tests with fake gateway, origin-guard tests.
Accept: sign up → sign in → land on empty dashboard shell in the running stack; no token reachable from browser JS.

## WP07 — Canvas core logic  (deps: WP02)
Standards: `blazor-web-app` ("Logic outside the markup"), testing-strategy.
Do: in `MiroClone.Web/Canvas/Core` (plain C#, no Blazor types): `Layer` copies, `Camera` (pan/zoom, screen↔world), hit-testing per type,
bounds, multi-select net, resize with handles + flip rules, translate, `LayerFactory`, colour model, `History` (per-user undo/redo
of inverse mutations, bounded), `LayerOrdering` (bring to front/back), path simplification, `MutationApplier` (apply remote mutation
idempotently). 100% rules in classes with xUnit tests incl. property-style tests for resize/hit-test. No JS.
Accept: ≥ 90% line coverage of Core; no dependency on Web UI.

## WP08 — Dashboard UI  (deps: WP04, WP06)
Do: `/` dashboard: sidebar (workspace switcher, "Team boards", "Favourite boards"), search box (debounced), board grid with cards (image from
`imageSeed` static SVG assets under wwwroot, author, relative time, favourite toggle), "New board" (modal), rename modal, delete confirm,
empty states (no boards / no results / no favourites), skeleton loading, error alerts, pagination. Optimistic update only after
success. Accessible (roles, labels, test ids per `e2e-acceptance-testing`).
Tests: bUnit per component with fake `BoardsClient`.
Accept: every dashboard feature of miro-clone-nextjs `app/(dashboard)` present.

## WP09 — Canvas rendering + core tools  (deps: WP05, WP06, WP07)
Do: `/board/{id}` page: load state via `CanvasClient`; SVG canvas component tree (`LayerPreview`, `Rectangle`, `Ellipse`, `Text`, `Note`,
`Path`), toolbar (select, rectangle, ellipse, text, note, pencil), selection box + resize handles, selection net, pan (wheel) /
zoom, delete (Del), info/participants header chrome, board title, loading + not-found states. Pointer events via a ≤60-line JS ES module
(pointer capture, coordinates, wheel) — only JS in the app, dependency-budget ADR. All geometry decisions go through WP07 Core.
Local mutations go through an `ICanvasSession` interface (in-memory implementation now; WP10 plugs the hub). Dispose + cancellation correct.
Tests: bUnit render tests per layer type; Core covered by WP07.
Accept: can create, move, resize, delete shapes locally; reload restores them via CanvasService once WP10 lands (until then via REST load).

## WP10 — Realtime in Web  (deps: WP05, WP09)
Standards: `blazor-web-app` (live updates, `InvokeAsync`, disposal).
Do: `HubCanvasSession : ICanvasSession` (server-side SignalR client from the circuit, bearer from token service, reconnect with state re-read),
sends `LayerMutation`s, applies remote ones via `MutationApplier`, presence (throttled cursor ≤ 20/s, selection), `CursorsPresence`,
`Participants` avatars (colour per user), connection-state banner. Background loops own a CTS cancelled in `Dispose`;
UI updates through `InvokeAsync(StateHasChanged)`.
Tests: fake hub transport; reconnect test; two-session convergence test using the real hub on `TestServer` if feasible.
Accept: two browsers see each other's cursors and edits < 1 s locally.

## WP11 — Editing features  (deps: WP09)
Do: pencil path drawing, text and note inline editing (`contenteditable` avoided → controlled `<input>/<textarea>` overlay), colour
picker (palette from nextjs repo), layer bring-to-front/back, undo/redo (Ctrl+Z / Ctrl+Shift+Z) via Core `History` also emitting
mutations, keyboard shortcuts, 100-layer limit feedback, multi-select move/colour/delete. Features parity check against
`miro-clone-nextjs/app/board/[boardId]/_components` listed in the PR.
Tests: bUnit + Core.
Accept: parity list ticked.

## WP12 — E2E acceptance  (deps: WP10, WP11)
Standards: `e2e-acceptance-testing`, `testing-strategy`.
Do: `MiroClone.Web.E2E` Playwright suite against the AppHost (launch profile with fake/real authservice per WP03): sign-up via dev outbox, create board,
draw shapes, second browser context sees them, reload persists, favourite, search, rename, delete, cross-workspace 404 UX. Every test
asserts something; awaits the circuit handshake; test ids/roles; independent + cleanup. CI job `e2e` in same PR (excluded from unit job).
Accept: suite green in CI; one deliberate-failure sanity run noted in PR.

## WP13 — Deployment (Fly.io)  (deps: WP02; extend as services land)
Standards: `fly-io-deployment`, `pr-preview-environments`, P5–P7, P12.
Do: multi-stage Dockerfile per service (runtime major == TFM major, :8080, non-root), one `fly.toml` per app in `flyio/`, Web with
min machine 1, websockets, shared Data Protection keys, forwarded headers before origin guard; `.github/workflows/flyio.yml`
tag-driven (`v*`) with path-based change detection and cold-estate bootstrap; PR preview workflow with teardown; `docs/DEPLOYMENT.md`.
Do NOT deploy and do not add secrets; list required repo secrets/variables for the owner in the PR.
Accept: `docker build` of each image succeeds locally if Docker exists, else workflows lint with `actionlint`.

## WP14 — Documentation  (deps: WP10, WP11)
Standards: `research-documentation` (only if a LaTeX track is chosen), `repo-baseline` (staleness).
Do: final README with architecture diagram (Mermaid), `docs/ARCHITECTURE.md`, `docs/UI-UX.md` (screens, states, ranked backlog),
`docs/TESTING.md`, `docs/PRIVACY.md` (personal data held: names/avatars colour only; export/erasure delegated to authservice —
document how board authorship behaves when a user is erased), `docs/architecture/COMPLIANCE.md` (checklist table with evidence per item).
Accept: every compliance checklist item has a file/line citation or a justified N/A.

## WP15 — Final audit and hardening  (deps: all)
Skills: `architecture-core:architecture-review`, `quality-and-process:security-review`, `ticket-delivery:pr-review`.
Do: run the review, write `docs/ARCHITECTURE_REVIEW.md` and `docs/SECURITY_REVIEW.md`, then fix every blocking and should-fix finding
in this same PR (or merged follow-up PRs); verify clean-clone onboarding (`scripts/run-local-stack.sh up`), `main` CI green, and tag
`v0.1.0` readiness checklist (do not push the tag).
Accept: zero blocking findings; onboarding verified.
