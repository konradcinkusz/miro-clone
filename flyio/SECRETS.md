# Fly.io DEV: secrets and variables

Nothing secret is in this tree. Secrets live in **GitHub → Settings → Environments → `dev-fly`**. The workflows (`flyio.yml`,
`flyio-pr-env-deploy.yml`, `flyio-pr-env-destroy.yml`, `flyio-destroy-dev.yml`) read them and stage them on each Fly app with
`flyctl secrets set --stage`, so one release does not restart a service twice (fly-io-deployment §9). Non-secret configuration is in
`[env]` in each `flyio/*.fly.toml`.

## Owner setup, once

1. Create the environment `dev-fly`. Under *Deployment branches and tags*, allow `v*` tags and the default branch only.
   Pull requests from this repository use the same environment, so the protection rules apply to previews too.
2. Add the secrets below to `dev-fly`. Generate each random value with `openssl rand -hex 24`.
3. Add the variable `FLY_ORG` to `dev-fly` if the Fly organization is not `personal`.

## Secrets (environment `dev-fly`)

| Secret | Used by | How to obtain | Set on Fly by |
|---|---|---|---|
| `FLY_API_TOKEN` | every Fly job | `flyctl tokens create org` (an organization token, not a personal one) | — (the CI credential) |
| `POSTGRES_PASSWORD` | PostgreSQL superuser `miroclone` | `openssl rand -hex 24` | `flyio.yml` → `miroclone-postgres-dev` |
| `BOARDS_DB_PASSWORD` | role `boards_app` (database `boards`) | `openssl rand -hex 24` | postgres (init script) and `miroclone-boards-dev` |
| `CANVAS_DB_PASSWORD` | role `canvas_app` (database `canvas`) | `openssl rand -hex 24` | postgres (init script) and `miroclone-canvas-dev` |
| `AUTHSERVICE_DB_PASSWORD` | role `authservice_app` (database `authservice`) | `openssl rand -hex 24` | postgres (init script) and `miroclone-authservice-dev` |
| `AUTHSERVICE_JWT_PRIVATE_KEY` | authservice's RS256 signing key (PKCS#8 PEM) | `openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048`; paste the whole PEM as a multi-line secret | `miroclone-authservice-dev` as `Jwt__PrivateKeyPem` |

Rules for these:

- **The signing key is this estate's alone.** Never reuse the key of another authservice instance, and never use the local development key
  from `docs/AUTH.md`. Sharing a key shares a trust root (`shared-service-reuse` §1).
- **The role passwords are read once.** The PostgreSQL init script reads them on the first boot of an empty volume. Changing a secret later does
  not change the database. Rotate with `ALTER ROLE <role> PASSWORD '…'` on the postgres app, then re-stage the secret (ADR 0015).
- The deploy jobs fail when a required secret is empty (`: "${NAME:?…}"`). An ephemeral key would invalidate every token on restart.
- Preview environments generate their own `PR_DB_PASSWORD` and authservice key on every run. Neither is stored (ADR 0019).

## Variables (environment `dev-fly`)

| Variable | Default | Purpose |
|---|---|---|
| `FLY_ORG` | `personal` | Fly organization that owns the apps. |

## Fly app names and where each is set

| App | Config | Secrets it holds |
|---|---|---|
| `miroclone-postgres-dev` | `flyio/postgres.fly.toml` | `POSTGRES_PASSWORD`, the three service passwords |
| `miroclone-authservice-dev` | `flyio/authservice.fly.toml` | `ConnectionStrings__DefaultConnection`, `Jwt__PrivateKeyPem` |
| `miroclone-canvas-dev` | `flyio/canvas-service.fly.toml` | `ConnectionStrings__canvas` |
| `miroclone-boards-dev` | `flyio/boards-service.fly.toml` | `ConnectionStrings__boards` |
| `miroclone-web-dev` | `flyio/web.fly.toml` | none (the BFF holds no database or signing key) |

The connection strings are assembled in the workflow from the password and the fixed host `miroclone-postgres-dev.internal`. They are never
stored in the repository.
