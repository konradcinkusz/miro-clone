# 0042 — The account pages stay in the interactive router; their forms still post natively

- **Status:** Accepted. Amends the account-page clause of ADR 0022 ("static server pages, outside the interactive router"). The rest of ADR 0022 stands: the forms post to `/account/*`, the tokens stay in the server-side session, and the cookie is written by the endpoint.
- **Date:** 2026-10-26
- **Principles:** blazor-web-app §3 and §13 (render modes are set on the root components, so every page is interactive; the pin tests read them from disk), §7 (sessions and authentication stay on the server); P5 (tokens never reach the browser); P13 (a defect that only a browser sees is tested in a browser); COMPLIANCE register R6

## Context

WP12's end-to-end run found that `/sign-up` answered "Page not found" on the directly started estate, and the authenticated journeys never reached a dashboard. The authenticated journeys could not be run in the session that found this, so the cause had to be read from the code.

The account pages carried `@attribute [ExcludeFromInteractiveRouting]`, so that the router would not handle them interactively. `App.razor` renders `<Routes @rendermode="InteractiveServer" />`, so the server first renders `/sign-up` correctly (the page answers 200 with its heading) and then the circuit starts on the same URL. The circuit's router does not have the excluded page in its table, and it renders the `NotFound` branch over the form. A request made with `curl` shows only the server response, which is why the defect survived the earlier sessions.

Reproduced in Chromium against the Release build of `MiroClone.Web`: `/sign-up` and `/sign-in` both showed `heading "Page not found"` after the circuit connected. With the attribute removed from the seven account pages, both headings render.

## Decision

- The seven account pages (`Consent`, `ForgotPassword`, `ResetPassword`, `SignIn`, `SignOut`, `SignUp`, `VerifyEmail`) do not carry `ExcludeFromInteractiveRouting`. The interactive router therefore has their routes, and the circuit renders them.
- Their forms keep `method="post"` and `action="/account/*"`. No `@onsubmit` and no enhanced form, so the browser posts natively and the endpoint answers with the redirect. The circuit is not needed to sign in, but it no longer blocks the page.
- The alternative, making the account pages fully static by moving the render mode from `Routes` to each dashboard page, was rejected for now. It would make every link between dashboard pages a full page load, and it would change the pin tests that state the interactive-only rule (`RenderModePinTests`, TESTING.md §3). That is a larger change than this defect needs.

## Consequences

- `HostPinTests` asserts the opposite of the old pin: the account pages must not carry the attribute. A regression of this defect is caught by unit tests, and in a browser by `AccountPageJourneyTests` (page-only, no authservice account needed, runs in CI).
- The browser check was made both ways: the journey passes with the fix and fails with the attribute restored (`heading "Page not found"`).
- The authenticated journeys (sign-up to dashboard, sign-out, boards) remain opt-in under ADR 0041 until they are run against a real authservice; see COMPLIANCE R6.
- The sign-up form shows its "service not reachable" alert when authservice is down, because the consent versions are read from it. That is intended, and the page-only journey does not assert the form for that reason.
