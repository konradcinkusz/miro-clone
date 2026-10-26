# Privacy

As of 2026-10-26. This page lists the personal data this repository handles, where each item is stored, how long it stays, and
what happens to it when a person is erased. It was written from the code on `main`. Where the code does nothing, the page
says so.

**Short version:** this repository holds no user table. The account record (email, password, memberships, consent) lives in
authservice. This repository holds a person's id on the boards they create and favourite, a name label that is "Member" in
practice, and short-lived session data on the web server. **Nothing here deletes or anonymises that data when a person is
erased from authservice.** Section 4 sets out the gap and the decision it needs.

## 1. Personal data held, by store

| Store | Personal data | Purpose | Kept until | Code |
|---|---|---|---|---|
| authservice database (not in this repository) | Email, password hash, verification state, organizations and roles, consent acceptances, refresh tokens | Accounts, sign-in, workspace membership | authservice's rules (its own soft-delete window and reaper, per the identity standard; not verified for v0.3.4 here) | `docs/AUTH.md` lists the calls this repository makes |
| Web server memory (session store) | Access and refresh tokens, token expiry, every token claim (subject, email, verified flag, organization ids and roles) | Keeps the user signed in without a token in the browser | Eight hours from sign-in (absolute). Lost on restart | `Web/Auth/SessionService.cs` (`SessionRecord`) |
| Browser cookie `miroclone.session` | Subject, email, verified flag, session id. No token | Names the server-side session | Eight hours. HttpOnly, SameSite=Strict, Secure outside Development | `Web/Auth/SessionClaims.cs`, `Web/ServiceCollectionExtensions.cs` |
| `boards.boards` (BoardsService) | `author_id` (the person's id), `author_name` (the `name` claim, else `Member`), `workspace_id`, `title`, `created_at` | Shows who made a board and in which workspace | Until the board is deleted | `BoardsService/Boards/Board.cs`, `BoardContracts.cs` (`BoardCaller.TryRead`) |
| `boards.board_favourites` (BoardsService) | `user_id`, `board_id` | A person's favourites | Until the board is deleted, or the row is removed by a favourite toggle | `BoardsService/Boards/BoardFavourite.cs` |
| `canvas.canvas_snapshots` (CanvasService) | `workspace_id`, layers as JSON. **Layers hold no person's id or name** | The board's content | Until the board is purged. Orphans are never swept (see §5) | `CanvasService/Persistence/CanvasSnapshotStore.cs`, `Canvas/Layer.cs` |
| CanvasService memory (presence) | User id, label (`Member` plus four hex digits), colour chosen from the user id, cursor position, selection | Shows who is on a board right now | While connected. Not written to any store | `CanvasService/Realtime/CanvasHub.cs` (`Stamp`), `PresenceTracker` |
| Data Protection key ring | No personal data. It is the secret that protects the cookie and the form tokens | Cookie and antiforgery protection | Until replaced. On `App_Data/keys` locally, `/data/keys` on a volume in deployment | `Web/ServiceCollectionExtensions.cs` (`DataProtection:KeysPath`) |
| Logs and telemetry | Request metadata. A search of every log message in the repository found no email, password or token written to a log | Operations | Set by the exporter; none is configured in the repository | `ServiceDefaults/ServiceDefaultsExtensions.cs` (OTLP only when `OTEL_EXPORTER_OTLP_ENDPOINT` is set) |

Development only: the authservice dev outbox writes verification, reset and invitation tokens to its log
(`Development` environment, ADR 0011). The seed script's password is for development and is marked `gitleaks:allow`.

### What other people see

- A board's author id and name are visible to every member of the workspace who can open the board list.
- A participant on a live board sees the label `Member` plus four hex digits and a colour, and a cursor. The label never
  contains the email address (ADR 0016 §5, `CanvasHub.cs`).
- Nothing in the board or presence data carries an email address.

## 2. Data flows out of the repository

| To | What | Why |
|---|---|---|
| authservice | Email and password at sign-in and sign-up; invitee email on invite; verification and reset tokens | Accounts |
| OTLP endpoint, if configured | Logs, traces and metrics, as the exporter sends them | Operations |

No other third party is called. No analytics, no email provider, and no CDN for fonts or scripts is used.

## 3. Protections in the code

- Tokens never reach the browser. The circuit holds them (ADR 0022).
- The cookie holds no token, is HttpOnly and SameSite=Strict, and is encrypted by ASP.NET Core cookie authentication.
- Tokens are checked against authservice's keys before they are stored, and each service checks them again (ADR 0007, ADR 0011).
- A row in another workspace answers 404, not 403, so the existence of another workspace's board is not revealed (ADR 0016).
- The canvas purge after a delete is refused for another workspace's snapshot (ADR 0016 §8).
- Presence identity comes from the token, so a client cannot claim another person's label or colour.

Gap: the sign-in endpoints have no rate limit in this repository (see [UI-UX.md](UI-UX.md#5-ranked-backlog), item 2).

## 4. Erasure

### What happens today

A person is erased in authservice, or their account is deleted there. **This repository has no handler for that event, no
endpoint and no job.** Going through the stored data:

| Item | Effect of the erasure in authservice | Effect in this repository |
|---|---|---|
| Account and credentials | Removed or deactivated by authservice (its rules) | Not touched here |
| Refresh tokens | Expected to be revoked on permanent deletion, as the identity standard §8 requires. Not verified for authservice v0.3.4 | If authservice refuses the next refresh, the session ends (ADR 0023) |
| Access token already issued | Still valid until it expires. Its lifetime is set by authservice | Each service checks the signature and expiry on every call, so the token works until it expires |
| Open live connection | Stays open until it closes. Tokens are checked when a connection opens (ADR 0016 consequence) | Stays open until it closes or the session ends |
| Session in web memory | Ends at the next refresh, or after eight hours | Ends then. Nothing is deleted at once |
| `boards.boards` rows the person created | No effect | **Kept.** `author_id` and `author_name` stay on each board |
| `boards.board_favourites` rows | No effect | **Kept.** The row names a user id that can no longer sign in |
| `canvas_snapshots` | No effect | **Kept.** No person's id is in the layers, so there is nothing to erase in the snapshot |
| Presence | None | Gone when the connection closes |

So after an erasure in authservice, the person's id and the name stored at creation remain in `boards`, and their favourite
rows remain in `board_favourites`. The boards stay visible to the workspace.

### Why the name is "Member" in practice

`BoardCaller.TryRead` copies the `name` claim, and falls back to `Member`. authservice issues no name claim (ADR 0011,
`CanvasHub.cs`), so today every `author_name` is `Member`. The id is still a person's identifier, so it is personal data
even without the name.

### The decision needed

This is an owner decision, and it changes what the repository must do. Two options:

1. **Anonymise on erasure (recommended here).** Replace `author_id` with a fixed unknown id and `author_name` with a fixed
   "Deleted user" label, and delete the person's favourite rows. This needs a signal from authservice (an event, or a
   reconciliation job) that names the erased user. No such signal is in this repository, and this repository has not
   verified whether authservice v0.3.4 publishes one. Record the choice in an ADR when it is made.
2. **Keep authorship and record the basis.** Boards are shared work in a workspace, so the author stays visible. This is a
   legal judgement, and it has to be written down with its reasons before it is relied on.

Until one of these is chosen and built, the answer to an erasure request for a person who created boards is: their
authservice account is erased, and the boards and favourite rows keep the id.

### Exporting a person's data

There is no export in this repository. The data in it that is attributable to a person is the `author_id` on boards and the
`user_id` on favourites. A request for those rows would need a query by user id, which is not implemented. Authservice's own
export, if it has one, is outside this repository and is not verified here.

## 5. Retention and clean-up

| Data | Retention in code today | Gap |
|---|---|---|
| Boards | Until deleted | None |
| Canvas snapshot after a delete | Until purged. A failed purge leaves an orphan **forever**, because no sweep exists (ADR 0013, ADR 0016) | The orphan keeps the layers of a deleted board. Needs the sweep |
| Session | Eight hours absolute | None |
| Presence | While connected | None |
| Logs | Set by the exporter | Not configured in the repository; the query-string token question is open (UI-UX item 7) |

## 6. Decisions open

1. Erasure policy for `boards` and `board_favourites` (§4).
2. A sweep or purge for orphan canvas snapshots, and the credential it uses (ADR 0013).
3. Whether a name claim should come from authservice, which would change what other people see (ADR 0016 §5).
4. Telemetry redaction of the websocket token (ADR 0016 consequences).

These are also in [architecture/COMPLIANCE.md](architecture/COMPLIANCE.md#3a-open-register).
