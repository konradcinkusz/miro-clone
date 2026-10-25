# Deployment

How the estate reaches Fly.io, what the owner does once, and what to check on each deploy. The decisions are in ADR 0015–0019. The
standards applied are `fly-io-deployment`, `pr-preview-environments` and `shared-service-reuse`.

**Status:** the configuration and workflows are in the repository and lint clean. **No deploy has been run.** Every item in
[“Verify after the first deploy”](#verify-after-the-first-deploy) is still open until the owner runs it.

## What runs where

| Piece | Where | Config |
|---|---|---|
| Web (Blazor BFF) | Fly app `miroclone-web-dev`, one machine, key ring on volume `web_keys` | `flyio/web.fly.toml` |
| BoardsService | `miroclone-boards-dev`, one machine | `flyio/boards-service.fly.toml` |
| CanvasService | `miroclone-canvas-dev`, one machine | `flyio/canvas-service.fly.toml` |
| authservice (pinned `v0.3.4`, its own key and database) | `miroclone-authservice-dev` | `flyio/authservice.fly.toml` |
| PostgreSQL 17, private, three databases with a role each | `miroclone-postgres-dev`, volume `postgres_data` | `flyio/postgres.fly.toml` |

Images are built from the repository root with the Dockerfiles beside each project (`MiroClone.*/Dockerfile`). The root `.dockerignore`
keeps `bin`, `obj`, `tests` and `docs` out of every build.

## Workflows

| Workflow | Trigger | Does |
|---|---|---|
| `flyio.yml` | push of a `v*` tag, or a manual run | test, then deploy in order: PostgreSQL, authservice, Canvas, Boards, Web, then verify JWKS and Web's health probe. Only services that changed, or whose app is missing, are deployed. |
| `flyio-pr-env-deploy.yml` | pull request (not draft) | a full preview: four apps named `miroclone-<service>-pr-<N>`, its own databases and authservice key, one sticky comment with the URLs |
| `flyio-pr-env-destroy.yml` | pull request closed, or a manual run with a PR number | destroys the preview's apps, databases and role; idempotent |
| `flyio-destroy-dev.yml` | manual, with the typed confirmation `destroy-dev` | destroys the dev application apps. PostgreSQL is kept unless `delete_data` is ticked. |
| `pr-validation.yml` (`docker-build`) | every pull request | builds the three images without pushing, so a broken Dockerfile fails before a tag |

## Owner steps, once

These cannot be done from the repository. Nothing below has been done.

1. **Check the Fly app names are free:** `miroclone-postgres-dev`, `miroclone-authservice-dev`, `miroclone-canvas-dev`, `miroclone-boards-dev`,
   `miroclone-web-dev`. Fly app names are global. If one is taken, rename it in its `flyio/*.fly.toml` and in the workflow's `APP_*` variables,
   in one PR.
2. **Create the GitHub environment `dev-fly`** and restrict its deployment branches and tags to `v*` tags and the default branch. The same
   environment serves pull requests, so its secrets reach previews. Decide whether that is acceptable for your repository before adding them.
3. **Create `FLY_API_TOKEN`** in that environment with `flyctl tokens create org`.
4. **Add the secrets** `POSTGRES_PASSWORD`, `BOARDS_DB_PASSWORD`, `CANVAS_DB_PASSWORD`, `AUTHSERVICE_DB_PASSWORD` (each `openssl rand -hex 24`) and
   `AUTHSERVICE_JWT_PRIVATE_KEY` (an RSA PKCS#8 PEM from `openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048`). The full table is in
   `flyio/SECRETS.md`.
5. **Add the variable `FLY_ORG`** if the apps belong to an organization other than `personal`.
6. **Confirm the authservice image is pullable** without credentials: `ghcr.io/konradcinkusz/authservice:v0.3.4`. If the package is private, the
   deploy job needs a registry credential. That is not configured here.
7. **Push the first tag** when you are ready to deploy. This branch does not push tags.

The first tag creates the apps and volumes that do not exist yet (fly-io-deployment §11).

## Verify after the first deploy

The first deploy is the first run of every path below, so check each one.

- [ ] `flyio.yml` ends green, and `verify` shows `JWKS publishes keys`.
- [ ] `curl -s https://miroclone-authservice-dev.fly.dev/.well-known/jwks.json` returns a `keys` array.
- [ ] `curl -s -o /dev/null -w "%{http_code}\n" https://miroclone-web-dev.fly.dev/health` returns `200`.
- [ ] A browser request from `https://miroclone-web-dev.fly.dev` is not refused by the origin guard (ADR 0016). `fly logs -a miroclone-web-dev` shows no 403 for the page's own requests.
- [ ] The Web machine started with the key ring written: `fly ssh console -a miroclone-web-dev -C "ls /data/keys"` lists a `key-*.xml` file. If the directory is not writable, the volume took root ownership: fix it once with `chown app:app /data/keys` over `fly ssh`, then redeploy (ADR 0017).
- [ ] Each service's database holds its own schema, and no role can read another's: `fly ssh console -a miroclone-postgres-dev -C "psql -U miroclone -d postgres -c '\l'"` lists the three databases.
- [ ] A second `flyio.yml` run with no change deploys nothing (only `verify` runs).

## Previews

Open a non-draft pull request from a branch of this repository. The preview workflow posts a single comment with the URLs. Closing the pull request
destroys it. To destroy one by hand, run `flyio-pr-env-destroy.yml` with its number. A preview's first request after idle is a cold start,
which is accepted for previews (ADR 0019).

## Operations

- **Roll back:** push a new tag that points at the previous good commit. Tags are never moved or deleted. A rollback is a deploy like any other.
- **Scale:** not built. Scaling any service past one machine is blocked until its migrations and the Web key ring can share state (ADR 0017,
  ADR 0018). Do not run `flyctl scale count` on these apps.
- **Destroy the dev estate:** run `flyio-destroy-dev.yml` with `destroy-dev`. Data is kept unless `delete_data` is ticked.
- **Rotate a database password:** `ALTER ROLE` on the postgres app, then re-stage the secret and redeploy the consumer (ADR 0015).

## Not done

- **Scale and manual scale workflow** (fly-io-deployment §12): not built. Blocked by ADR 0017 and ADR 0018.
- **Shared Data Protection keys across machines:** the key ring is shared across restarts and redeploys, not across machines (ADR 0017).
- **Scale to zero for the dev estate:** the four dev apps are always on, because every service is on a synchronous path (ADR 0015,
  `INFRASTRUCTURE-ANALYSIS.md`).
- **PostgreSQL over TLS:** the mesh is encrypted, and the image has no certificates. `SSL Mode=Disable` is recorded in ADR 0015.
- **Cold-start measurement for previews:** not measured, because no preview has run (ADR 0019).
- **Live verification:** no deploy has run, and no Fly or GHCR credential was available. The `docker-build` job proves the three images build in CI. It does not prove they run on Fly.

## Local checks

```bash
actionlint .github/workflows/*.yml             # workflow lint, what was run for this change
scripts/check-kernel-size.sh                    # kernel ceiling
docker build -f MiroClone.Web/Dockerfile -t miroclone-web:local .   # from the repository root, needs a Docker daemon
```
