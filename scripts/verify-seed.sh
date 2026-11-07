#!/usr/bin/env bash
# Checks that the demo seed ran on an estate: both demo users sign in over authservice's API, are in the same organization, and each
# sees the three demo boards through the boards service; the first board holds its shapes on the canvas service. Exit 0 means yes.
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
org_of_owner=""
for email in owner@dev.test member@dev.test; do
  token=$(curl -sS -f -H 'Content-Type: application/json' -d "{\"email\":\"$email\",\"password\":\"$password\"}" "$auth/api/v1/auth/login" | jq -er .accessToken)
  org=$(claims_of "$token" | jq -er '.organization | if type == "array" then (if length == 1 then .[0] else error("not exactly one organization") end) else . end')
  [[ -z "$org_of_owner" ]] && org_of_owner="$org"
  [[ "$org" == "$org_of_owner" ]] || { echo "FAIL: $email is in $org, not $org_of_owner" >&2; exit 1; }
  titles=$(curl -sS -f -H "Authorization: Bearer $token" "$boards/api/boards?pageSize=50" | jq -r '[.items[].title | select(startswith("Demo: "))] | sort | join(" | ")')
  echo "$email: organization $org; boards: $titles"
  [[ "$titles" == "Demo: Empty board | Demo: Sprint planning | Demo: Welcome" ]] || { echo "FAIL: $email does not see the three demo boards" >&2; exit 1; }
  if [[ "$email" == owner@dev.test ]]; then
    welcome=$(curl -sS -f -H "Authorization: Bearer $token" "$boards/api/boards?pageSize=50" | jq -r '.items[] | select(.title == "Demo: Welcome") | .id')
    layers=$(curl -sS -f -H "Authorization: Bearer $token" "$canvas/api/canvas/$welcome" | jq '.layerIds | length')
    echo "Demo: Welcome $welcome holds $layers shapes"
    (( layers >= 4 )) || { echo "FAIL: Demo: Welcome holds $layers shapes, expected at least 4" >&2; exit 1; }
  fi
done
echo "seed verified"
