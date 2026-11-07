#!/usr/bin/env bash
# Checks that the demo seed ran on an estate: the four demo users sign in over authservice's API with their roles (owner, admin, member of the Dev
# Workspace; owner of the Other Workspace), each sees only its workspace's demo boards, the first board holds its shapes, and each workspace gets 404 for
# the other's board. Exit 0 means yes.
# Used by the CI jobs that start the estate (compose-e2e, apphost-e2e) and by a developer after the first start.
#
# Usage: scripts/verify-seed.sh   (AUTHSERVICE_URL, BOARDS_URL, CANVAS_URL as in seed-dev-users.sh)
set -euo pipefail

auth="${AUTHSERVICE_URL:-http://localhost:5080}"
boards="${BOARDS_URL:-http://localhost:5081}"
canvas="${CANVAS_URL:-http://localhost:5082}"
password="Dev-Password-123!" # gitleaks:allow

claims_of() {
  local payload="${1#*.}"; payload="${payload%%.*}"; payload="${payload//-/+}"; payload="${payload//_//}"
  while (( ${#payload} % 4 )); do payload+="="; done
  printf '%s' "$payload" | base64 -d
}

echo "authservice $auth: $(curl -sS -m 10 "$auth/health/ready")"

# user, expected role, workspace (dev = Dev Workspace, other = Other Workspace), boards that user must see
declare -A org_of_workspace=()
check_user() {
  local email="$1" role="$2" workspace="$3" expected_titles="$4"
  local token org actual_role titles
  token=$(curl -sS -f -H 'Content-Type: application/json' -d "{\"email\":\"$email\",\"password\":\"$password\"}" "$auth/api/v1/auth/login" | jq -er .accessToken)
  org=$(claims_of "$token" | jq -er '.organization | if type == "array" then (if length == 1 then .[0] else error("not exactly one organization") end) else . end')
  actual_role=$(claims_of "$token" | jq -er --arg key "organization:$org:role" '.[$key] | if type == "array" then .[0] else . end')
  [[ "$actual_role" == "$role" ]] || { echo "FAIL: $email has role $actual_role, expected $role" >&2; exit 1; }
  if [[ -z "${org_of_workspace[$workspace]:-}" ]]; then org_of_workspace[$workspace]="$org"; fi
  [[ "$org" == "${org_of_workspace[$workspace]}" ]] || { echo "FAIL: $email is in $org, not ${org_of_workspace[$workspace]}" >&2; exit 1; }
  titles=$(curl -sS -f -H "Authorization: Bearer $token" "$boards/api/boards?pageSize=50" | jq -r '[.items[].title | select(startswith("Demo: "))] | sort | join(" | ")')
  echo "$email: $role of $workspace, organization $org; demo boards: $titles"
  [[ "$titles" == "$expected_titles" ]] || { echo "FAIL: $email sees '$titles', expected '$expected_titles'" >&2; exit 1; }
  LAST_TOKEN="$token"; LAST_ORG="$org"
}

dev_boards="Demo: Empty board | Demo: Sprint planning | Demo: Welcome"
check_user owner@dev.test Owner dev "$dev_boards"
owner_token="$LAST_TOKEN"
check_user admin@dev.test Admin dev "$dev_boards"
check_user member@dev.test Member dev "$dev_boards"
check_user other@dev.test Owner other "Demo: Other workspace board"
[[ "${org_of_workspace[dev]}" != "${org_of_workspace[other]}" ]] || { echo "FAIL: both workspaces are the same organization" >&2; exit 1; }

welcome=$(curl -sS -f -H "Authorization: Bearer $owner_token" "$boards/api/boards?pageSize=50" | jq -r '.items[] | select(.title == "Demo: Welcome") | .id')
layers=$(curl -sS -f -H "Authorization: Bearer $owner_token" "$canvas/api/canvas/$welcome" | jq '.layerIds | length')
echo "Demo: Welcome $welcome holds $layers shapes"
(( layers >= 4 )) || { echo "FAIL: Demo: Welcome holds $layers shapes, expected at least 4" >&2; exit 1; }

# Isolation: another workspace's board answers 404 (never 403), both ways.
other_board=$(curl -sS -f -H "Authorization: Bearer $LAST_TOKEN" "$boards/api/boards?pageSize=50" | jq -r '.items[] | select(.title == "Demo: Other workspace board") | .id')
code=$(curl -sS -o /dev/null -w '%{http_code}' -H "Authorization: Bearer $owner_token" "$canvas/api/canvas/$other_board")
[[ "$code" == "404" ]] || { echo "FAIL: the Dev Workspace owner got HTTP $code for the Other Workspace board, expected 404" >&2; exit 1; }
code=$(curl -sS -o /dev/null -w '%{http_code}' -H "Authorization: Bearer $LAST_TOKEN" "$canvas/api/canvas/$welcome")
[[ "$code" == "404" ]] || { echo "FAIL: the Other Workspace owner got HTTP $code for a Dev Workspace board, expected 404" >&2; exit 1; }
echo "isolation: each workspace gets 404 for the other's board"
echo "seed verified"
