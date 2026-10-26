# 0030 — One ES module of at most 60 lines carries the board's pointer input; no JavaScript package

- **Status:** Accepted
- **Date:** 2026-10-26
- **Principles:** blazor-web-app §4 (no JS interop packages, no CSS framework; every added dependency is a review finding until justified here), §1 (no JS toolchain, no `package.json`), §6 (everything off the UI thread is marshalled back before the page reacts); AGENTS.md "Versions only in `Directory.Packages.props`"; WP09 scope ("the only JS in the app, dependency-budget ADR")

## Context

Pointer capture, the element's coordinates and the wheel cannot be read from Blazor markup. Blazor's `@onpointerdown` gives the event but not the pointer capture needed to keep a drag alive outside the surface, and a wheel handler in Blazor cannot call `preventDefault` (it would scroll the page). The canvas needs both.

## Decision

- **One file: `MiroClone.Web/wwwroot/js/canvas-pointer.js`, at most 60 lines.** It exports `attach(surface, target)` and returns `{ dispose() }`. It has no `import`, makes no network call and evaluates no string. `CanvasPointerModuleTests` reads the file and fails when any of those change.
- **It keeps no state.** It forwards `pointerdown`, `pointermove`, `pointerup` and `pointercancel` as `OnPointer(phase, x, y, button, shift)`, and `wheel` as `OnWheel(x, y, deltaX, deltaY, zoom)`. Coordinates are relative to the surface. The wheel listener is non-passive and calls `preventDefault`. Every rule about the canvas stays in C# (`BoardEditor`, over `MiroClone.Web.Canvas.Core`).
- **Loaded by the circuit, never by the page.** `JsCanvasPointerSource` imports it with `IJSRuntime` and disposes the attachment and the module. The page reaches it only through `ICanvasPointerSource`, so bUnit tests substitute a fake and need no browser.
- **No package is added.** No npm, no JS test runner. The file is covered by the same xUnit suite.

## Consequences

- The app still has one JavaScript file. A second one needs its own ADR and a budget.
- `pointermove` is not coalesced in the browser. Every move is a circuit round trip. Acceptable for WP09; if WP10's presence throttle (≤ 20/s) is not enough, coalescing with `requestAnimationFrame` is the first change, and it stays inside the 60-line budget.
- A closed circuit rejects the messages. The module swallows that rejection, because the page is gone by then.
