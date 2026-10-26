# 0034 — The dashboard opens a new board, returns focus to the opener of an invite or create dialog, and keeps focus inside an open dialog, with framework focus and no app JavaScript

- **Status:** Accepted
- **Date:** 2026-10-26
- **Principles:** blazor-web-app §4 (dependency budget: the framework only; no JS package, and no app script beyond the one module ADR 0030 budgets), §5 (errors as state), §9 (scoped CSS for the new sentinels); the WP08 gaps that ADR 0027 named (deviation 1 and the focus statement); ADR 0028 (the dashboard has no JavaScript of its own)

## Context

ADR 0027 named two gaps the dashboard had to close with the board page (WP09) in place. It said that creating a board should navigate to `/board/{id}`, and that moving focus back to the opener "would need JavaScript and is not done". WP11 is the step that closes them.

The second claim is not quite right. Blazor's `ElementReference.FocusAsync()` is part of the framework: the framework ships the script that focuses, and the application adds none. ADR 0028 forbids app JavaScript on the dashboard, not the framework's own focus call. This ADR records that reading, so the next reviewer does not take the call for a second script.

## Decision

- **Create opens the new board.** `CreateBoardAsync` navigates to `/board/{id:D}` with `NavigateTo` after the service answers `201`. The "created" notice is dropped, because the board page replaces it. The list is not reloaded, since the page is leaving. This changes ADR 0027's deviation 1, which this ADR supersedes.
- **Invite and create return focus to the control that opened them.** `Home` keeps an `ElementReference` to the "New board" and "Create board" buttons and to "Invite members". `CloseDialogsAsync` moves focus to the opener while the opener is still on the page, before the dialog is removed. A failed focus (the opener is gone, or the circuit is closing) is ignored: focus then stays where the browser put it.
- **Rename and delete do not return focus yet.** Their openers are menu items inside a board card, so the card would have to expose a reference. That is a card change outside WP11's files, and it is recorded as not done in the PR.
- **A dialog keeps focus inside itself.** `Dialog` places two `tabindex="0"` sentinels, one before and one after the card, hidden with scoped CSS. Focus that reaches the start sentinel moves to the end sentinel, and focus that reaches the end sentinel moves to the card (`tabindex="-1"`). Between the two, Tab and Shift+Tab reach every control of the card and do not leave the dialog. The sentinels need no JavaScript: each one calls the framework's `FocusAsync` in its `focus` handler.
- **Escape still closes the dialog**, as in ADR 0027.

## Consequences

- **The dashboard still has no app JavaScript.** Focus uses the framework's own call; ADR 0028's rule holds, and no new dependency or script is added.
- **Keyboard users keep their place.** A dialog that closes returns to the control that opened it, which the WP08 acceptance called for.
- **A create that navigates away does not return focus.** The page change moves focus to the new board, which is the intent.
- **Focus in a card's menu is not restored.** Rename and delete leave focus where the browser put it, until the card exposes its menu button.
- **The trap is tested by its structure, not by a browser.** `DashboardPageTests` checks that the sentinels and the card's `tabindex` render; the focus moves themselves need a browser run, which WP12 covers.
