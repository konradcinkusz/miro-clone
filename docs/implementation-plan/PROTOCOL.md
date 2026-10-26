# Sub-session protocol (applies to every work package)

You are an autonomous sub-session. You deliver exactly one work package (WP) from
[`WORK-PACKAGES.md`](WORK-PACKAGES.md) as one pull request, merge it, and stop. Nobody answers questions:
decide with the standards, record the decision, move on.

## 0. Orient (≤10 minutes)
1. Repo: `konradcinkusz/miro-clone`. Start from latest `main`: `git fetch origin main && git checkout -B <your-branch> origin/main`.
2. Read `docs/implementation-plan/README.md` (topology, **frozen contracts**), this file, and your WP section.
3. Read the standards your WP lists. They are in the `architecture-standards` plugin if enabled
   (`.claude/settings.json`); otherwise `add_repo` `konradcinkusz/architecture-standards` and read
   `docs/guides/*.md` and `docs/architecture/00-REFERENCE-ARCHITECTURE.md`. Never guess a rule you can read.
4. Read `AGENTS.md` / `CLAUDE.md` in the repo if present (WP00 creates them).
5. Reference repos may be attached read-only with `add_repo` (see README). Never copy code from them blindly.

## 1. Work
- Stay inside your WP scope. Touch only files your WP names plus the minimal wiring (`*.sln`, `AppHost`, `Directory.Packages.props`).
- Versions only in `Directory.Packages.props`; projects reference packages without versions. Warnings are errors.
- Tests at the layer holding the logic, written with the code, no network. PostgreSQL/Playwright tests skip unless
  `TEST_POSTGRES` / browser available, as in `aurelius-promptus`.
- No secrets in tree. Fixtures that look like secrets carry `gitleaks:allow`.
- No TODO placeholders presented as done. If you cannot finish an acceptance item, say so in the PR body under "Not done".
- Decisions that deviate from or interpret a standard → short ADR in `docs/decisions/NNNN-title.md` citing the principle.
- Commit messages: area prefix (`auth:`, `boards:`, `canvas:`, `web:`, `build:`, `ci:`, `docs:`), imperative summary, body says why.
  Follow the attribution lines given in your system reminder.

## 2. Prove (never skip, report what you ran)
```bash
dotnet build MiroClone.sln            # 0 warnings
dotnet test --solution MiroClone.sln     # all green; with TEST_POSTGRES if a local PostgreSQL is available
scripts/check-*.sh                    # repo guards, once they exist
```
Re-read your own diff adversarially (tenant isolation, tokens in browser, secrets, kernel purity) before pushing.

## 3. Deliver
1. `git push -u origin <your-branch>`; retry network errors up to 4× (2s,4s,8s,16s).
2. Open a PR (ready, not draft) with the repo template: what/why, WP id, acceptance checklist ticked with evidence, commands run
   and their results, decisions, "Not done". Link the WP.
3. Wait for CI (`pr-validation`, `secret-scan`). Red → root-cause, fix, push; never skip/disable a test, never empty-commit.
4. Conflicts with `main` (parallel WPs edit `MiroClone.sln`, `AppHost`, `Directory.Packages.props`): merge `main` into your branch,
   resolve keeping both sides' additions, rebuild, retest, push.
5. When CI is green and the PR is mergeable, **squash-merge it yourself** (`merge_pull_request`, method squash), then delete the branch.
6. Final message: PR link, merged yes/no, commands run, any follow-up the supervisor must schedule. Then stop.

If blocked on something only the owner can do (a missing secret, a repo setting), do not work around it silently: finish
everything else, merge what is green, and state the blocker precisely in the final message.
