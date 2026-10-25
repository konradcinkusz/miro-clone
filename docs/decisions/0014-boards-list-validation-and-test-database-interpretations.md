# 0014 — Boards list, validation and test-database interpretations

- **Status:** Accepted
- **Date:** 2026-10-25
- **Principles:** service-api-patterns §3 (validation, one shared rule), §4 (clamp list inputs, do not reject them); P4 (persistence is
  provider-portable and its schema is migrated, never "ensured"); P13 (test at the layer that holds the logic); the frozen contract
  (`GET` paginates with `page`/`pageSize<=50`; titles are 1–60 characters after trimming)

## Context

The contract fixes the shapes but leaves several points open: whether a bad page size is rejected or clamped, the sort order, how search
matches, what a rename returns, and which database the endpoint tests use. Each one is an interpretation, and each is recorded here.

## Decision

1. **Clamp, do not reject, list inputs.** `pageSize` is clamped to 1–50 (default 20) and `page` to at least 1, and the page is capped so
   the offset fits an int. A request above the limit is served, not refused.
2. **Newest first.** Boards are ordered by `createdAt` descending, then by `id`, so pages do not reshuffle when two rows share a timestamp.
3. **Search is a case-insensitive "contains" on a stored key.** `Board.TitleSearch` holds the title lowered with the invariant culture,
   written on every rename. The query compares against that column. SQLite folds ASCII only and PostgreSQL folds by locale, and the
   analyzers reject a locale-dependent `ToLower()` in a query, so folding at write time is the one approach that is the same on both.
   `%` and `_` are plain text, because the match is `contains`, not `LIKE`.
4. **Rename answers 200 with the `BoardSummary`.** The contract does not name the status. A `PATCH` that returns the new state saves the
   client a second read.
5. **A token with no active workspace answers 403.** The token is valid, so this is not 401. It names no workspace, so no board can be
   reached. It is refused, never defaulted to an empty workspace.
6. **The author's name is copied at creation** from the `name` claim, falling back to `Member`, truncated to 200 characters. This service
   holds no user directory (P3), and a later name change does not rewrite history.
7. **Migrations are provider-specific, so endpoint tests use the in-memory provider.** The migrations are generated for PostgreSQL, so
   their column types are PostgreSQL types. Applying them to SQLite raises `PendingModelChangesWarning`, and EF refuses to start. P4's
   "provider-portable" applies to the code and the model, not to generated migration files. The in-memory provider has no schema and skips
   migration (the kernel checks `IsRelational`), so the endpoint tests run anywhere. Each test instance gets its own database name, so
   tests do not share data. The schema itself is verified where the real provider runs: `PostgreSqlMigrationTests` and
   `PostgreSqlBoardsTests`, both gated on `TEST_POSTGRES`.
8. **Gated PostgreSQL tests use one database per test class.** Classes run in parallel, and two creating the same database race.

## Consequences

- A client that sends `pageSize=500` gets 50 items and a `pageSize` of 50 in effect. The contract's limit holds without a 400 path.
- Endpoint tests do not exercise the relational provider. The PostgreSQL-gated tests do, and they run only where a server is available.
  CI does not provide one, so those tests skip there (`skipped` in the summary). Running them in CI needs a PostgreSQL service in
  `pr-validation`, which is a CI decision for the owner and is listed under "Owner actions" in the PR body.
- The kernel's Sqlite test pattern (WP02's `BoardsServiceHostTests` migration check) is retired for this service. Its migration assertion
  moved into the gated PostgreSQL test.
