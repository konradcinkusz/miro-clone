#!/usr/bin/env bash
# Starts the whole estate in containers with docker compose (compose.yaml). Generates the authservice signing key once, outside
# git (.local/ is ignored), then builds the three service images and waits until every container reports healthy.
# Extra arguments go to `docker compose up`. Stop with: docker compose down -v
set -euo pipefail

repo_root="$(git rev-parse --show-toplevel)"
cd "$repo_root"

key_file=".local/authservice-jwt-dev.pem"
if [[ ! -s "$key_file" ]]; then
  mkdir -p .local
  openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out "$key_file"
  chmod 600 "$key_file"
fi
export AUTHSERVICE_JWT_PRIVATE_KEY="${AUTHSERVICE_JWT_PRIVATE_KEY:-$(cat "$key_file")}"
# Also keep it in the gitignored .env, so that later `docker compose ps|logs|down` interpolate compose.yaml without the variable exported.
printf 'AUTHSERVICE_JWT_PRIVATE_KEY="%s"\n' "$AUTHSERVICE_JWT_PRIVATE_KEY" > .env
chmod 600 .env

docker compose up --build --wait --wait-timeout 600 "$@"
docker compose ps
echo
echo "Web:         http://localhost:8080"
echo "authservice: http://localhost:5080"
