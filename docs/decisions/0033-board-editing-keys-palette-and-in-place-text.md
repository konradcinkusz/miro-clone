# 0033 — Board editing: one keyboard handler on the page, the reference palette as layer data, text edited in a controlled field over the layer, and undo through the session

- **Status:** Accepted
- **Date:** 2026-10-26
- **Principles:** blazor-web-app §1 and §4 (no contenteditable, no JS package; the page owns the keyboard through Blazor's own handlers), §9 (colour only through tokens: chrome colours are `var(--color-…)`, and layer colours are data that no stylesheet names; `StyleContractTests` reads the stylesheets), §10 (rules in plain C# with their own tests: `BoardEditor`, `Palette`, `History`); ADR 0012 (layer order and history are Core's); ADR 0030 (the one JS module is unchanged); ADR 0031 (every edit goes through `ICanvasSession`, one gesture is one undo step)

## Context

WP11 adds the editing features the reference app (`miro-clone-nextjs`, `app/board/[boardId]/_components`) has and WP09 left out: text and note editing in place, a colour picker, bring-to-front and back, undo and redo, keyboard shortcuts, and the 100-layer feedback. Four choices needed a record.

1. **Where the keyboard is read.** WP09 read keys on the SVG surface, which has focus only after a click on it. A click on the toolbar moved focus away, so shortcuts stopped working.
2. **Where the palette comes from.** The reference picker has eight fixed colours. They are data for layer fills. The brief asks for them to be used token-safely.
3. **How text is edited.** The brief rules out `contenteditable` and asks for a controlled `<input>` or `<textarea>`.
4. **Where undo lives.** `History` (WP07) already records inverses and returns the mutations it applies. The page must not hold its own stacks, because WP10 must send the same mutations to the room.

## Decision

- **Keys are read on the page.** `BoardPage` wraps the header, toolbar, selection bar and surface in one `div` with `@onkeydown`. Keys typed into the text overlay stop there (`@onkeydown:stopPropagation`), so a letter typed into a layer never runs a shortcut. The editor also ignores every key while an edit is open, which is the second guard.
- **Shortcuts** (`BoardEditor.Key(KeyInput)`): Delete and Backspace remove the selection; Escape cancels a gesture; Enter edits the one selected text or note; Ctrl+Z undoes; Ctrl+Shift+Z redoes (the reference's binding); Ctrl+A selects all; V, T, N, R, E and P choose Select, Text, Note, Rectangle, Ellipse and Pencil. Ctrl and Meta are one flag. Ctrl+A and the letter tools go beyond the reference, which binds only undo and redo; the PR body lists them.
- **The palette is Core data.** `Palette.Colours` holds the eight reference colours with their names: Red, Yellow, Green, Blue, Purple, Orange, Black and White. A layer's fill is set from them as an SVG attribute. A swatch receives its colour as one custom property, `--swatch`, set in the markup and read by scoped CSS, so no stylesheet names a colour. A new text layer and a pencil stroke default to `Palette.Ink`, black; rectangles, ellipses and notes keep `BoardEditor.DefaultFill`.
- **Note and text ink.** A note's text is black or white, whichever reads better on its fill (`Palette.ReadableOn`, a luma rule). A text layer's text takes its fill, so its colour is the chosen colour.
- **Text is edited in place by a controlled field.** The field is an absolutely positioned `<input>` (text) or `<textarea>` (note) over the layer, placed from the camera at the current zoom. Its value is the editor's text, and each `input` event sends the text to the editor (`EditText`). The layer under edit is not drawn on the SVG, so its text never appears twice. Focus is set by the framework's `ElementReference.FocusAsync` (see ADR 0034 for why this is not app JavaScript).
- **One edit is one undo step.** `FinishEdit` commits the text as one `Upsert`. It ends on blur, on Enter in a text field, on Escape, and when a pointer press or a tool change starts something else. Empty text removes the layer as one `Delete`, so a click with the text tool that types nothing leaves nothing behind.
- **Undo and redo are the session's.** `ICanvasSession` gains `CanUndo`, `CanRedo`, `Undo()` and `Redo()`. They return the mutations that changed the document, which WP10 sends to the room. The toolbar's Undo and Redo buttons and the keys call the same editor methods.
- **Front and back are `Reorder` mutations.** `LayerOrdering` (Core) computes the order, and the mutation goes through `Commit`, so it is one undo step. The selected layers keep their order among themselves, as the reference does.
- **Colour, front, back and delete act on the selection.** The selection bar shows only while something is selected. Colour applies to every selected layer that does not already have it, in one step.
- **The 100-layer limit is reported when a gesture starts or a layer is inserted.** It is the same notice the canvas already showed (`LimitNotice`), and the page shows it as a callout with `role="status"`. The limit is not checked by undo, because undo only removes layers.

## Parity with the reference

The parity list for each component of `app/board/[boardId]/_components` is in the PR body. The list is the acceptance evidence for WP11.

## Consequences

- **A keystroke on the text overlay re-renders the page.** The page holds the whole frame, so each `input` event costs one render. This is acceptable for a single text field; WP10's presence throttle does not apply to typing.
- **Double-click to edit is not added.** The pointer module sends no click count, and adding one would change ADR 0030's contract. Enter edits the selected text or note instead. A later package can add the click count inside the same 60-line budget.
- **Text colour follows the fill.** A text layer that already holds a light fill, from data written before this change, now draws in that light fill; before, it drew in the default black. The page keeps the stored colour, and the user can recolour it.
- **Nothing is sent yet.** Until WP10 lands, edits, colour and order stay local, as ADR 0031 states.
