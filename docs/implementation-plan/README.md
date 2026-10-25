# miro-clone (Blazor) — implementation plan

Greenfield rebuild of the Next.js/Convex/Liveblocks Miro clone as a .NET 10 estate that follows
[`architecture-standards`](https://github.com/konradcinkusz/architecture-standards) (P1–P15, `blazor-web-app`,
`service-api-patterns`, `state-snapshot-persistence`, `identity-and-accounts`, `repo-baseline`).

Execution: a supervisor session schedules work packages (WP) onto autonomous sub-sessions.
Rules for sub-sessions: [`PROTOCOL.md`](PROTOCOL.md). The packages: [`WORK-PACKAGES.md`](WORK-PACKAGES.md).

## References (read-only)

| Repo | Use |
|---|---|
| `konradcinkusz/miro-clone-nextjs` | Feature inventory: `app/(dashboard)`, `app/board/[boardId]/_components`, `convex/` |
| `konradcinkusz/AureliusMundus` | `services/RealtimeService` (SignalR hub), `services/BlazorFrontend`, `services/AspireAppHost` — early PoC, **do not copy**; learn only |
| `konradcinkusz/authservice` | The identity provider (adopted as-is, never copied into this tree) |
| `konradcinkusz/aurelius-promptus` | Worked example of the estate: AppHost, ServiceDefaults, Fly.io, workflows, tests |
| `konradcinkusz/architecture-standards` | The standards |

## Scope

In: sign-in through authservice (workspace = authservice organization), dashboard (list, search, favourites,
create, rename, delete), board canvas (select, move, resize, multi-select, rectangle, ellipse, text, note,
pencil path, colour, layer ordering, delete, undo/redo), realtime presence (cursors, participants) and
live co-editing, persistence across restarts.
Out (ADR-0004): AI agents, chat, simulations, metrics, templates from AureliusMundus.

## Target topology (P1, P3, P9)

```
MiroClone.AppHost            Aspire composition root (WithReference/WaitFor/WithHttpHealthCheck)
MiroClone.ServiceDefaults    shared kernel: telemetry, health, resilience, JWKS JWT validation, rate limit,
                             origin guard, DB provider. NO entity/DTO/enum/user-facing string.
authservice (external)       only identity provider; own db + signing key; pinned image, never copied
MiroClone.BoardsService      boards, favourites. Own PostgreSQL db "boards". EF Core migrations.
MiroClone.CanvasService      layers per board: in-memory aggregate + JSON snapshot ("canvas" db),
                             SignalR hub /hubs/canvas for presence + mutations
MiroClone.Web                Blazor Web App, interactive Server render mode. The circuit is the BFF:
                             tokens live server-side only, httpOnly SameSite=Strict cookie, typed clients
tests/*                      xUnit per service; Web.Tests (bUnit); Web.E2E (Playwright, awaits circuit)
```

Namespaces/projects are prefixed `MiroClone.`. Target framework from `Directory.Build.props` (.NET 10, `global.json`).
Blazor WebAssembly is not used. Web references only ServiceDefaults; DTOs are copies with round-trip tests.

## Contracts (frozen — change only via ADR + both sides in one PR)

All APIs: JSON camelCase, `Authorization: Bearer` JWT from authservice, claims give user id + active workspace
(`WorkspaceClaims`). A row in another workspace answers **404, not 403**. Lists paginate (`page`, `pageSize<=50`).

**BoardsService** `/api/boards`
- `GET ?search=&favourites=true&page=&pageSize=` → `{ items: BoardSummary[], total }`
- `POST {title}` → 201 `BoardSummary` · `PATCH /{id} {title}` (1–60 chars, trimmed) · `DELETE /{id}` (also removes favourites)
- `PUT|DELETE /{id}/favourite` → 204 (idempotent)
- `GET /{id}/access` → 204/404 — used by CanvasService only (anti-corruption client, resilience handler)
- `BoardSummary { id: guid, title, authorId, authorName, imageSeed: 1..10, createdAt, isFavourite }`
- DELETE publishes nothing; CanvasService purges via `DELETE /api/canvas/{boardId}` called by BoardsService
  after commit (best effort, retried; orphan snapshot is harmless and swept by a hosted service).

**CanvasService**
- `GET /api/canvas/{boardId}` → `CanvasState { version: long, layerIds: guid[], layers: { [id]: Layer } }`
- `Layer { id, type: Rectangle|Ellipse|Text|Note|Path, x, y, width, height, fill: {r,g,b}, value?: string, points?: [x,y][] }`
- Max 100 layers per board (409 beyond). Last-writer-wins per layer.
- Hub `/hubs/canvas` (JWT via access_token): client→server `JoinBoard(boardId)`, `SendMutation(boardId, LayerMutation)`,
  `UpdatePresence(boardId, Presence)`; server→client `InitialState(CanvasState, Presence[])`,
  `LayerMutation(connectionId, LayerMutation)`, `PresenceUpdated(connectionId, Presence)`, `PresenceLeft(connectionId)`.
- `LayerMutation { kind: Upsert|Delete|Reorder, layer?: Layer, id?: guid, order?: guid[] }`
- `Presence { userId, name, color, cursor?: {x,y}, selection: guid[] }`

## Wave plan

```
W0 WP00 → W1 WP01 → W2 WP02 → W3 {WP03, WP04, WP05, WP07, WP13} → W4 WP06 → W5 {WP08, WP09}
 → W6 {WP10, WP11} → W7 {WP12, WP14} → W8 WP15
```
Max 4 sub-sessions in parallel. A wave starts only when every dependency PR is merged and `main` CI is green.
