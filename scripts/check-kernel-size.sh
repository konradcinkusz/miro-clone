#!/usr/bin/env bash
# Kernel ceiling (P2): MiroClone.ServiceDefaults stays within its line budget. A kernel that grows past it has
# become a shared domain, so the code moves to the service that owns it (ADR 0005).
set -euo pipefail

readonly limit=800

repo_root="$(git rev-parse --show-toplevel)"
cd "$repo_root"

lines="$(find MiroClone.ServiceDefaults -name '*.cs' -not -path '*/obj/*' -not -path '*/bin/*' -print0 \
  | xargs -0 cat | wc -l)"
echo "MiroClone.ServiceDefaults: ${lines} lines (ceiling ${limit})"

if [ "${lines}" -gt "${limit}" ]; then
  echo "Kernel size guard failed: move domain or service code out of MiroClone.ServiceDefaults (P2, ADR 0005)." >&2
  exit 1
fi
