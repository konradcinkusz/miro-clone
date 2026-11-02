#!/usr/bin/env bash
# The fallback when the Aspire AppHost cannot be used: no AppHost, the three .NET services as plain processes, and PostgreSQL and
# authservice as Docker containers. The wiring is the AppHost's (MiroClone.AppHost/EstateExtensions.cs), the log of every service
# goes to .local/logs/<name>.log, and Ctrl+C stops the processes and removes the containers.
#   Web http://localhost:8080   authservice http://localhost:5080   boards :5081   canvas :5082   postgres 127.0.0.1:5432
set -euo pipefail

repo_root="$(git rev-parse --show-toplevel)"
cd "$repo_root"
docker info > /dev/null 2>&1 || { echo "Docker is not running. Start Docker, then run this again." >&2; exit 1; }

mkdir -p .local/logs
key_file=".local/authservice-jwt-dev.pem"
[[ -s "$key_file" ]] || { openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out "$key_file"; chmod 600 "$key_file"; }

pg_password="miroclone-local" # gitleaks:allow
network="miroclone-local"
pids=()
cleanup() {
  trap - EXIT INT TERM
  for pid in "${pids[@]:-}"; do [[ -n "$pid" ]] && kill "$pid" 2> /dev/null || true; done
  docker rm -f miroclone-local-postgres miroclone-local-authservice > /dev/null 2>&1 || true
  docker network rm "$network" > /dev/null 2>&1 || true
}
trap cleanup EXIT INT TERM
cleanup_start() { docker rm -f miroclone-local-postgres miroclone-local-authservice > /dev/null 2>&1 || true; docker network rm "$network" > /dev/null 2>&1 || true; }
cleanup_start

wait_http() { # url, name, seconds
  for ((i = 0; i < $3; i++)); do
    curl -fsS -o /dev/null "$1" 2> /dev/null && return 0
    sleep 2
  done
  echo "$2 did not become healthy at $1 (see .local/logs and 'docker logs')." >&2
  return 1
}

docker network create "$network" > /dev/null
docker run -d --name miroclone-local-postgres --network "$network" --network-alias postgres -p 127.0.0.1:5432:5432 \
  -e POSTGRES_PASSWORD="$pg_password" -v "$repo_root/docker/initdb.sql:/docker-entrypoint-initdb.d/10-databases.sql:ro" postgres:18.3 > /dev/null
until docker exec miroclone-local-postgres pg_isready -U postgres -d authservice > /dev/null 2>&1; do sleep 1; done

docker run -d --name miroclone-local-authservice --network "$network" -p 127.0.0.1:5080:8080 \
  -e ASPNETCORE_ENVIRONMENT=Development \
  -e "ConnectionStrings__DefaultConnection=Host=postgres;Port=5432;Username=postgres;Password=$pg_password;Database=authservice" \
  -e Jwt__Algorithm=RS256 -e "Jwt__PrivateKeyPem=$(cat "$key_file")" -e Jwt__Issuer=MiroClone -e Jwt__Audience=miro-clone \
  -e Jwt__PublicBaseUrl=http://localhost:5080 ghcr.io/konradcinkusz/authservice:v0.3.4 > /dev/null
wait_http http://localhost:5080/health/ready authservice 60

export ASPNETCORE_ENVIRONMENT=Development Auth__Authority=http://localhost:5080 Auth__Issuer=MiroClone Auth__Audience=miro-clone Auth__RequireHttpsMetadata=false
conn="Host=127.0.0.1;Port=5432;Username=postgres;Password=$pg_password;Database="

start() { # name, project, port, extra env...
  local name="$1" project="$2" port="$3"; shift 3
  env "ASPNETCORE_URLS=http://127.0.0.1:$port" "$@" dotnet run --project "$project" --no-launch-profile > ".local/logs/$name.log" 2>&1 &
  pids+=("$!")
  wait_http "http://127.0.0.1:$port/health" "$name" 90
}
start boards-service MiroClone.BoardsService 5081 "ConnectionStrings__boards=${conn}boards" Canvas__BaseUrl=http://127.0.0.1:5082/
start canvas-service MiroClone.CanvasService 5082 "ConnectionStrings__canvas=${conn}canvas" Boards__BaseAddress=http://127.0.0.1:5081/
start web MiroClone.Web 8080 services__boards-service__http__0=http://127.0.0.1:5081 services__canvas-service__http__0=http://127.0.0.1:5082

# The demo data (ADR 0050), unless SEED_ENABLED=false. The member's invitation token is read from the authservice container's log.
if [[ "${SEED_ENABLED:-true}" != "false" ]]; then
  AUTHSERVICE_CONTAINER=miroclone-local-authservice scripts/seed-dev-users.sh
fi

echo "Estate is up: Web http://localhost:8080  authservice http://localhost:5080. Ctrl+C stops it."
wait
