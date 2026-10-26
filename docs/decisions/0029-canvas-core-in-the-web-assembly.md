# 0011 — The canvas rules live in MiroClone.Web/Canvas/Core, plain C#, under a source-level isolation test

- **Status:** Accepted
- **Date:** 2026-10-25
- **Principles:** blazor-web-app "Logic outside the markup"; testing-strategy (logic is tested at the layer that holds it, without network);
  AGENTS.md "Versions only in `Directory.Packages.props`" and the dependency budget (no package is added for WP07)

## Context

WP07 is the geometry, history and mutation rules of the board canvas. WP09 renders them and WP10/WP11 send them over the hub. The rules
must be testable without a browser and without Blazor. The plan puts them in `MiroClone.Web/Canvas/Core`, not in a new project.

Two details of the standard shaped the choice:

1. A separate class library would be the stronger boundary, but it would add a project to the solution, which WP03, WP04 and WP05 also
   edit. It would also let the Web project reference a second assembly for logic that only the Web project uses.
2. Blazor types cannot be kept out by project references when the code sits in the Web assembly. They are kept out by a test that reads the
   source and fails on any `Microsoft.AspNetCore`, `Microsoft.JSInterop`, `MiroClone.Web.Components`, `@code` or `@inject` in `Canvas/Core`.

## Decision

- All Core types are in namespace `MiroClone.Web.Canvas.Core` under `MiroClone.Web/Canvas/Core/`. They are plain C# and use no Blazor or JS types.
- `tests/MiroClone.Web.Tests/Canvas/Core/CoreIsolationTests.cs` pins that boundary by reading the sources.
- Property-style tests use a seeded xorshift generator in the test fixtures (`TestRandom`). The generator is deterministic, so a failing case
  reproduces. No property-testing package is added. The fixture is not used for anything security-sensitive.
- Line coverage is measured with `dotnet-coverage` run from a user-level tool path, outside the repository. The acceptance target is
  ≥ 90% of Core. The measured result is in the PR body. CI does not enforce it, so the number is evidence, not a gate.
- No warning is suppressed. The analyzer findings found during the build were fixed in the code (CA1859 return types, CA1720 parameter names).

## Consequences

- Core and its tests share the Web assembly. The Web project is the only consumer, so no public API is needed beyond what the tests use.
- A future rule that needs a Blazor type cannot land in Core without failing `CoreIsolationTests`. The check is textual, so it catches a
  `using` or a reference by name. It does not prove the absence of every dependency. The compiler and review cover the rest.
- If a second consumer ever needs Core (for example a worker that replays mutations), the folder can move to its own project with one ADR.
