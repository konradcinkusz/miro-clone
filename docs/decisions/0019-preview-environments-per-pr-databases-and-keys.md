# 0019 — Preview environments: per-PR apps, databases and role on the shared PostgreSQL, a generated key per preview, and teardown on close

- **Status:** Accepted
- **Date:** 2026-10-25
- **Principles:** `pr-preview-environments` §2 (naming), §3 (shared vs per-PR, with the negatives stated), §4 (draft guard, concurrency, one sticky
  comment), §5 (teardown in three layers), §6 (scale to zero); `shared-service-reuse` §1 (independent signing key), §5 (database per instance,
  no cross-grants)

## Context

Each non-draft pull request gets its own copy of the estate. The standard leaves four things to decide for this estate.

1. **The name.** Static apps are `miroclone-<service>-dev`. The standard asks for a `-pr-{number}` suffix. A suffix on the full static name
   gives `miroclone-authservice-dev-pr-1234`, which is close to the length limit for Fly app names.
2. **Substrate.** Per-PR databases and their login role are created on the shared dev PostgreSQL. The databases are private, so the runner cannot
   reach them, and a role is needed to keep each preview's data apart from the others.
3. **The authservice key.** Each instance needs its own key (`shared-service-reuse` §1). A preview is disposable, so storing a key for it would
   be a credential kept for no reason.
4. **Cost.** The standard asks for `min_machines_running = 0` in previews. The configs in `flyio/` set 1, because they serve the dev estate.

## Decision

- **Names:** `miroclone-<service>-pr-<N>` for the four preview apps (`authservice`, `canvas`, `boards`, `web`). The number replaces the
  environment segment, which keeps every name under the length limit. Teardown matches exactly these names.
- **Substrate:** `scripts/flyio-pr-database.sh create|drop <N>` creates or drops `boards_pr_<N>`, `canvas_pr_<N>`, `authservice_pr_<N>`, and the
  role `pr_<N>` that owns all three. The SQL runs through `flyctl ssh console`, base64-encoded, and every statement is idempotent. Each service
  still has its own database. The role is shared by one PR's three databases, which is a deliberate departure from the per-service roles of
  ADR 0015. It holds only for a disposable PR, and it means no preview role can reach another PR's databases.
- **Password:** generated per run with `openssl rand -hex 24`, masked, and held in `GITHUB_ENV` for the rest of the job. It is never written
  to the tree or to a secret store.
- **authservice key:** `openssl genpkey` (PKCS#8, RS256) per run, passed to `flyctl secrets set --stage` and not stored anywhere.
- **Configs:** generated from `flyio/*.fly.toml` into the untracked `flyio/.generated/`, with the app name replaced and
  `min_machines_running = 0`. The per-PR addresses (`Jwt__PublicBaseUrl`, `Auth__Authority`, `Canvas__BaseUrl`) are passed with `--env`, and the
  images are deployed by `--image` from the head commit.
- **Draft guard:** a job with no environment binding, so drafts never wait for approval (§4). Fork PRs are skipped, because they have no secrets.
- **Opt-in:** both preview workflows run only when the repository variable `FLYIO_PREVIEWS` is `true`. The variable is unset until the dev estate exists, so a repository without it does not fail on every pull request.
- **Concurrency:** one group per PR number, with `cancel-in-progress: false`.
- **Sticky comment:** one comment per PR, created or updated in place, marked with `<!-- miroclone-pr-env -->`. Teardown rewrites it to "destroyed".
- **Teardown:** on PR close, the workflow destroys the four apps, drops the databases and the role, and skips anything already gone. The same
  workflow also runs on demand with a PR number.

## Consequences

- Scale to zero holds for all four preview apps (`min_machines_running = 0`). The cost of a cold first request was not measured, because no preview
  has been deployed yet. Measuring it is a follow-up for the owner's first preview.
- Previews are not built from the static estate's images. Each PR builds its own, so a preview runs the code under review.
- Preview data lives on the shared dev PostgreSQL, and a preview's teardown drops it. A preview whose teardown failed keeps its databases until the
  manual run removes them.
- Web's preview key ring is on a 1 GB volume, which is cheap and keeps the preview close to the dev estate's behaviour.
- The environment's protection rules apply to teardown too. If the owner adds a required reviewer to `dev-fly`, a closed PR waits for approval.
