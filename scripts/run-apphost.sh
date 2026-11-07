#!/usr/bin/env bash
# One command for the developer path: the Aspire AppHost with its "http" launch profile (dashboard http://localhost:15170).
# Needs a running Docker daemon (PostgreSQL and authservice are containers) and the .NET 10 SDK. The authservice signing key is
# generated on the first run and kept in the AppHost's user secrets; nothing to create by hand (docs/AUTH.md, ADR 0049).
# Extra arguments go to dotnet run. Use `--launch-profile https` for https://localhost:17170 (run `dotnet dev-certs https --trust` once).
set -euo pipefail

repo_root="$(git rev-parse --show-toplevel)"
cd "$repo_root"

docker info > /dev/null 2>&1 || { echo "Docker is not running. Start Docker, then run this again." >&2; exit 1; }

# Build the whole estate once first. The AppHost starts the services (and the seed) as `dotnet run` at the same moment, and several
# builds of the shared ServiceDefaults project at once collide ("GenerateDepsFile ... file in use"), so a service never starts (ADR 0050).
dotnet build MiroClone.sln --nologo -v quiet

exec dotnet run --project MiroClone.AppHost --launch-profile http "$@"
