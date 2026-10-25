# 0012 — Canvas rules: layer order, net selection, resize flips, history, and malformed remote mutations

- **Status:** Accepted
- **Date:** 2026-10-25
- **Principles:** the frozen contracts in `docs/implementation-plan/README.md` (`CanvasState`, `Layer`, `LayerMutation`, max 100 layers,
  last-writer-wins per layer); blazor-web-app "Logic outside the markup"; P14 (decisions recorded in the repository)

## Context

The frozen contract gives `layerIds: guid[]` and `LayerMutation.order: guid[]` but does not say which end of the array is the top. The
original clone (`miro-clone-nextjs`) is the reference for in-scope behaviour. That reference could not be attached to this session, so
the geometry choices below follow the common conventions of a vector editor. WP11's parity check must reconcile them with the original.

## Decision

**Layer order.** `CanvasState.layerIds` and `LayerMutation.order` run bottom to top: index 0 is the bottom layer and the last index is the
top. `CanvasDocument.Order` and `LayerOrdering` follow this. A new layer goes on top. **WP05 (CanvasService) and WP10 (Web) must read the array
the same way.** Any change is a change to a frozen contract and needs an ADR with both sides in one PR.

**Hit-testing.** Filled shapes and text count anywhere inside their box. Ellipses count inside their outline. Paths count within a tolerance of
the stroke. The tolerance is in world units and grows the target. The topmost layer under the pointer wins.

**Net (marquee) selection.** A layer is selected when its bounding box intersects the net. Touching edges count. A layer only has to be grazed,
not fully enclosed.

**Resize.** A handle keeps its opposite edge (the anchor) fixed and moves the edge under the cursor. Dragging past the anchor flips the box to the
other side; it never inverts it. Each side is at least `Resizer.MinimumSize` (1 world unit). Every call reads only the start box and the cursor,
so the same drag always gives the same box. A path's points scale with its box. A flat path (zero width or height) keeps its points on that axis,
so it does not widen from zero.

**Translation and ordering.** Selected layers move as a group and keep their relative order when brought to front or sent to back.

**History.** Undo and redo are per user and per board. Each entry is the inverse of one local action, computed by `MutationApplier` before the
action is applied. A batch is one step. A mutation that changes nothing adds no step. The stacks hold at most 100 entries, and the oldest is
dropped first. An undo may overwrite a remote change to the same layer (last-writer-wins). That is accepted for this audience.

**Mutation application.** `MutationApplier` is idempotent: applying a mutation twice leaves the same state, and the second call returns
`Unchanged`. A new layer is refused with `LimitReached` at 100 layers (409 on the server). A malformed remote mutation is refused with
`Invalid`: a missing field, a non-finite number, a negative size, or points on a non-path. A refused mutation changes nothing.

**Reorder.** A reorder keeps the requested ids that exist, in the requested order. It drops unknown ids and duplicates. Layers the request leaves
out keep their relative order and go to the top.

## Consequences

- WP09 renders bottom to top, which is the natural SVG order. WP10 sends `Reorder` with the full order, not a delta.
- WP11 reconciles these choices against `miro-clone-nextjs/app/board/[boardId]/_components` once the reference is available. A difference is
  a change to this ADR, not a silent change in the code.
- Unknown ids in a reorder are ignored, not rejected. A reorder that races a delete from another user still applies cleanly.
