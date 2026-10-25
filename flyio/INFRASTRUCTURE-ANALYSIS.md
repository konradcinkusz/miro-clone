# Fly.io DEV: topology, sizing and cost

Reasoned per `fly-io-deployment` §13. Prices are not stated, because they change and this document must not go stale with them. Multiply the
machine-hours below by the current Fly rate for `shared-cpu-1x` 512 MB and 1 GB, and add the volumes.

## 1. What runs when nothing is happening

| App | Machines idle | Memory | Volume | Why it runs |
|---|---|---|---|---|
| `miroclone-postgres-dev` | 1 (always) | 1 GB | 10 GB | Holds three databases; not scaled to zero (it has no HTTP listener) |
| `miroclone-authservice-dev` | 1 | 512 MB | — | `min_machines_running = 1` (question 2) |
| `miroclone-canvas-dev` | 1 | 512 MB | — | `min_machines_running = 1` (question 2) |
| `miroclone-boards-dev` | 1 | 512 MB | — | `min_machines_running = 1` (question 2) |
| `miroclone-web-dev` | 1 | 512 MB | 1 GB (keys) | `min_machines_running = 1` (question 2) |

Total: four application machines plus the database, all always on, and 11 GB of volume.

## 2. Which services pin a machine, and which synchronous call forces it

- **authservice.** Every other service fetches its JWKS on the request path, and validators refetch when their key cache expires. A stopped
  authservice would make every authenticated call fail until it woke, so it stays up.
- **Boards.** Web will call `/api/boards` synchronously for every page that lists or opens a board (WP05 and WP06 wire the typed clients).
- **Canvas.** Boards calls Canvas when a board is deleted (ADR 0013). Web will call it on every canvas open (WP10).
- **Web.** Entered from a browser, and a Blazor circuit is a live WebSocket. A cold start is a slow first page, but the circuit is state held
  in memory, and it is kept warm for the same reason as the others (ADR 0017).
- **PostgreSQL.** Never scales to zero: a database that sleeps delays every request that reaches it.

## 3. The cheaper option, and what it costs

- **Let Boards, Canvas and Web scale to zero.** The first request after idle waits for a cold start, which the proxy absorbs (auto-start).
  Cost: several seconds on the first request after an idle period. Saving: three machines' idle hours. Not chosen, because the Web circuit and
  the JWKS path are both on synchronous paths, and a slow first click on the sign-in path is a poor first experience. This is a decision the
  owner can take later by setting `min_machines_running = 0`, with the consequences above.
- **Consolidate Boards and Canvas into one app.** One machine fewer. Rejected: ADR 0003 separates them, and their databases are separate.

## 4. What is off the table

- `force_https = false`, or a plain-http authority: tokens and cookies would travel in clear.
- One database shared by two services, or one role across databases (ADR 0015, `shared-service-reuse` §5).
- A public listener on PostgreSQL.
- Authservice instance shared with another estate: its key would be a shared trust root.
- Scaling Boards, Canvas or Web past one machine until their migrations and the Web key ring move (ADR 0017, ADR 0018).

## Previews

Each preview runs four apps at `min_machines_running = 0`, so a closed preview costs its volume only (ADR 0019). The dev PostgreSQL is shared,
and each preview adds three small databases.
