# Authentication and claims

authservice (`ghcr.io/konradcinkusz/authservice:v0.3.4`, pinned in `MiroClone.AppHost`) is the only identity provider
(ADR 0002). It owns users, organizations and the signing key. This repository holds none of them. The decisions behind
this page are in [ADR 0011](decisions/0011-authservice-adoption-and-token-contract.md) and [ADR 0007](decisions/0007-jwt-validation-and-workspace-claim.md).

## Tokens the services accept

Every service validates the same bearer token against authservice's JWKS (`MiroClone.ServiceDefaults`, `AddJwtValidation`).

| Rule | Value |
|---|---|
| Algorithm | RS256 only. An HS256 token is refused. |
| Issuer (`iss`) | `MiroClone` (`Auth__Issuer`) |
| Audience (`aud`) | `miro-clone` (`Auth__Audience`). One audience for the estate. |
| Keys | authservice's discovery document and `/.well-known/jwks.json`. No signing key in any service. |
| Clock skew | 30 seconds. Expiry is enforced. |

## Claims

| Claim | Issued by authservice as | Read by | Meaning here |
|---|---|---|---|
| `sub` | the user id (GUID) | `WorkspaceClaims.UserId` | The caller. |
| `email` | the account email | none yet | Display only. |
| `email_verified` | `"true"` or `"false"` on sign-in and refresh | none yet | Whether the address is confirmed. |
| `organization` | one claim per membership, with the organization id (GUID) | `WorkspaceClaims.WorkspaceId` | The workspace. |
| `organization:{id}:role` | one claim per membership, for example `Owner`, `Admin`, `Member` | none yet | Role inside that workspace. Used by WP04. |

**There is no `workspace` claim and no active-organization claim.** `WorkspaceClaims.WorkspaceId` returns the id only when
the token carries exactly one `organization`. A user in two organizations gets null, so every board endpoint refuses
them (fail closed, ADR 0011). Choosing an active workspace needs an authservice change, which is an owner decision.

## Running it in development

1. **The signing key needs no step.** `scripts/run-apphost.sh` generates an RSA-2048 key on the first run and Aspire keeps it in the
   AppHost's user secrets (`Parameters:authservice-jwt-private-key`); `scripts/compose-up.sh` and `scripts/run-local-stack.sh` write one to
   `.local/` (gitignored). To use your own key, set the user secret:

   ```bash
   openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out ~/miroclone-jwt-dev.pem
   dotnet user-secrets set "Parameters:authservice-jwt-private-key" "$(cat ~/miroclone-jwt-dev.pem)" --project MiroClone.AppHost
   ```

   Without any key authservice cannot be configured and every service that waits for it never starts: that was the first failure
   of the first local run (ADR 0049). Never reuse this key outside development.

2. Start the estate. It needs a Docker daemon for PostgreSQL and authservice.

   ```bash
   scripts/run-apphost.sh      # Aspire; or scripts/compose-up.sh; or scripts/run-local-stack.sh
   ```

   The authservice endpoint is on the dashboard (AppHost), or <http://localhost:5080> (compose, run-local-stack).

3. **The demo users are seeded for you** (ADR 0050). Every start of the estate runs the seed after authservice, boards-service and
   canvas-service are healthy: `scripts/run-apphost.sh` (a project resource, Development only), `scripts/compose-up.sh` (a one-shot `seed`
   container) and `scripts/run-local-stack.sh` do it through the same program, `MiroClone.Seed`. It talks to the public APIs only, never to
   a database, and a second run changes nothing.

   | Who | Email | Password | Role | Organization |
   |---|---|---|---|---|
   | Owner | `owner@dev.test` | `Dev-Password-123!` | Owner | `Dev Workspace` |
   | Admin | `admin@dev.test` | `Dev-Password-123!` | Admin | `Dev Workspace` |
   | Member | `member@dev.test` | `Dev-Password-123!` | Member | `Dev Workspace` |
   | Other | `other@dev.test` | `Dev-Password-123!` | Owner | `Other Workspace` |

   These are published development values, not secrets: they satisfy authservice's password rule and exist only on your machine. The
   seed refuses to run unless `ASPNETCORE_ENVIRONMENT=Development`. It creates three boards whose titles start with `Demo: ` in the
   `Dev Workspace` (`Welcome` and `Sprint planning` with shapes, and an empty one) and `Demo: Other workspace board` in the `Other Workspace`.
   That prefix, the `dev.test` addresses, the organization names and layer ids derived from fixed names are the seed's namespace: `--reset`
   removes only boards in it. The admin and the member are invited by the owner with their role (`Admin`, `Member`) and accept; the other
   user founds its own organization. **No user is in both organizations**, because the web app uses the single organization in the token
   (ADR 0024, above), so the two workspaces show isolation: the other workspace's board answers "Board not found", never "forbidden".
   Roles: Owner and Admin see "Invite members" on the dashboard; Member does not.
   The sign-in page shows the four logins with a one-click fill, only in Development with `Seed:Enabled` true (ADR 0051).

   - Switch it off: `dotnet run --project MiroClone.AppHost -- --Seed:Enabled=false`, or `SEED_ENABLED=false scripts/compose-up.sh`.
   - Run it by hand against a running estate, or reset the demo boards: `scripts/seed-dev-users.sh [--reset]`. Check it: `scripts/verify-seed.sh`.
   - Each invitation token (admin, member) is not returned by authservice. Its dev outbox prints it to the container's console, and the seed
     reads it from there over the Docker socket (`Seed__AuthServiceContainer`; compose mounts the socket into the seed container, which is
     why that container runs as root). Without a Docker socket set `Seed__AuthServiceLogFile` or `Seed__InvitationToken`.

4. Check the protected path. Use the boards-service address from the dashboard.

   ```bash
   # Refused without a token: 401
   curl -s -o /dev/null -w "%{http_code}\n" http://localhost:<boards>/api/session

   # Accepted with a token: 200, with the user and the workspace
   TOKEN=$(curl -s -X POST http://localhost:<authservice>/api/v1/auth/login \
     -H 'Content-Type: application/json' \
     -d '{"email":"owner@dev.test","password":"Dev-Password-123!"}' | jq -r .accessToken)
   curl -s -H "Authorization: Bearer $TOKEN" http://localhost:<boards>/api/session
   ```

## The HTTP surface this repository uses

| Call | authservice route | Used by |
|---|---|---|
| Register | `POST /api/v1/auth/register` | `MiroClone.Seed` |
| Sign in | `POST /api/v1/auth/login` | seed; WP06 |
| List organizations | `GET /api/v1/organizations` | seed |
| Create organization | `POST /api/v1/organizations` | seed |
| Invite | `POST /api/v1/organizations/{id}/invite` | seed |
| Accept invitation | `POST /api/v1/organizations/invitations/accept` | seed |
| Sign in | `POST /api/v1/auth/login` | web app sign-in (WP06) |
| Refresh | `POST /api/v1/auth/refresh` | web app session refresh (WP06, ADR 0023) |
| Sign out | `POST /api/v1/auth/logout` | web app sign-out (WP06) |
| Current user and consent status | `GET /api/v1/auth/me` | web app consent check at sign-in (WP06, ADR 0025) |
| Consent versions and acceptance | `GET /api/v1/auth/consents/versions`, `POST /api/v1/auth/consents` | web app sign-up and consent page (WP06) |
| Verify, resend, forgot, reset | `POST /api/v1/auth/verify-email`, `resend-verification`, `forgot-password`, `reset-password` | web app account pages (WP06) |
| List organizations | `GET /api/v1/organizations` | web app workspace state (WP06, ADR 0024) |

Organization membership reaches a token only when that token is issued. A user who joins an organization must sign in
again before `organization` appears (`TokenService.BuildClaimsAsync`).

## Sign-in in the web app (WP06)

The web app is a backend-for-frontend: its account pages post to `/account/*` endpoints, which call authservice and keep the
tokens in a server-side session. The browser holds an HttpOnly, SameSite=Strict cookie that names the session and carries no
token. The decisions are ADR 0022 (the session model), ADR 0023 (refresh), ADR 0024 (the workspace) and ADR 0025 (verification,
consent and reset).

Running it with the estate: sign up at `/sign-up`, then sign in at `/sign-in`. A new account has no organization, so the
dashboard says so. To see the workspace, create an organization through authservice's API, then sign in again: the token is
issued afresh and carries it. Two or more organizations show the choice, and boards stay closed until authservice issues a
token for one (ADR 0024).

## Not done in this work package

- **Choosing an active workspace** for users with several organizations. authservice issues no organization-scoped token, so
  the dashboard states the choice and waits. Owner decision, see ADR 0011 and ADR 0024.
- **Two-factor sign-in.** A second-factor challenge is refused with a message (ADR 0025).
- **Consent is checked at sign-in only** (ADR 0025). The dashboard does not re-check it on each page.
- **One web instance.** Sessions live in the process's memory (ADR 0022). Shared sessions and affinity come with a deployment decision (WP13).
- **Production configuration:** done in WP13. `flyio/authservice.fly.toml` sets `Database__SchemaMode=Migrate` and RS256, and the key and database come from platform secrets. See [DEPLOYMENT.md](DEPLOYMENT.md).
