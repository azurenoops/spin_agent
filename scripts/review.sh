#!/usr/bin/env bash
set -euo pipefail

# scripts/review.sh — Invoke the review agent
# ==============================================
# Run via: make review REF="42"        (PR number)
#      or: make review REF="my-branch" (branch name)
#
# This script feeds a diff to an AI review agent, which produces
# structured review feedback.
#
# What this script should do:
#   1. Read the reviewer role definition (e.g. agents/reviewer.md)
#   2. Obtain the diff (from a PR number, branch, or staged changes)
#   3. Invoke the AI agent with the role + diff
#   4. Output the review to stdout or post it as PR comments

REF="${1:-}"

if [ -z "$REF" ]; then
  echo "Usage: make review REF=\"pr-number-or-branch\""
  echo ""
  echo "  This invokes an AI review agent to review code changes."
  echo ""
  echo "  ⓘ No review agent configured yet."
  echo "    Edit scripts/review.sh to wire up your preferred AI tool."
  exit 1
fi

echo "==> Reviewing: $REF"
echo ""
echo "  ⓘ No review agent configured yet."
echo "    Edit scripts/review.sh to wire up your preferred AI tool."
echo ""

# ------------------------------------------------------------------
# Uncomment / adapt for your preferred AI tool:
# ------------------------------------------------------------------

# --- Get the diff ---
# If REF is a number, treat it as a PR:
#   DIFF=$(gh pr diff "$REF")
# Otherwise treat it as a branch:
#   DIFF=$(git diff main..."$REF")

# --- Claude CLI ---
# ROLE=$(cat agents/roles/reviewer.md)
# echo "$DIFF" | claude -p "$ROLE\n\nReview the following diff:\n$(cat -)"

# --- GitHub Copilot CLI ---
# gh copilot explain "Review this diff: $(git diff main..."$REF" | head -500)"

# --- OpenAI CLI ---
# DIFF=$(gh pr diff "$REF" 2>/dev/null || git diff main..."$REF")
# openai api chat.completions.create \
#   -m gpt-4 \
#   -g system "$(cat agents/roles/reviewer.md)" \
#   -g user "Review this diff:\n$DIFF"

echo "    See agents/README.md for setup instructions."
