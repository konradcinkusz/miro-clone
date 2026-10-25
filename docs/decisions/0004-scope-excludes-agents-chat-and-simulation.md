# 0004 — Scope: sign-in, dashboard, board canvas, presence and persistence only

- **Status:** Accepted
- **Date:** 2026-10-25
- **Principles:** P14 (anti-goals stated before features; documentation records reasoning)

## Context

The reference repositories carry features this product does not need: AI agents, chat, simulations, metrics and
templates from `AureliusMundus`. Copying them would widen every contract and every test before the core works.

## Decision

In scope: sign-in through authservice; a dashboard (list, search, favourites, create, rename, delete); the board
canvas (select, move, resize, multi-select, rectangle, ellipse, text, note, pencil path, colour, layer ordering,
delete, undo/redo); realtime presence and live co-editing; persistence across restarts.

Out of scope: AI agents, chat, simulations, metrics, and templates. `AureliusMundus` is read for learning only and its code is never copied.

## Consequences

- A request for any out-of-scope feature needs a new ADR that supersedes this one.
- The feature inventory in `miro-clone-nextjs` is the reference for in-scope behaviour, not a list of things to port wholesale.
