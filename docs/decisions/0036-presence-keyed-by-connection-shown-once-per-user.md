# 0036 — Presence is kept per connection, shown once per user, paced at 50 ms, and a leave the session cannot place re-reads the room

- **Status:** Accepted
- **Date:** 2026-10-26
- **Principles:** the frozen contract (`docs/implementation-plan/README.md`, `InitialState`, `PresenceUpdated`, `PresenceLeft`): a contract change needs an ADR with both sides in one PR, so the gap is handled on the client; ADR 0016 consequences ("`InitialState` lists presence without a connection id ... WP10 must handle that, or the contract needs a connection id added through an ADR"); ADR 0016 §6 (presence allows 30 a second in steady state, 60 in a burst, and a rejection is immediate); service-api-patterns §1 (rate limits per caller, no queue); blazor-web-app §9 (markup decides nothing: the names, colours and cursor positions are computed in the session)

## Context

The hub answers a join with presence entries that carry no connection id. The next `PresenceUpdated` and `PresenceLeft` do carry one. So a session that joined after a person has two kinds of entry for them: one keyed by a connection the session has never seen, and one keyed by a connection it has. Two tabs of one person are two connections and one user.

The service ADR left the choice open: handle it in WP10, or change the contract. Changing the contract means both sides in one PR and a new `InitialState` shape, for a problem that affects only the list and not the edits.

## Decision

1. **Entries are kept by connection, as the events give them.** A join's entries are keyed `user:{id}`, because the join gives no connection. A later `PresenceUpdated` keys its connection id.
2. **The list shows each user once.** The signed-in user comes first. Each other user shows the newest entry the session received for them, so a stale join entry never hides a newer cursor. Names and colours come from the service; the colour must be a six-digit hex colour, or the neutral fallback is shown.
3. **A leave for a known connection removes that entry.** A leave for a connection the session cannot place re-reads the room (a join), because the session cannot know which entry it was. The re-read replaces the entries with the service's list.
4. **Presence is paced at 50 ms.** The session sends the latest cursor and selection at most once every 50 ms, so it sends no more than 20 a second, under the service's steady rate of 30. The cursor is in world coordinates, so it stays on the layer when the camera moves. The name and colour are left empty, because the service stamps identity from the token.
5. **A remote selection is drawn from the remote's own bounds.** A selected layer that this session no longer has is skipped rather than drawn.

## Consequences

- The contract is unchanged. The cost is a re-read when a participant who joined before this session leaves, which is rare and bounded by the number of people on the board.
- A re-read on leave also resends the session's own presence, which the service then broadcasts to the others. That is harmless and idempotent.
- If the service ever adds a connection id to `InitialState`, rule 1 becomes exact and rule 3's re-read goes away. That change is a contract ADR, not a WP10 change.
