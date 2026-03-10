#!/usr/bin/env bash
set -euo pipefail

# scripts/lint.sh — Run project linters
# =======================================
# Run via: make lint
#
# Configure this script for your project's language and tooling.

echo "==> Linting..."
echo ""
echo "  ⓘ No linters configured yet."
echo "    Edit scripts/lint.sh to enable linting for your project."

# ------------------------------------------------------------------
# Uncomment / adapt the linter(s) that match your stack:
# ------------------------------------------------------------------

# --- JavaScript / TypeScript ---
# npx eslint . --max-warnings=0

# --- Python ---
# ruff check .
# pylint src/

# --- Go ---
# golangci-lint run ./...

# --- Shell ---
# shellcheck scripts/*.sh

# --- General ---
# pre-commit run --all-files

exit 0
