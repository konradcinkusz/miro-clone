# 0029 — The board's title comes from walking the boards list; a single-board read is a contract change, not made here

- **Status:** Accepted
- **Date:** 2026-10-26
- **Principles:** the frozen contracts (`docs/implementation-plan/README.md`): a contract changes only through an ADR with both sides in one PR, and WP09 is Web-only; blazor-web-app §5 (one typed client per backend, DTOs are copies); AGENTS.md "Lists paginate (`page`, `pageSize<=50`)"

## Context

The board page must show the board's title (WP09 "board title"). The frozen BoardsService contract has `GET /api/boards` (a paginated list), and no single-board read. The CanvasService answers `GET /api/canvas/{boardId}` with 404 for a board outside the caller's workspace, but its state carries no title.

## Decision

- `BoardsClient.FindAsync(sessionId, boardId)` walks `GET /api/boards` with `pageSize=50` until it finds the id, and stops at the last page or at an empty page. It returns null when the workspace does not list the board, and the page then shows the not-found state.
- The page reads the canvas first. A 404 there is authoritative and ends the load. The list lookup then supplies the title.
- No endpoint is added and no contract changes. The change belongs in the boards service and needs its own ADR and a PR touching both sides.

## Consequences

- **The cost grows with the number of boards.** A workspace with N boards needs ceil(N / 50) list calls to open the last one. The estate is small today; a `GET /api/boards/{id}` is the fix, and it is the first follow-up for WP10 or WP11 to schedule.
- The walk makes no assumption about sort order beyond the list's own paging.
