# 0033 — The live board session is a hub client owned by the circuit: an ordered outbox, remote edits on the dispatcher, and a re-read on every reconnect

- **Status:** Accepted
- **Date:** 2026-10-26
- **Principles:** blazor-web-app §6 (everything off the UI thread is marshalled back before the page reacts; `InvokeAsync`), §7 (the circuit is the BFF: the token stays in the session service, and a handler gets it per request), §10 (rules in plain classes, tested without the page), §5 (typed clients, DTOs are copies pinned by round-trip tests); ADR 0016 (the hub contract and its token transport); ADR 0031 (`ICanvasSession` commits a gesture once; the editor owns no rules); ADR 0012 (`MutationApplier` is the only place a remote mutation is applied; undo may overwrite a remote change)

## Context

WP10 connects the board to the canvas hub. Three things need deciding that the frozen contract does not settle:

1. Who owns the connection. The page's circuit is the only place that holds the user's token (ADR 0022), so the connection must be made from the circuit.
2. Where a remote edit runs. The hub delivers on its own threads, while the editor reads the document on the circuit. Two threads on one `CanvasDocument` would race.
3. What happens to edits made while the connection is down, and to a server that refuses one. The contract has no acknowledgement; the service applies a mutation and answers only with an error code.

## Decision

- **`HubCanvasSession : IRealtimeCanvasSession`** holds one board's document, one `History` and one outbox. It implements `ICanvasSession`, so the editor's rules are unchanged: `Commit` runs `History.Execute` as before, then queues the applied mutations for the hub. The editor gains one method, `Reconcile`, which drops a selection a remote delete removed.
- **Remote edits go through `MutationApplier` on the dispatcher.** The session captures the circuit's context when the page opens it (`HubCanvasSessionFactory`) and posts every change to it. Tests pass no dispatcher and run changes inline. A `Changed` event tells the page to re-read the frame, inside `InvokeAsync`.
- **The outbox is ordered and sent one at a time.** An edit leaves the queue only when the hub has taken it. A transport failure leaves it queued for the next connection, where a repeat of an edit the service already has changes nothing (`MutationApplier` is idempotent).
- **A reconnect re-reads the board.** The hub's `InitialState` replaces the document. The edits still in the outbox are then replayed on top, so the user's view keeps them and they are sent after the join.
- **A refused edit is dropped and the board is re-read.** This covers the layer limit and an invalid layer. The service's state wins. A rate-limited edit is kept and sent after 250 ms.
- **Retries and terminal states.** A failed connection retries with backoff, starting at one second and doubling to 15 seconds, and the delay resets after a live connection. A refusal for a board the caller cannot see, or a missing sign-in, is terminal: the banner says so and nothing retries.
- **Undo history survives a re-read.** It is per user and per board, as ADR 0012 says, and an undo may overwrite a remote change. This is accepted, not engineered away.

## Consequences

- The circuit holds one hub connection per open board. The token is read from the session service for each request. No token reaches the browser (ADR 0022).
- Every mutation is applied on the same thread as the editor, so no lock is needed around the document reads that the editor makes each frame. The locks that remain protect only the outbox and the participant map, which the background loops touch.
- **Known limit.** A drag that began before a remote delete of its layer re-creates the layer on release, because an upsert creates a missing layer. The editor is WP09's and is not changed here; WP11 can filter the commit if it matters.
- The outbox is unbounded. A board edited for hours offline holds every edit until the connection returns. Acceptable for this audience; a cap with a notice is the first change if it is not.
