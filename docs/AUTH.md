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

1. Create a signing key outside the repository. Keep it out of git.

   ```bash
   openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out ~/miroclone-jwt-dev.pem
   dotnet user-secrets set "Parameters:authservice-jwt-private-key" "$(cat ~/miroclone-jwt-dev.pem)" --project MiroClone.AppHost
   ```

   The AppHost reads this parameter when it starts. Without it the authservice container cannot be configured, so the estate does not start.

2. Start the estate. It needs a Docker daemon for PostgreSQL and authservice.

   ```bash
   scripts/run-local-stack.sh
   ```

   Open the Aspire dashboard and note the `authservice` endpoint, for example `http://localhost:<port>`.

3. Seed two users in one organization (owner and member). The seed is safe to run again.

   ```bash
   scripts/seed-dev-users.sh http://localhost:<port>
   ```

   The member's invitation token is written to the authservice log, not returned. On the first run the script stops and
   says so. Find the newest `Invitation token:` line in the dashboard's authservice log, then run:

   ```bash
   INVITATION_TOKEN=<token> scripts/seed-dev-users.sh http://localhost:<port>
   ```

   `SEED_PASSWORD` sets the password for both seed users. The default is for development only.

4. Check the protected path. Use the boards-service address from the dashboard.

   ```bash
   # Refused without a token: 401
   curl -s -o /dev/null -w "%{http_code}\n" http://localhost:<boards>/api/session

   # Accepted with a token: 200, with the user and the workspace
   TOKEN=$(curl -s -X POST http://localhost:<authservice>/api/v1/auth/login \
     -H 'Content-Type: application/json' \
     -d '{"email":"owner@dev.test","password":"<SEED_PASSWORD>"}' | jq -r .accessToken)
   curl -s -H "Authorization: Bearer $TOKEN" http://localhost:<boards>/api/session
   ```

## The HTTP surface this repository uses

| Call | authservice route | Used by |
|---|---|---|
| Register | `POST /api/v1/auth/register` | `scripts/seed-dev-users.sh` |
| Sign in | `POST /api/v1/auth/login` | seed script; WP06 |
| List organizations | `GET /api/v1/organizations` | seed script |
| Create organization | `POST /api/v1/organizations` | seed script |
| Invite | `POST /api/v1/organizations/{id}/invite` | seed script |
| Accept invitation | `POST /api/v1/organizations/invitations/accept` | seed script |
| Sign in | `POST /api/v1/auth/login` | web app sign-in (WP06) |
| Refresh | `POST /api/v1/auth/refresh` | web app session refresh (WP06, ADR 0018) |
| Sign out | `POST /api/v1/auth/logout` | web app sign-out (WP06) |
| Current user and consent status | `GET /api/v1/auth/me` | web app consent check at sign-in (WP06, ADR 0020) |
| Consent versions and acceptance | `GET /api/v1/auth/consents/versions`, `POST /api/v1/auth/consents` | web app sign-up and consent page (WP06) |
| Verify, resend, forgot, reset | `POST /api/v1/auth/verify-email`, `resend-verification`, `forgot-password`, `reset-password` | web app account pages (WP06) |
| List organizations | `GET /api/v1/organizations` | web app workspace state (WP06, ADR 0019) |

Organization membership reaches a token only when that token is issued. A user who joins an organization must sign in
again before `organization` appears (`TokenService.BuildClaimsAsync`).

## Sign-in in the web app (WP06)

The web app is a backend-for-frontend: its account pages post to `/account/*` endpoints, which call authservice and keep the
tokens in a server-side session. The browser holds an HttpOnly, SameSite=Strict cookie that names the session and carries no
token. The decisions are ADR 0017 (the session model), ADR 0018 (refresh), ADR 0019 (the workspace) and ADR 0020 (verification,
consent and reset).

Running it with the estate: sign up at `/sign-up`, then sign in at `/sign-in`. A new account has no organization, so the
dashboard says so. To see the workspace, create an organization through authservice's API, then sign in again: the token is
issued afresh and carries it. Two or more organizations show the choice, and boards stay closed until authservice issues a
token for one (ADR 0019).

## Not done in this work package

- **Choosing an active workspace** for users with several organizations. authservice issues no organization-scoped token, so
  the dashboard states the choice and waits. Owner decision, see ADR 0011 and ADR 0019.
- **Two-factor sign-in.** A second-factor challenge is refused with a message (ADR 0020).
- **Consent is checked at sign-in only** (ADR 0020). The dashboard does not re-check it on each page.
- **One web instance.** Sessions live in the process's memory (ADR 0017). Shared sessions and affinity come with a deployment decision (WP13).
- **Production configuration:** `Database__SchemaMode=Migrate`, https metadata, and secrets from the platform (WP13).
