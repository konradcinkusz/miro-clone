# 0026 — Fly.io deployment is disabled by owner decision; the workflows keep manual dispatch only

- **Status:** Accepted. Overrides the automatic triggers set by ADR 0017–0021 for the estate's Fly workflows.
- **Date:** 2026-10-25
- **Principles:** owner instruction (no deployment to Fly.io for now); `fly-io-deployment` §10 and `pr-preview-environments` §1–§5 are
  deliberately not applied while this decision stands.

## Context

WP13 added the Fly.io deploy path: a `v*` tag deploys the estate (`flyio.yml`), each pull request gets a preview (`flyio-pr-env-deploy.yml`),
and a closed pull request tears its preview down (`flyio-pr-env-destroy.yml`). The owner has decided that nothing is deployed to Fly.io for
now.

## Decision

- **Every automatic trigger is removed** from the Fly workflows: the `v*` tag push in `flyio.yml`, the `pull_request` events in
  `flyio-pr-env-deploy.yml` and `flyio-pr-env-destroy.yml`. Nothing runs on a tag, a pull request, or a schedule.
- **`workflow_dispatch` is kept**, so a human can still start a workflow on purpose. This is a residual risk: a manual run of `flyio.yml`
  or the preview workflow still deploys. Do not start them while this decision stands.
- **Files are kept.** `flyio/`, the `MiroClone.*/Dockerfile` files and the `docker-build` job in `pr-validation.yml` are unchanged. The
  `docker-build` job is a CI image check, not a deployment.
- Each changed workflow carries the banner `DISABLED by owner decision: no Fly.io deployment.` at its top. `docs/DEPLOYMENT.md` carries the
  same status at its top.

## How to re-enable

Restore the automatic triggers and remove the banner, then update this ADR's status:

- `flyio.yml`: `on:` gets `push: { tags: ["v*"] }` above `workflow_dispatch:`.
- `flyio-pr-env-deploy.yml`: `on:` gets `pull_request: { types: [opened, synchronize, reopened, ready_for_review] }`.
- `flyio-pr-env-destroy.yml`: `on:` gets `pull_request: { types: [closed] }`.
- `flyio-destroy-dev.yml` never had an automatic trigger and needs no change.

Re-enabling is a new ADR that supersedes this one, and it goes through the `dev-fly` environment secrets (`flyio/SECRETS.md`) as before.

## Consequences

- No Fly.io app, database or image is created or changed by a merge or a tag while this decision stands.
- `docs/DEPLOYMENT.md` "Verify after the first deploy" stays open. No deploy has run and none is planned.
