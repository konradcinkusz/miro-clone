#!/usr/bin/env bash
# Installs the repository's git hooks (.githooks/) for this clone. Safe to run more than once.
set -euo pipefail

repo_root="$(git rev-parse --show-toplevel)"
cd "$repo_root"

chmod +x .githooks/pre-commit
git config core.hooksPath .githooks
echo "Git hooks installed: core.hooksPath=.githooks"

if ! command -v gitleaks >/dev/null 2>&1; then
  echo "Note: gitleaks is not installed. The pre-commit hook will refuse commits until it is."
  echo "Install: https://github.com/gitleaks/gitleaks#installing"
fi
