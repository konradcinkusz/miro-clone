# UI and UX

As of 2026-10-26. Every screen, state and label below was read from the Razor components on `main`. A state is listed only if
a component renders it. Nothing here has been checked in a browser: the end-to-end suite (WP12) is not merged, and the
browser-only checks are listed in the backlog.

## 1. Screens

| Route | Screen | Component | Who sees it |
|---|---|---|---|
| `/` | Dashboard | `Pages/Home.razor` | Signed in |
| `/board/{id}` | Board canvas | `Pages/Board/BoardPage.razor` | Signed in, board in the active workspace |
| `/sign-in`, `/sign-up`, `/verify-email`, `/forgot-password`, `/reset-password`, `/consent` | Account forms | `Pages/Account/*.razor` | Anyone; consent only when owed |
| `/sign-out` | Sign-out | `Pages/Account/SignOut.razor` | Signed in |

The account forms are static server pages outside the interactive router. They post to `/account/*` and need no circuit
(ADR 0022). The dashboard and the board are interactive Blazor Server pages.

## 2. Dashboard (`/`)

**Layout:** a sidebar with the workspace name, "Team boards" and "Favourite boards" (`Sidebar.razor`). Main column: heading,
search field (`Search boards`), "Invite members" for Owner and Admin only, then the board grid with a pager.

**Board card:** a cover picture chosen by `imageSeed` (ten SVG files in token colours, ADR 0028), the title, the author and a
relative time, a favourite toggle, and actions: board link (shown as read-only text, not a copy button), rename, delete with
confirmation.

**Dialogs:** New board, Rename board, Confirm delete, Invite members. Each is a `role="dialog"`, closes on Escape, and keeps
focus inside itself through two sentinels (ADR 0034). Focus returns to the opener for New board and Invite members only.

| State | Trigger | What the user sees |
|---|---|---|
| Loading | Page opened | "Loading your workspace…" and four skeleton cards |
| Boards | Workspace has boards | Grid of cards, pager, "Page N of M" |
| Empty, no boards | Workspace is empty | "Create your first board." with a Create board button |
| Empty, search | Search matches nothing | "No results found." |
| Empty, favourites | Favourites view is empty | "No favourite boards." |
| Choose a workspace | Several organizations, none active | Callout listing each organization and its role; boards stay closed |
| No workspace | Account in no organization | Callout: ask an owner to invite, or create one in authservice, then sign in again |
| Error | Service call fails | One `role="alert"` sentence from `BoardErrors`: 404 "This board no longer exists", 400 title length, 401 "Your session has ended", 403 "no active workspace", 429 "Too many requests", otherwise the screen's own fallback |
| Notice | Create, rename or delete succeeded | `role="status"` callout |

**Rules the UI follows:** search is debounced at 300 ms and kept in the address (`?search=`, `favourites`, `page`). Changes
appear only after the boards service confirms them (no optimistic updates, ADR 0027). A page past the end moves to the last
page. Titles are checked before sending with the same rule as the service: trimmed, 1 to 60 characters.

## 3. Board canvas (`/board/{id}`)

**Header:** "Boards" link back to the dashboard, the board title, and the participants list (one avatar per person, self first).
**Toolbar:** Select (V), Text (T), Note (N), Rectangle (R), Ellipse (E), Pencil (P), Undo (Ctrl+Z, disabled when nothing to
undo), Redo (Ctrl+Shift+Z). **Selection bar** (role toolbar, shown only while something is selected): eight colours, Bring to
front, Send to back, Delete.

**Canvas:** pan with the wheel, zoom with Ctrl or Meta and the wheel, marquee select (a layer is selected when it is grazed),
handles for one selected non-path layer, multi-select move, colour, order and delete. Enter edits the one selected text or
note in place. Escape cancels a gesture. Delete and Backspace remove the selection. Ctrl+A selects all.

| State | Trigger | What the user sees |
|---|---|---|
| Loading | Page opened | "Loading board…" (`role="status"`) |
| Board not found | Canvas answers 404, or the board is not in the workspace | "Board not found", "It may have been deleted, or it belongs to another workspace.", link "Back to boards" |
| Connecting | Live session starting | Banner "Connecting to live updates…" (no warning tone) |
| Reconnecting | Connection lost | Warning banner: "Connection lost. Reconnecting; your edits stay on this screen and are sent when it returns." |
| Unavailable | Terminal: the sign-in has ended, or the hub refuses the board (`board_not_found`, `unauthenticated`). Retries stop | Warning banner: "Live updates are unavailable for this board. Reload the page to try again." |
| Live | Connected | No banner |
| Layer limit | A gesture or insert would pass 100 layers | Callout (`role="status"`); the tool draws nothing |
| Remote edit | Another person changes a layer | The layer updates in place; a selected layer that was deleted remotely is dropped from the selection |

Presence: cursors and selections of other people are drawn in their colour. The label is `Member` plus four hex digits of the
user id, because authservice issues no name claim (ADR 0016, ADR 0036).

## 4. Account screens

| Screen | Heading | Notes |
|---|---|---|
| Sign in | "Sign in" | A notice after verification ("Your email address is verified…"). Two-factor accounts are refused with a message (ADR 0025) |
| Sign up | "Create an account" | Requires both terms and privacy acceptance, using the versions authservice currently requires |
| Verify email | "Verify your email" | Resend available; the token comes from the authservice email (the dev outbox log in development) |
| Forgot password | "Forgot your password?" | Always the same generic answer, so the page never reveals an account (ADR 0025) |
| Reset password | "Choose a new password" | The token is carried in the address and spent on use |
| Consent | "Accept the updated terms" | Shown at sign-in when authservice says consent is owed |
| Sign out | "Sign out" | Ends the local session and asks authservice to revoke refresh tokens (best effort) |

## 5. Ranked backlog

Ranked by user impact, then by risk if left, then by cost. Items 1–4 are gaps that can hurt someone today. Items 5–9 block
or limit a core flow. Items 10–17 are polish and parity. The item's source is the ADR, the merged PR body (#12 WP08, #13 WP09,
#14 WP11, #15 WP10) or a code read.

| Rank | Item | Why this rank | Source | Blocked by |
|---|---|---|---|---|
| 1 | **Run the stack and the browser suite.** The AppHost has never been run live. The sessions that wrote these pages had no Docker daemon. No browser has driven a board. Latency under 1 s between two browsers is unmeasured. Phone width, dark mode and focus behaviour are unchecked. | Every UI claim above rests on bUnit and text checks. This is the largest unknown | ADR 0011 (verification boundary), PR #12, #13, #15 "Not done" | WP12 (E2E) merge; Docker |
| 2 | **Sign-in endpoints are not rate limited.** The Web app never adds the rate limiter, and the kernel's auth policy has no user. `/account/sign-in` relies on authservice alone | Credential guessing; security | Code read: `Web/Program.cs`, `ServiceDefaults/RateLimitingExtensions.cs` | Decision: apply `RateLimitPolicies.Auth` to `/account/*` |
| 3 | **Erasure and authorship.** Erasing a user in authservice leaves their board author id and name in `boards`, and their favourites, with no cleanup here | Personal data kept after erasure. See [PRIVACY.md](PRIVACY.md#4-erasure) | Code read; identity guide §8 | Owner decision on the policy |
| 4 | **Orphan canvas snapshots are never swept.** A failed purge after a delete leaves a snapshot. The logs say "left for the sweep"; no sweep exists | Storage and data retention; the snapshot holds a deleted board's layers | ADR 0013, ADR 0016; code read (`CanvasPurgeClient`) | Needs a service credential decision (ADR 0013) |
| 5 | **Choose a workspace; Create organization.** A user in two organizations cannot open any board, and a user in none cannot create a workspace from the app. The UI says so and offers no action | Blocks a core flow for multi-organization users | ADR 0024, PR #12 "Not done", `Home.razor` | authservice endpoint that issues a token scoped to one organization (ADR 0024 recommends one) |
| 6 | **Presence and authorship labels.** Every participant and every board author is "Member" plus digits, because authservice issues no name claim. People cannot tell each other apart | Shared boards are hard to read. The fix is an authservice claim | ADR 0016 §5, ADR 0036, `CanvasHub.cs` | authservice name claim |
| 7 | **Hub token in the websocket query string is not checked against telemetry.** The token reaches the telemetry pipeline if the instrumentation records the query. Not verified | Credential in logs or traces, if it is recorded | ADR 0016 consequences ("checked in WP13"), not done | Verify with a run; add redaction |
| 8 | **A dragged layer deleted remotely comes back on release.** An upsert creates a missing layer. A user sees a layer reappear | Data correctness in co-editing | ADR 0035 known limit | Filter the commit in `BoardEditor` (WP11 owned) |
| 9 | **The offline outbox is unbounded.** A board edited for hours offline keeps every edit | Memory on the circuit; a long offline session holds everything | ADR 0035 | Cap with a notice |
| 10 | **Single board read.** Opening the last board of N costs ceil(N/50) list calls | Slow for large workspaces | ADR 0032 | A `GET /api/boards/{id}` contract change, both sides in one PR |
| 11 | **Presence collapses two tabs of one person into one entry.** The join list has no connection id | Minor: duplicate-looking entries for one user | ADR 0036 | Contract ADR adding a connection id to `InitialState` |
| 12 | **Double-click to edit text; title edit on the board page; focus return for rename and delete.** Parity gaps with the reference | Discoverability, keyboard parity | PR #14 "Not done"; ADR 0034 | Click count in the pointer module (within 60 lines); a card exposing its menu button |
| 13 | **Consent is checked at sign-in only; two-factor sign-in is refused.** A user who keeps a session after a terms change keeps it until next sign-in | Legal consent currency; users with 2FA cannot sign in | ADR 0025 | A consent re-check; a second-factor step |
| 14 | **Undo can overwrite a remote change.** Undo restores the state before the gesture, even if someone else changed the layer since | Surprise for co-editors | ADR 0035 (accepted, not engineered away) | Decide whether undo should skip changed layers |
| 15 | **Reload after a live edit is verified only in-process.** The late-joiner test shows the service's state. A browser reload has not been run | Edits could be lost on reload if the outbox is not flushed first | PR #15 "Not done" | WP12 |
| 16 | **Deploy and restart end every session and circuit.** Sessions and circuits live in memory on one instance | Users are signed out on each deploy | ADR 0019, ADR 0022, ADR 0020 | A shared session store (a deployment decision) |
| 17 | **Unused code path.** `LocalCanvasSession` is no longer used by the page; it remains for tests | Maintenance only | PR #15 "Not done" | Remove after WP12 |

Items deliberately left out of the list: AI agents, chat, simulations, metrics and templates (ADR 0004).

## 6. Accessibility and style

- Forms use labels and `role="alert"` or `role="status"` for messages. Dialogs have `aria-labelledby`.
- Colours come only from the token blocks in `wwwroot/app.css`. `StyleContractTests` checks both themes define the same tokens
  and that no stylesheet names a colour outside them. Layer fills are data and are not checked.
- Dark mode: the token block defines it, and the test checks it. It has not been looked at in a browser (item 1).
- Cover pictures are decorative (`alt=""`), since the title is text beside them.
