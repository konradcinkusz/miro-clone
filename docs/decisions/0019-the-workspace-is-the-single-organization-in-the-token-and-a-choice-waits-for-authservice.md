# 0019 — The workspace is the token's single organization; with several, the dashboard states the choice is blocked and waits for an authservice change

- **Status:** Accepted as an interim. The owner decision it depends on is open (ADR 0011, "Owner decision needed before WP06").
- **Date:** 2026-10-25
- **Principles:** P14 (decisions recorded in the repository, with the reasoning); identity-and-accounts §1 (authorization facts are stamped at
  issuance, so a service never calls back); blazor-web-app §5 ("errors reach the page as state", "a degraded dependency is read, not
  assumed"); ADR 0011 (fail closed when the active workspace is ambiguous)

## Context

authservice stamps one `organization` claim per membership and no active-organization claim (ADR 0011, re-read for WP06). A user in one
organization has a workspace. A user in two has none in the token, and the boards service refuses them. A user in none has none either.
The plan asked for a workspace switcher that obtains a token scoped to one organization from authservice. authservice has no endpoint for
that: login and refresh return the same token with every membership in it, and there is no organization-scoped issuance.

## Decision

- **The dashboard decides the workspace from the stored token's claims, with `WorkspaceClaims` as the one rule** (`WorkspaceState`). One
  organization is the active workspace. Two or more is `ChooseOne`. None is `NoWorkspace`. An unreadable organization claim fails closed,
  as the boards service does.
- **The dashboard shows each state in words and calls no boards endpoint unless a workspace is active**, so a user with no workspace never
  sees a list that the boards service would refuse anyway (`HomeShellTests`).
- **The organizations are listed read-only** from `GET api/v1/organizations`, with the role each one carries (`userRole`). No switch control
  exists: an action with nothing behind it would be a placeholder.
- **Rejected:** re-signing the token in the web app (it has no signing key, and must not have one); a workspace header the boards service
  would trust (an unsigned claim is not authorization).

## Consequences

- Users in two organizations cannot open boards until authservice issues a token for one organization. This is the owner's decision.
- **Recommended option for the owner:** authservice adds an endpoint that issues a token scoped to one organization for the caller
  (for example `POST api/v1/auth/organization` taking an organization id, and returning a new token pair with that single `organization`
  claim). The web app then needs only a button that calls it, and ADR 0019 is superseded by that change. Either way, the services keep
  reading `WorkspaceClaims` as they do today.
