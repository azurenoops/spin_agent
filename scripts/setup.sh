#!/usr/bin/env bash
set -euo pipefail

# scripts/setup.sh — One-time dev environment setup
# ===================================================
# Run via: make setup
#
# Checks for required tools, installs git hooks, and prints a summary.

echo "==> Running dev environment setup..."
echo ""

# ------------------------------------------------------------------
# 1. Check for required tools
# ------------------------------------------------------------------
MISSING=()

for tool in git make; do
  if command -v "$tool" &>/dev/null; then
    echo "  ✓ $tool ($(command -v "$tool"))"
  else
    echo "  ✗ $tool — NOT FOUND"
    MISSING+=("$tool")
  fi
done

if [ ${#MISSING[@]} -ne 0 ]; then
  echo ""
  echo "ERROR: Missing required tools: ${MISSING[*]}"
  echo "Please install them before continuing."
  exit 1
fi

# ------------------------------------------------------------------
# 2. Install pre-commit hooks (if pre-commit is available)
# ------------------------------------------------------------------
echo ""
if command -v pre-commit &>/dev/null; then
  echo "==> Installing pre-commit hooks..."
  pre-commit install
  echo "  ✓ pre-commit hooks installed"
else
  echo "  ⓘ pre-commit not found — skipping hook installation."
  echo "    Install it with: pip install pre-commit  (or: brew install pre-commit)"
fi

# ------------------------------------------------------------------
# 3. TODO: Add project-specific setup steps
# ------------------------------------------------------------------
# Examples:
#   npm install          # Node.js dependencies
#   pip install -r requirements.txt  # Python dependencies
#   go mod download      # Go module dependencies
#   cp .env.example .env # Environment file

# ------------------------------------------------------------------
# 4. Summary
# ------------------------------------------------------------------
echo ""
echo "==> Setup complete."
echo "    Run 'make help' to see available commands."
