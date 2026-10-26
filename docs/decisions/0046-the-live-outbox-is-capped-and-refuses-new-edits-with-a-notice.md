# 0046 — The live outbox holds at most 1,000 unsent edits; at the cap new edits are refused and the page says so

- **Status:** Accepted. Carries out the first change that ADR 0035 names ("a cap with a notice") for the offline outbox.
- **Date:** 2026-10-26
- **Principles:** ADR 0035 (the outbox is ordered and sent one at a time; a reconnect replays it); ADR 0031 (a gesture commits once, and an edit is local until the hub takes it); P15 and P5 (no state grows without a bound on the server or in the circuit); the audit's (G) finding

## Context

The outbox in `HubCanvasSession` was unbounded. A board edited for hours offline held every edit until the connection returned, and each edit is kept in the circuit's memory. ADR 0035 accepted that for this audience and named the cap as the first change if it proved necessary.

Two designs were possible at the cap:

- **Drop the oldest edit.** The room would miss an edit the page still shows, so the copies would diverge with no sign to the user.
- **Refuse the newest edit.** The page keeps every edit it has accepted, and the user is told that new edits are paused.

## Decision

- `HubCanvasSession` holds at most `DefaultMaximumOutbox` = 1,000 unsent edits. The constructor takes the cap, so tests use a small one.
- At the cap, `Commit`, `Undo` and `Redo` return no change and touch nothing: the refused edit is never applied locally, so the document and the outbox agree.
- `IRealtimeCanvasSession.EditsPaused` is true while the outbox is at the cap. The board's connection banner shows "Too many edits are waiting to be sent, so new edits are paused until the connection returns." even when the board is otherwise live.
- When the outbox drains below the cap, the session raises `Changed`, so the banner clears without a reload.

The number 1,000 is generous for one person's edits between reconnects: a gesture commits once (ADR 0031), and the largest board holds 100 layers (`CanvasDocument.MaxLayers`).

## Consequences

- An offline user who keeps editing past 1,000 edits sees the notice and loses those edits from this screen, rather than the browser holding them. That is a visible, bounded loss in an extreme case, and the notice says what is happening.
- The cap is per circuit (per open board), so it bounds memory per user, not across the server.
- Tests: `HubCanvasSessionTests.AnOutboxAtItsCapRefusesNewEditsUntilTheConnectionTakesSome` (refusal before the document changes, and resume after reconnect), and `ConnectionBannerTests` (the notice while live). Without the cap the first test fails.
