# 0020 — Migrating services run one machine, and each deploy replaces it rather than overlapping

- **Status:** Accepted. Settles the decision ADR 0009 deferred to WP13.
- **Date:** 2026-10-25
- **Principles:** P4 (the schema is migrated, never ensured), P3 (one database per service); `fly-io-deployment` §5, §8, §12 (scaling a stateful
  app); `service-api-patterns` (startup order)

## Context

ADR 0009 runs migrations at startup, in the process that serves traffic, and notes that a deployment with several instances runs the same
migration concurrently. ADR 0009 left the choice to WP13: run one instance first, or serialise the migrations.

Serialising the migrations would need a lock or a separate migration job, which is more machinery than the estate needs. Keeping one instance
is simpler, and it is what `fly-io-deployment` §8 recommends for stateful apps.

## Decision

- **One machine per migrating service.** Boards, Canvas, authservice and Web each deploy with `--ha=false`. Their configs set
  `min_machines_running = 1`. Nothing scales them out.
- **`[deploy] strategy = "immediate"`** for Boards, Canvas and Web. Fly stops the old machine before it starts the new one, so the old and the
  new revision never migrate or serve at the same moment. This costs a short gap at each deploy, which is accepted for a dev estate.
- Migration runs under `Database:Provider = Npgsql` on the first start of each revision (ADR 0009). authservice uses
  `Database__SchemaMode = Migrate` (ADR 0011).

## Consequences

- Scaling a service out is blocked until its migrations move to a separate job or get a lock. That change belongs in its own ADR, and the
  `[deploy]` strategy must change with it.
- The gap at each deploy is a visible outage of a few seconds to the boards and canvas APIs. Web's open circuits are dropped at a deploy too,
  because circuit state is in memory (ADR 0001).
- The deploy workflow waits for each service's `/health` probe (ADR 0009: a failed migration stops startup, so the probe never answers) before
  it moves on.
