# 0027 — The dashboard keeps its list state in the address, applies changes only after the service confirms them, and departs from the Next.js reference in three named places

- **Status:** Accepted
- **Date:** 2026-10-26
- **Principles:** blazor-web-app §3 (`[SupplyParameterFromQuery]` for shareable state), §5 (errors as state, one `role="alert"`),
  §6 (debounce with a cancelled task, cancelled on dispose), §9 (scoped CSS for component layout, no invented shared class names),
  §10 (rules in plain C# with their own tests), E2E locator rule (role and accessible name first, `data-testid` as the fallback);
  ADR 0024 (the workspace is read-only, no switch control)

## Context

WP08 asks for the Next.js dashboard feature set (`app/(dashboard)` in `miro-clone-nextjs`) on the Blazor dashboard. Three of its
features do not carry over as they are: the workspace switcher (ADR 0024 forbids a switch with nothing behind it), creating a board
and navigating into it (the board page belongs to WP09), and copying a link to the clipboard (which needs JavaScript, and WP08
may not add any: WP09 owns the only JS module).

## Decision

- **List state lives in the address.** `Home` takes `search`, `favourites` and `page` as query parameters. A link reopens the same view,
  and the pager and the sidebar are plain links. `BoardQuery` builds those addresses, so the page and the tests share one rule.
- **The search box debounces at 300 ms.** A keystroke cancels the pending wait and starts another. When the wait ends, the page
  replaces the address with `search=…`, which reloads the list through the parameters. The wait is cancelled on dispose.
- **No optimistic updates.** Create, rename, delete and favourite change the list only after the boards service answers success.
  A favourite removed on the favourites view reads the list again instead of editing it.
- **A page past the end moves to the last page** (after a delete empties it), and the address follows.
- **Errors reach the page as text.** `BoardErrors` maps each status to one sentence and never shows the response body. The list keeps
  its existing "could not be loaded" sentence, pinned by `HomeShellTests`.
- **Invitations are sent as members.** Only the Owner and Admin roles see "Invite members". The control calls authservice's invite
  endpoint; the invitation token goes to the authservice log, not to the page. The role names are read from the organization list.
- **Titles are checked before sending** with the same rule as the boards service (trimmed, 1 to 60). `RequestRoundTripTests` and
  `DashboardRulesTests` pin the two rules together.
- **Layout uses scoped CSS** (`*.razor.css`), which the contract allows. No shared class name is added to `app.css`.

## Deviations from the reference (named, not silent)

1. **Create does not open the new board.** The reference navigates to `/board/{id}` after creating. That page is WP09's, so the
   dashboard reloads the list and shows a confirmation. The cards already link to `/board/{id}`; WP09 makes those links work.
2. **"Copy board link" shows the absolute link in a read-only field.** The clipboard needs JavaScript, which WP08 may not add (see
   ADR 0028). The user selects and copies the text.
3. **The workspace is shown read-only, and "Create organization" is not offered.** ADR 0024 applies: authservice has no endpoint
   for either yet. A user in no workspace sees the explanation the shell already gave.

Toasts in the reference became inline `role="status"` notices, and the modals are dialogs with Escape to close. Focus is placed by the
first field's `autofocus`; moving focus back to the opener would need JavaScript and is not done.

## Consequences

- The dashboard has no JavaScript of its own. Dialog focus management and the copy action are the two places a later package could add it.
- Bunit tests cannot set query parameters directly, so they navigate to the address and render (`DashboardPageTests.Render`).
- Debounce and reload timing are tested with real short waits, bounded by bUnit's assertion timeout, not with a fake clock.
