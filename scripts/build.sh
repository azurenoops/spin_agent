#!/usr/bin/env bash
set -euo pipefail

# scripts/build.sh — Build the project
# =======================================
# Run via: make build
#
# Configure this script for your project's build tooling.

echo "==> Building..."
echo ""
echo "  ⓘ No build configured yet."
echo "    Edit scripts/build.sh to enable builds for your project."

# ------------------------------------------------------------------
# Uncomment / adapt the build command(s) that match your stack:
# ------------------------------------------------------------------

# --- JavaScript / TypeScript ---
# npm run build
# npx tsc --noEmit

# --- Python ---
# python -m build

# --- Go ---
# go build -o bin/ ./...

# --- Rust ---
# cargo build --release

# --- Docker ---
# docker build -t myapp .

exit 0
