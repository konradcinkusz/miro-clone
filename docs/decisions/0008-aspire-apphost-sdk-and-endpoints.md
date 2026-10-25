# 0008 — The AppHost uses the Aspire AppHost SDK, pinned in global.json, with explicit endpoints

- **Status:** Accepted
- **Date:** 2026-10-25
- **Principles:** P1 (the AppHost is the composition root and exists for development); P5 (configuration through the environment);
  AGENTS.md "Versions only in `Directory.Packages.props`"

## Context

P1 names the Aspire AppHost as the one command a developer runs. Two facts from building it (WP02) shaped the wiring:

1. The hosting package alone does not run. Without the AppHost SDK, `dotnet run` fails with "the path to the DCP executable is
   required" and "the Aspire Dashboard binaries are missing". The SDK's build targets supply both paths.
2. The SDK's build targets warn (ASPIRE010) when `AspireUseCliBundle` is false. The bundle is what the orchestration features need.
   The warning is an error here (warnings as errors).

The SDK is referenced by name, not by a package reference, so `Directory.Packages.props` does not govern its version. Writing the
version in the project file would put a version outside the one file that holds versions.

## Decision

- `MiroClone.AppHost` is `Microsoft.NET.Sdk` with `<Sdk Name="Aspire.AppHost.Sdk" />`. The SDK version lives in
  `global.json` under `msbuild-sdks`, next to the pinned .NET SDK. It is the only place an SDK version is written, and it is
  pinned exactly, as the packages are.
- `AspireUseCliBundle` is `true` in the AppHost project. This clears ASPIRE010 by enabling the feature, and nothing is suppressed.
- Projects are added by path (`AddProject(name, path)`), resolved from the repository root. Program.cs passes the root from
  `AppHostDirectory`; the model test passes it from the solution file. No generated `Projects.*` types are used, so the
  services are not compiled into the AppHost.
- Each project declares an `http` endpoint whose port Aspire assigns through `ASPNETCORE_HTTP_PORTS`. No `launchSettings.json` is
  needed per project. `WithHttpHealthCheck("/health")` requires that named endpoint.
- Only the web app is external (`WithExternalHttpEndpoints`).

## Consequences

- `dotnet run --project MiroClone.AppHost` needs the DCP and dashboard packages, which come from NuGet on first run.
- Aspire's own wait edges are kept: a service that references a database also waits for the PostgreSQL server that hosts it.
  The model test pins this, so the behaviour is visible in review.
- A later Aspire upgrade changes one line in `global.json` and the matching `Aspire.Hosting.*` versions in `Directory.Packages.props`.
