# 0038 — The end-to-end suite is Playwright for .NET, and it starts the estate itself on fixed ports with an empty database

- **Status:** Accepted. The start decision is amended by ADR 0041: in CI the estate is started from containers and built services, and the AppHost is an opt-in path (`MIROCLONE_E2E_APPHOST=1`).
- **Date:** 2026-10-26
- **Principles:** blazor-web-app §1 and §13 (no Node toolchain in a .NET Blazor repository; journeys wait for the circuit, use test ids and roles, and run in their own job); e2e-acceptance-testing §3 (locator convention), §5 (independence and cleanup decided per environment), §6 (CI wiring is part of done); testing-strategy §4 (ephemeral environments for E2E); ADR 0001 (no Node toolchain); ADR 0011 (the signing key is per instance and never in the tree)

## Context

WP12 asks for a Playwright suite against the whole estate. Three choices needed a record.

1. **Which Playwright.** `AGENTS.md` allowed the .NET package or the pre-installed Node package. ADR 0001 keeps Node out of the repository, so the .NET package is the one that fits.
2. **Who starts the estate.** The AppHost assigns every port at random and keeps PostgreSQL's data volume between runs. A journey that needs the web app's address, or a fresh database, cannot get either from the outside.
3. **What the authservice dev outbox is.** The invitation tokens reach a developer only through the authservice container's log (ADR 0011). The journeys read the same log.

## Decision

- **Playwright for .NET** (`Microsoft.Playwright`, pinned in `Directory.Packages.props` to the driver that matches the pre-installed chromium). The project is `tests/MiroClone.Web.E2E`.
- **The suite starts the AppHost** as a child `dotnet run` unless `MIROCLONE_E2E_BASE_URL` names a running estate. It passes three settings through the environment:
  - `Estate:WebPort` and `Estate:AuthServicePort`, free loopback ports chosen by the fixture, so the journeys know the addresses without parsing Aspire's output;
  - `Estate:EphemeralData=true`, which starts PostgreSQL without its data volume, so each run begins with an empty database;
  - `Parameters:authservice-jwt-private-key`, an RSA key generated for the run and never written to the tree or to user secrets.
- **The AppHost reads the three settings as optional.** Unset, the estate is exactly as before: Aspire assigns the ports, and PostgreSQL keeps its volume.
- **The dev outbox is `docker logs` of the authservice container**, found by its image. A journey invites a member, waits for the new `Invitation token:` line, and accepts it. This is the flow `scripts/seed-dev-users.sh` follows.
- **The journeys share one estate and run one after another** (one collection). Each creates its own accounts and boards, and none depends on another.

## Consequences

- The suite needs Docker and a Chromium. CI has both; a developer runs it with `dotnet test --project tests/MiroClone.Web.E2E`.
- A cold run takes minutes, because the images and the Aspire dashboard are fetched on first use.
- **Cleanup is by environment, not by test.** The ephemeral database is discarded with the run (e2e §5). A run against a persistent estate through `MIROCLONE_E2E_BASE_URL` keeps the accounts it created; only the boards the delete journey removes are cleaned up. That case is documented, not hidden.
- The suite signs in through the real account pages, but it creates accounts through authservice's API. That keeps each journey independent of the registration form, which has its own two journeys.
