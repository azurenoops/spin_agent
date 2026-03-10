---
name: setup-teamwork
description: "Fill in all CUSTOMIZE placeholders across Teamwork agent files by analyzing the repository's tech stack, languages, and tooling. Use after installing Teamwork into a new repository."
---

# Setup Teamwork

## Overview

This skill scans the repository to detect the tech stack, languages, build tools, and test frameworks, then fills in all `<!-- CUSTOMIZE -->` placeholders in the agent files under `.github/agents/`.

## Steps

### Step 1: Analyze the Repository

Detect the project's technology stack by examining these files (check each if it exists):

| File | What to Extract |
|------|----------------|
| `package.json` | Languages (TypeScript/JavaScript), package manager (npm/yarn/pnpm), test framework (Jest/Vitest/Mocha), build/lint/test scripts |
| `go.mod` | Go version, module name |
| `pyproject.toml` / `requirements.txt` / `setup.py` | Python version, dependencies, test framework (pytest/unittest) |
| `Cargo.toml` | Rust version, dependencies |
| `Makefile` | Build, test, lint commands |
| `Dockerfile` | Base image, build steps |
| `.github/workflows/` | CI commands, test/lint/build steps |
| `tsconfig.json` | TypeScript configuration |
| `.eslintrc*` / `biome.json` / `.prettierrc` | Lint/format tools |
| `docker-compose.yml` | Database engine, services |
| `prisma/schema.prisma` / `migrations/` | ORM, database engine |

**If the repository is empty or has no detectable config files**, skip auto-detection and go directly to Step 2 to ask the user for their project details.

### Step 2: Build a Summary

**If config files were found:** compile the detected information into a summary and present it to the user for confirmation or corrections.

**If the repository is empty or new:** ask the user directly for their project details:

- What languages and frameworks will you use?
- What package manager? (npm, pnpm, yarn, go mod, pip, cargo, etc.)
- What test framework? (Jest, pytest, go test, etc.)
- What are your build, test, and lint commands?
- Will the project have an API? If so, what framework? (Express, FastAPI, Gin, etc.)
- Will the project use a database? If so, which engine and ORM?

Present the gathered information as a summary:

```
Tech Stack: [detected or user-provided]
Languages: [detected or user-provided]
Package Manager: [detected or user-provided]
Test Framework: [detected or user-provided]
Build Command: [detected or user-provided]
Test Command: [detected or user-provided]
Lint Command: [detected or user-provided]
API Framework: [if applicable]
Database Engine: [if applicable]
ORM/Migration Tool: [if applicable]
```

### Step 3: Confirm with User

Before making changes, show the full summary and ask the user to confirm or correct any values.

**If the user doesn't know their tech stack yet or can't provide details**, stop here. Do not fill in any placeholders with guessed or assumed values. Inform the user they can run `/setup-teamwork` again later when their tech stack is decided.

### Step 4: Fill In Placeholders

For each `.agent.md` file in `.github/agents/`:

1. Read the file
2. Find lines containing `<!-- CUSTOMIZE -->` and bracketed placeholder values like `[e.g., ...]`
3. Replace the bracketed placeholder text with the actual detected values
4. Remove the `<!-- CUSTOMIZE -->` comment (it's no longer needed once filled in)
5. Write the updated file

Only fill in values that were detected or confirmed by the user. Leave unknown values as placeholders.

### Step 5: Verify

List all remaining unfilled placeholders (if any) and inform the user which agent files still need manual input.

## What Not to Do

- 🚫 Never guess values — only use what's detected or confirmed by the user.
- 🚫 Never modify agent instructions, boundaries, or responsibilities — only fill in Project Knowledge placeholders.
- 🚫 Never remove agent files that aren't relevant (e.g., `dba-agent` when there's no database). The user decides which agents to keep.
