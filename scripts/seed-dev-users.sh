#!/usr/bin/env bash
# Seeds the demo estate (four users in two organizations, and the demo boards) into a running estate, by running the same program the
# AppHost and compose.yaml run (MiroClone.Seed, ADR 0050 and 0051). Safe to run again: a second run changes nothing.
# Development only; the program refuses to run in any other environment.
#
# Usage: scripts/seed-dev-users.sh [--reset]
#   --reset              first removes the demo boards (titles starting "Demo: ") and then creates them again. Nothing else is touched.
#   AUTHSERVICE_URL      default http://localhost:5080 (compose). With the AppHost, use the address on the dashboard.
#   BOARDS_URL           default http://localhost:5081 (compose). With the AppHost, use the address on the dashboard.
#   CANVAS_URL           default http://localhost:5082 (compose). With the AppHost, use the address on the dashboard.
#   AUTHSERVICE_CONTAINER   the authservice container, whose log holds the invitation tokens
#                           (default miroclone-authservice-1 for compose; miroclone-authservice-apphost for the AppHost).
#   AUTHSERVICE_LOG      a file with authservice's console output, instead of the container.
#   INVITATION_TOKEN     the invitation token itself, instead of either.
set -euo pipefail

repo_root="$(git rev-parse --show-toplevel)"

export ASPNETCORE_ENVIRONMENT=Development
export Seed__AuthServiceUrl="${AUTHSERVICE_URL:-http://localhost:5080}"
export Seed__BoardsUrl="${BOARDS_URL:-http://localhost:5081}"
export Seed__CanvasUrl="${CANVAS_URL:-http://localhost:5082}"
export Seed__AuthServiceContainer="${AUTHSERVICE_CONTAINER:-miroclone-authservice-1}"
[[ -n "${AUTHSERVICE_LOG:-}" ]] && export Seed__AuthServiceLogFile="$AUTHSERVICE_LOG"
[[ -n "${INVITATION_TOKEN:-}" ]] && export Seed__InvitationToken="$INVITATION_TOKEN"

exec dotnet run --project "$repo_root/MiroClone.Seed/MiroClone.Seed.csproj" -- "$@"
