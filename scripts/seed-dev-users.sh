#!/usr/bin/env bash
# Seeds two development users in one authservice organization through its HTTP API (WP03), so that a board can be
# shared between them. Safe to run again: it reuses the users, the organization and an existing membership.
#
# Usage: scripts/seed-dev-users.sh [authservice-url]
#   AUTHSERVICE_URL     default http://localhost:8081. Use the authservice address the AppHost assigns.
#   INVITATION_TOKEN    the invitation token. The dev outbox logs it (Development only), so on the first run the script
#                       prints where to find it. Pass it on the second run, or set AUTHSERVICE_LOG to the log file instead.
#   AUTHSERVICE_LOG     file holding authservice's console output, searched for the newest invitation token.
#   SEED_PASSWORD       password of both seed users. Development only; never a real credential.
#   TERMS_VERSION, PRIVACY_VERSION   must match authservice's ConsentVersions (2026-01-01 in its appsettings.json).
set -euo pipefail

base_url="${1:-${AUTHSERVICE_URL:-http://localhost:8081}}"
owner_email="owner@dev.test"
member_email="member@dev.test"
organization_name="Dev Workspace"
# A throwaway development password, shared by the two seed users. gitleaks:allow
password="${SEED_PASSWORD:-Dev-Password-123!}" # gitleaks:allow
terms="${TERMS_VERSION:-2026-01-01}"
privacy="${PRIVACY_VERSION:-2026-01-01}"

require() {
  command -v "$1" >/dev/null || { echo "seed-dev-users: '$1' is required" >&2; exit 1; }
}
require curl
require jq

# Decodes the payload of a JWT (no signature check: this is a local seeding helper, not a trust decision).
claims_of() {
  local payload="${1#*.}"
  payload="${payload%%.*}"
  payload="${payload//-/+}"
  payload="${payload//_//}"
  while (( ${#payload} % 4 )); do payload+="="; done
  printf '%s' "$payload" | base64 -d
}

post_json() {
  local path="$1" body="$2" token="${3:-}"
  local headers=(-H 'Content-Type: application/json')
  [[ -n "$token" ]] && headers+=(-H "Authorization: Bearer $token")
  curl -sS -f "${headers[@]}" -d "$body" "$base_url$path"
}

# Signs in, and registers the account first when it does not exist yet.
token_for() {
  local email="$1" login
  login=$(jq -cn --arg e "$email" --arg p "$password" '{email:$e,password:$p}')
  if token=$(post_json /api/v1/auth/login "$login" 2>/dev/null | jq -er .accessToken); then
    printf '%s' "$token"
    return
  fi
  local register
  register=$(jq -cn --arg e "$email" --arg p "$password" --arg t "$terms" --arg v "$privacy" \
    '{email:$e,password:$p,acceptedTermsVersion:$t,acceptedPrivacyVersion:$v}')
  post_json /api/v1/auth/register "$register" | jq -er .accessToken
}

# Returns the newest invitation token from the dev outbox log, which prints "Invitation token: <token>".
token_from_log() {
  [[ -n "${AUTHSERVICE_LOG:-}" && -f "$AUTHSERVICE_LOG" ]] || return 1
  grep -oE 'Invitation token: [A-Za-z0-9_-]+' "$AUTHSERVICE_LOG" | tail -1 | sed 's/^Invitation token: //'
}

owner_token=$(token_for "$owner_email")
member_token=$(token_for "$member_email")

organization_id=$(curl -sS -f -H "Authorization: Bearer $owner_token" "$base_url/api/v1/organizations" \
  | jq -r --arg n "$organization_name" '[.[] | select(.name == $n)][0].id // empty')
if [[ -z "$organization_id" ]]; then
  organization_id=$(post_json /api/v1/organizations "$(jq -cn --arg n "$organization_name" '{name:$n}')" "$owner_token" | jq -er .id)
fi

member_is_in_organization() {
  claims_of "$member_token" | jq -e --arg o "$organization_id" '(.organization // []) | if type == "array" then index($o) else . == $o end' >/dev/null
}

if ! member_is_in_organization; then
  # A repeat invite answers 400 ("already been sent") and the earlier invitation still works, so that failure is expected.
  post_json "/api/v1/organizations/$organization_id/invite" \
    "$(jq -cn --arg e "$member_email" '{email:$e,role:"Member"}')" "$owner_token" >/dev/null 2>&1 || true

  invitation="${INVITATION_TOKEN:-$(token_from_log || true)}"
  if [[ -z "$invitation" ]]; then
    cat >&2 <<MSG
seed-dev-users: the invitation for $member_email was sent. Its token is written to the authservice log, not returned.
  Find the newest line containing "Invitation token:" in the authservice console (or in the Aspire dashboard logs),
  then run again with it:
    INVITATION_TOKEN=<token> scripts/seed-dev-users.sh $base_url
MSG
    exit 2
  fi

  post_json /api/v1/organizations/invitations/accept "$(jq -cn --arg t "$invitation" '{token:$t}')" "$member_token" >/dev/null
fi

# Sign in again: an organization membership reaches a token only when it is issued (TokenService.BuildClaimsAsync).
owner_token=$(token_for "$owner_email")
member_token=$(token_for "$member_email")
member_is_in_organization || { echo "seed-dev-users: $member_email is not in $organization_id after accepting" >&2; exit 1; }

echo "seeded organization $organization_id"
echo "  $owner_email  user $(claims_of "$owner_token" | jq -r .sub)"
echo "  $member_email user $(claims_of "$member_token" | jq -r .sub)"
