#!/usr/bin/env bash
# Runs the whole estate for development through the Aspire AppHost (P1): PostgreSQL, the boards and canvas services,
# and the web app. Needs a running Docker daemon for the PostgreSQL container. Extra arguments go to dotnet run.
set -euo pipefail

repo_root="$(git rev-parse --show-toplevel)"
cd "$repo_root"

exec dotnet run --project MiroClone.AppHost "$@"
