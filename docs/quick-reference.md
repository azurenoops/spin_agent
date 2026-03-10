# Teamwork Quick Reference

## Agents (`.github/agents/`)

Select an agent from Copilot's dropdown or mention it with `@agent-name`.

| Agent | What It Does | Tools |
|-------|-------------|-------|
| `@planner` | Breaks goals into tasks, creates issues | read, search, edit |
| `@architect` | Designs systems, writes ADRs | read, search, edit |
| `@coder` | Implements tasks, writes code and tests | all |
| `@tester` | Writes adversarial tests | read, search, edit, execute |
| `@reviewer` | Reviews PRs for quality | read, search |
| `@security-auditor` | Audits for vulnerabilities | read, search |
| `@documenter` | Keeps documentation current | read, search, edit |
| `@orchestrator` | Coordinates workflows, dispatches roles | read, search, edit |
| `@triager` | Triages and classifies issues | read, search |
| `@devops` | CI/CD, infrastructure, deployments | read, search, edit, execute |
| `@dependency-manager` | Updates and audits dependencies | read, search, edit, execute |
| `@refactorer` | Restructures code, preserves behavior | read, search, edit, execute |
| `@lint-agent` | Fixes code style and formatting | read, search, edit, execute |
| `@api-agent` | Designs and builds API endpoints | all |
| `@dba-agent` | Database schema, migrations, queries | read, search, edit, execute |

## Skills (`.github/skills/`)

Invoke a workflow with `/skill-name` or let Copilot auto-detect the right one.

| Skill | When to Use |
|-------|------------|
| `/feature-workflow` | New feature from goal to merged code |
| `/bugfix-workflow` | Diagnose and fix a bug |
| `/refactor-workflow` | Restructure code without behavior change |
| `/hotfix-workflow` | Urgent production fix |
| `/security-response` | Respond to a security vulnerability |
| `/dependency-update` | Update third-party dependencies |
| `/documentation-workflow` | Standalone documentation update |
| `/spike-workflow` | Research or technical investigation |
| `/release-workflow` | Prepare and publish a release |
| `/rollback-workflow` | Roll back a failed deployment |
| `/setup-teamwork` | Fill in all CUSTOMIZE placeholders by analyzing the repo |

## Key Files

| File | Purpose |
|------|---------|
| `MEMORY.md` | Project context — read at session start |
| `.github/copilot-instructions.md` | Repo-wide Copilot instructions |
| `.github/instructions/*.instructions.md` | Path-specific auto-loaded guidelines |
| `.teamwork/config.yaml` | Model tier mappings and settings |
| `.teamwork/state/` | Active workflow state files |
| `.teamwork/handoffs/` | Handoff artifacts between roles |
| `.teamwork/memory/` | Structured project learnings |
| `docs/conventions.md` | Coding standards and conventions |
| `docs/architecture.md` | Architecture Decision Records |
| `docs/glossary.md` | Terminology definitions |

## Conventional Commits

```
feat(scope): add new feature
fix(scope): fix a bug
refactor(scope): restructure without behavior change
docs(scope): documentation only
test(scope): add or update tests
chore(scope): maintenance tasks
```

## Customization

Each agent file has a `## Project Knowledge` section with `<!-- CUSTOMIZE -->` placeholders.
Fill these in with your project's tech stack, commands, and conventions.

## Model Tiers

| Tier | Agents | When to Use |
|------|--------|-------------|
| Premium | planner, architect, coder, security-auditor, refactorer | Complex reasoning tasks |
| Standard | tester, reviewer, devops, dependency-manager, api-agent, dba-agent | Routine development tasks |
| Fast | documenter, orchestrator, triager, lint-agent | Simple, repetitive tasks |
