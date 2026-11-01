# 0049. The estate is run for real: generated dev key, launch profiles, and a compose estate that CI drives with a browser

Status: accepted. Applies the reference architecture's "verify what you ship" rule (a composed estate that nobody started is not verified) and register item R1.

## Context

The owner ran the estate locally and it did not work: the Aspire console was empty, authservice did not start, and the services did not come up.
Earlier work was declared done from unit tests; CI built the services but never ran the composed estate with the real sign-in flow, and the
authenticated browser journeys were skipped by default (ADR 0041).

## Decisions

1. **Signing key.** The AppHost declares `authservice-jwt-private-key` with a generated default (`GeneratedRsaKeyDefault`, RSA-2048 PKCS#8) and
   `persist: true`, so the first run stores it in user secrets. A key already set (user secret, environment) wins. Nothing enters the tree.
2. **Launch profiles.** `MiroClone.AppHost/Properties/launchSettings.json` has `http` (default, dashboard 15170, OTLP 19170, unsecured transport allowed)
   and `https` (17170/21170). Without a profile the dashboard and OTLP endpoints are unset and the AppHost prints nothing.
3. **compose.yaml** mirrors the AppHost wiring (databases, env vars, health-gated start order). The web service sets `DataProtection__KeysPath=/data/keys`
   on a volume, because the image's user cannot write the default `/app/App_Data/keys`. Aspire's DCP does not run on GitHub runners, so compose is the CI path.
4. **CI.** `compose-e2e` builds the three images, starts the estate, and runs the existing Playwright journeys against it with
   `MIROCLONE_E2E_AUTHENTICATED=1` and `MIROCLONE_E2E_REQUIRED=1`. The required check `pr-validation` is now an aggregate of `unit`, `e2e`, `compose-e2e` and `docker-build`.
5. **The Web image publishes with a restore.** The first containerized run showed `GET /_framework/blazor.web.js` answering 404: the Dockerfile restored from project files only and then published with `--no-restore`, which leaves out the static web assets (the Blazor client, the stylesheet, the endpoints manifest). The image would have served an unstyled page and no circuit, in Fly as well. The publish no longer uses `--no-restore`.
6. **No auth bypass.** Real sign-in through authservice is the only path. A `DevBypass` was reported on the owner's machine; it is not on `main` or any branch and is not added.

## Consequences

Development settings in compose (`ASPNETCORE_ENVIRONMENT=Development`, plain http) are for localhost and CI only. Deployments are unchanged (ADR 0017, ADR 0026).
