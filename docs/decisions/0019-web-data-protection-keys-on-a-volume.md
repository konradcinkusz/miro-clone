# 0019 — The Web app persists its Data Protection key ring to a volume, and runs on one machine

- **Status:** Accepted
- **Date:** 2026-10-25
- **Principles:** `blazor-web-app` (the circuit is the BFF; per-circuit state lives in server memory, as ADR 0001 records); `fly-io-deployment`
  §5 (stateful apps: a volume, `--ha=false`), §8 (a volume is local to one machine); P5 (configuration through the environment)

## Context

WP13 asks for "shared Data Protection keys" on the Web app. ASP.NET uses Data Protection for antiforgery tokens, and later for any cookie
(WP06). Keys kept in memory are lost on every restart, and a second machine would hold a different key ring, so tokens minted on one machine
would fail on the other.

Three options were considered:

- **A key ring in a database** (`PersistKeysToDbContext`). Web has no database (the BFF holds no data), and adding one only for keys would
  break the "no data outside its owner" rule (P3). It would also need an EF context in the BFF.
- **A key ring on a Fly volume** (`PersistKeysToFileSystem`). This survives restarts but binds the key ring to one machine.
- **A shared object store.** Not in the estate, and it would add a dependency the plan does not name.

## Decision

- `DataProtection:KeysPath` (set to `/data/keys` in `flyio/web.fly.toml`) turns on `PersistKeysToFileSystem` with the application name
  `MiroClone.Web`. When the setting is absent, the framework default is unchanged, so local runs are not affected.
- The key ring lives on the volume `web_keys` (1 GB, `fra`), mounted at `/data/keys`. The image creates the directory and gives it to the `app` user.
- Web runs on **one machine** (`min_machines_running = 1`, `[deploy] strategy = "immediate"`). A volume is bound to one machine, and Blazor
  circuits are in memory, so a second machine could not serve a circuit anyway. This is also the reason the key ring can stay on a volume.

## Consequences

- "Shared" in WP13 is satisfied across restarts and redeploys, not across machines. Scaling Web out is blocked until keys move to a store all
  machines can read. That change needs its own ADR, and it belongs with the work on circuit affinity (WP10).
- If the volume is lost, the key ring is lost. Antiforgery tokens and any cookie issued before then become invalid, and users sign in again.
  Nothing else is lost.
- The image pre-creates `/data/keys` and gives it to the `app` user, so the key ring can be written on first boot. A fresh Fly volume may take its
  ownership from the mount point or not. This was not verified on Fly, because no deploy was run. The owner's first deploy must confirm that the
  key ring is written and the circuit starts. `DEPLOYMENT.md` gives the check.
- `DataProtectionKeysTests` pins that the configured path receives a key file.
