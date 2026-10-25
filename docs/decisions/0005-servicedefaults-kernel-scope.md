# 0005 — MiroClone.ServiceDefaults holds plumbing only, under a mechanical ceiling

- **Status:** Accepted
- **Date:** 2026-10-25
- **Principles:** P2 (shared kernel, not shared domain) and its corollary P2a (every service calls `AddServiceDefaults()`);
  P8 (optional dependencies degrade); P15 (observability is a build-time decision); `service-api-patterns` §1 (rate limiting from the kernel)

## Context

The kernel is the one shared library on the hot path. The reference SaaS's kernel grew from 700 to 1,365 lines by carrying
seed data and a quota service, which is the failure P2 describes. Stating the limit in prose is not enough.

## Decision

- `MiroClone.ServiceDefaults` contains telemetry, health, service discovery, the one standard resilience handler, JWT
  validation, `WorkspaceClaims`, the standard rate-limit policies, the origin guard, the database provider extension and
  the optional-integration report. Nothing else.
- The ceiling is mechanical. `scripts/check-kernel-size.sh` fails `pr-validation` above 800 lines (currently 543).
- `tests/MiroClone.ServiceDefaults.Tests/ArchitectureTests.cs` asserts the kernel references no `MiroClone.*` assembly,
  declares no enum, and declares no `DbContext` or `DbSet`.
- Provider names are string constants, not an enum, because P2 forbids enums in the kernel.
- The kernel references EF Core and Npgsql only for the database provider extension, which P2's table lists as plumbing.

## Consequences

- **Not machine-checked:** the rule against user-facing strings and DTOs. The tests cannot see them. Review enforces it, and
  the next person to add a sentence to the kernel should move it to the owning service instead.
- Adding a second resilience handler, or an optional integration without a `Health` entry, is a review item, not a failure.
