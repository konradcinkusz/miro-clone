# 0003 — One service and one database per bounded context: Boards and Canvas

- **Status:** Accepted
- **Date:** 2026-10-25
- **Principles:** P3 (service per bounded context; database per service); P2 (shared kernel, not shared domain);
  P4 (provider-portable, migrated schema)

## Context

The product has two different data shapes. Boards, favourites and workspace listings are relational, with search,
paging and migrations. A board canvas is a small aggregate (up to 100 layers) that changes many times per second
while people co-edit, and is best held in memory and snapshotted.

## Decision

- **`MiroClone.BoardsService`** owns boards and favourites in its own PostgreSQL database `boards`, with EF Core migrations (`MigrateAsync`, P4).
- **`MiroClone.CanvasService`** owns layers per board: an in-memory aggregate with a JSON snapshot in its own store (`canvas`), and the SignalR hub `/hubs/canvas`.
- Neither service opens the other's database. Cross-context calls are HTTP: CanvasService asks BoardsService `GET /api/boards/{id}/access`, behind an anti-corruption client (P11).
- `MiroClone.ServiceDefaults` is a kernel: telemetry, health, resilience, JWT validation, rate limit, origin guard, DB provider. It has no entities, DTOs, enums or user-facing strings (P2).

## Consequences

- Board deletion cannot be one transaction across both stores. BoardsService purges the canvas by HTTP after commit, best effort with retries; an orphan snapshot is harmless and swept later.
- Two deployables and two databases for a small product. Accepted for the separation of relational and realtime concerns and for P3.
