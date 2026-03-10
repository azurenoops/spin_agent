#!/usr/bin/env bash
set -euo pipefail

# scripts/plan.sh — Invoke the planning agent
# ==============================================
# Run via: make plan GOAL="add user authentication"
#
# This script feeds a goal/description to an AI planning agent,
# which produces a structured plan the team can review and execute.
#
# What this script should do:
#   1. Read the planner role definition (e.g. agents/planner.md)
#   2. Gather project context (repo structure, recent changes, open issues)
#   3. Invoke the AI agent with the role + context + goal
#   4. Write the resulting plan to a known location (e.g. docs/plans/)

GOAL="${1:-}"

if [ -z "$GOAL" ]; then
  echo "Usage: make plan GOAL=\"description of what you want to accomplish\""
  echo ""
  echo "  This invokes an AI planning agent to produce a structured plan."
  echo ""
  echo "  ⓘ No planning agent configured yet."
  echo "    Edit scripts/plan.sh to wire up your preferred AI tool."
  exit 1
fi

echo "==> Planning: $GOAL"
echo ""
echo "  ⓘ No planning agent configured yet."
echo "    Edit scripts/plan.sh to wire up your preferred AI tool."
echo ""

# ------------------------------------------------------------------
# Uncomment / adapt for your preferred AI tool:
# ------------------------------------------------------------------

# --- Claude CLI ---
# ROLE=$(cat agents/roles/planner.md)
# CONTEXT=$(git --no-pager log --oneline -10 && echo "---" && find . -type f -not -path './.git/*' | head -50)
# claude -p "$ROLE\n\nProject context:\n$CONTEXT\n\nGoal: $GOAL"

# --- GitHub Copilot CLI ---
# gh copilot explain "Given the project in $(pwd), create a plan for: $GOAL"

# --- OpenAI CLI ---
# openai api chat.completions.create \
#   -m gpt-4 \
#   -g system "$(cat agents/roles/planner.md)" \
#   -g user "Goal: $GOAL"

echo "    See agents/README.md for setup instructions."
