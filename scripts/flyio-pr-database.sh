#!/usr/bin/env bash
# Creates or drops one preview environment's databases and login role on the shared dev PostgreSQL (ADR 0019).
#   create <pr-number> <postgres-app>    reads PR_DB_PASSWORD from the environment
#   drop   <pr-number> <postgres-app>
# The databases are private (6PN only), so the SQL runs on the postgres machine through flyctl ssh, base64-encoded so the
# password never has to be quoted for the remote shell. Every statement is idempotent: a re-run after a partial failure converges.
set -euo pipefail

action="${1:?usage: $0 create|drop <pr-number> <postgres-app>}"
pr="${2:?usage: $0 create|drop <pr-number> <postgres-app>}"
app="${3:?usage: $0 create|drop <pr-number> <postgres-app>}"

case "$pr" in
  '' | *[!0-9]*)
    echo "PR number must be digits: $pr" >&2
    exit 2
    ;;
esac

role="pr_${pr}"
databases=("boards_pr_${pr}" "canvas_pr_${pr}" "authservice_pr_${pr}")

case "$action" in
  create)
    : "${PR_DB_PASSWORD:?PR_DB_PASSWORD must be set}"
    sql="DO \$\$ BEGIN
  IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = '${role}') THEN
    CREATE ROLE ${role} LOGIN PASSWORD '${PR_DB_PASSWORD}';
  ELSE
    ALTER ROLE ${role} LOGIN PASSWORD '${PR_DB_PASSWORD}';
  END IF;
END \$\$;
"
    for database in "${databases[@]}"; do
      sql+="SELECT 'CREATE DATABASE ${database} OWNER ${role}' WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = '${database}') \\gexec
REVOKE CONNECT ON DATABASE ${database} FROM PUBLIC;
"
    done
    ;;
  drop)
    sql=""
    for database in "${databases[@]}"; do
      sql+="DROP DATABASE IF EXISTS ${database} WITH (FORCE);
"
    done
    sql+="DROP ROLE IF EXISTS ${role};
"
    ;;
  *)
    echo "unknown action: $action (expected create or drop)" >&2
    exit 2
    ;;
esac

encoded=$(printf '%s' "$sql" | base64 | tr -d '\n')
flyctl ssh console --app "$app" --command "sh -c 'echo ${encoded} | base64 -d | psql -v ON_ERROR_STOP=1 -U miroclone -d postgres'"
echo "preview database ${action}: ${role} on ${app} (${databases[*]})"
