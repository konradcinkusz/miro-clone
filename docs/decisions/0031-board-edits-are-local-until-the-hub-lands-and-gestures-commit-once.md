# 0031 — Board edits go through ICanvasSession; each gesture commits one undo step; the editor is plain C# over Core

- **Status:** Accepted
- **Date:** 2026-10-26
- **Principles:** blazor-web-app §10 (logic outside the markup: rules in plain classes with xUnit tests) and §9 (markup decides nothing); ADR 0012 (layer order, net selection, resize flips, history and mutation rules, all of them Core's); WP09 scope ("local mutations go through an `ICanvasSession` interface, in-memory now, WP10 plugs the hub")

## Context

WP09 draws a board and lets the user create, move, resize and delete layers. Those edits must not reach the canvas service yet (WP10 owns the hub), and the page must not own the rules. Every geometry, selection and history decision is already in `MiroClone.Web/Canvas/Core` (WP07).

## Decision

- **`ICanvasSession`** holds the document and commits local actions. `Commit(mutations)` runs them through `History.Execute`, which runs `MutationApplier` and records the inverse, and returns the mutations that changed the board. `LocalCanvasSession` is the in-memory implementation. WP10 adds `HubCanvasSession` with the same calls and sends the returned mutations to the room.
- **Loading is REST.** The page reads `CanvasClient.GetAsync` and maps the wire state with `CanvasStateMapper`. A malformed state throws `FormatException` and becomes one alert. Layers keep the `layerIds` order, bottom to top (ADR 0012).
- **`BoardEditor`** owns the tool, the selection and the gesture. It builds a `CanvasFrame` that the markup draws. Every decision is Core's: `HitTester` for the target, `Marquee` for the net, `Resizer` for handles and flips, `Translator` for moves, `LayerFactory` for new layers, `PathSimplifier` for pencil strokes.
- **One gesture is one commit.** A move, a resize or a drawn layer is one `Commit`, so one undo step later. A drag previews from the start position and never accumulates. Escape and a pointer cancel commit nothing. A release that changes nothing commits nothing.
- **Parity choices, taken where the reference (`miro-clone-nextjs`, `app/board/[boardId]/_components`) is clear:** handles appear only for one selected layer that is not a path (the reference does the same); a drawing tool returns to Select after one layer is made (the reference returns to its none mode); the net adds to a shift selection; the wheel pans and Ctrl or Meta with the wheel zooms at the pointer; Delete and Backspace remove the selection.
- **The 100-layer limit is checked before a gesture starts** and reported as a notice. `History` ignores a refused mutation, so the editor cannot learn of it afterwards.
- **Chrome colours are tokens; layer fills are data.** Selection, handles, net and text use `var(--color-…)` through `app.css`. A layer's fill arrives as an SVG attribute from the data. The default fill of a new layer is a constant until WP11 adds the palette.
- **Input is marshalled to the circuit's dispatcher** (`InvokeAsync`) before the page reacts (blazor-web-app §6). The surface disposes the browser source, and the page cancels its load and disposes the session.

## Consequences

- **Reload restores only what the service holds.** Until WP10, local edits are not sent, so a reload shows the last snapshot the canvas service stored. This is the WP09 acceptance ("reload restores them via CanvasService once WP10 lands"), and the PR body states it under "Not done".
- Undo and redo are not on the toolbar. `History` already supports them, so WP11 needs only the keys and the buttons.
- The inline text editor, colour picker and path editing belong to WP11. A text layer has no editing yet.
- A remote mutation (WP10) can change a selected layer under the user. The editor reads the document each frame, so the selection follows the new state. An in-progress drag keeps its start positions until it ends.
