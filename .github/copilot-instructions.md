# Copilot Instructions — Teamwork

Teamwork is an agent-native development template that structures AI-human collaboration through defined roles (Custom Agents), workflows (Skills), and conventions.

## Before You Start

0. **Read project context.** Start every session by reading `MEMORY.md` for current project state, recent decisions, and active context.

1. **Select your agent.** Choose the appropriate Custom Agent from `.github/agents/` — each agent has a defined persona, tool restrictions, and boundaries:

   **Core agents:**
   - `@planner` — Break goals into tasks. Never write code.
   - `@architect` — Design systems, write ADRs. Never write code.
   - `@coder` — Implement tasks, write tests, open PRs.
   - `@tester` — Write adversarial tests. Never modify production code.
   - `@reviewer` — Review PRs for quality. Never modify code.
   - `@security-auditor` — Audit for vulnerabilities. Never modify code.
   - `@documenter` — Keep documentation accurate and current.
   - `@orchestrator` — Coordinate workflows, dispatch roles. Never write code.

   **Extended agents:**
   - `@triager` — Triage and classify incoming issues.
   - `@devops` — CI/CD pipelines, infrastructure, deployments.
   - `@dependency-manager` — Update and audit third-party dependencies.
   - `@refactorer` — Restructure code without changing behavior.
   - `@lint-agent` — Fix code style and formatting issues.
   - `@api-agent` — Design and build API endpoints.
   - `@dba-agent` — Database schema, migrations, query optimization.

   **If no agent is specified:** Use `docs/role-selector.md` to determine the right one. Quick defaults: implementation → Coder, planning → Planner, code review → Reviewer, multi-role → Planner (break it down first).

2. **Read the conventions.** Review `docs/conventions.md` for coding standards, branch naming, commit format, and PR requirements.
3. **Understand the architecture.** Check `docs/architecture.md` for prior design decisions (ADRs) before proposing structural changes.
4. **Learn the vocabulary.** Use terminology as defined in `docs/glossary.md`.
5. **Invoke a workflow skill.** For multi-step tasks, use `/skill-name` or check `.github/skills/` for structured workflows:
   - `/feature-workflow` — Adding new functionality
   - `/bugfix-workflow` — Diagnosing and fixing bugs
   - `/refactor-workflow` — Restructuring existing code
   - `/hotfix-workflow` — Urgent production fixes
   - `/security-response` — Responding to security vulnerabilities
   - `/dependency-update` — Updating third-party dependencies
   - `/documentation-workflow` — Standalone documentation updates
   - `/spike-workflow` — Research or technical investigation
   - `/release-workflow` — Preparing and publishing releases
   - `/rollback-workflow` — Rolling back failed deployments
   - `/setup-teamwork` — Fill in all CUSTOMIZE placeholders by analyzing the repo

## Key Rules

- **Minimal changes.** Change only what is necessary. Do not refactor unrelated code.
- **Test before submitting.** Run all relevant tests and verify they pass before opening a PR.
- **Conventional commits.** Format: `type(scope): description` (e.g., `feat(auth): add token refresh`).
- **One task per PR.** Keep pull requests focused on a single task or change.
- **Respect agent boundaries.** Each agent's `.agent.md` file defines ✅ Always / ⚠️ Ask first / 🚫 Never rules. Follow them.
- **Keep scope small.** Target ~300 lines changed and ~10 files maximum per task.

## When to Escalate

Stop and ask the human when:

- Requirements are ambiguous or contradictory
- A change would affect architecture or public APIs
- Tests fail and the fix is unclear
- You are unsure which agent or workflow applies
- Security concerns arise that need human judgment
- The task crosses agent boundaries (e.g., a coder being asked to make architectural decisions)

## Project Structure

```
MEMORY.md                       — Project context (read at session start)
.github/
  agents/                       — Custom Agents (select from Copilot's dropdown)
  skills/                       — Skills (invoke via /skill-name)
  instructions/                 — Path-specific instructions (auto-loaded)
  copilot-instructions.md       — This file (repo-wide guidance)
.teamwork/
  config.yaml                   — Orchestration settings
  state/                        — Workflow state files
  handoffs/                     — Handoff artifacts between roles
  memory/                       — Structured project memory
  metrics/                      — Agent activity logs (gitignored)
docs/
  conventions.md                — Coding standards and project conventions
  architecture.md               — Architecture Decision Records (ADRs)
  protocols.md                  — Coordination protocol specification
  glossary.md                   — Terminology definitions
  role-selector.md              — Guide for choosing the right agent
  conflict-resolution.md        — Resolving conflicting instructions
  secrets-policy.md             — Rules for handling secrets
  cost-policy.md                — Guidelines for managing AI agent costs
```

## Model Selection

After selecting your agent, check its **Model Requirements** section for the recommended tier (premium, standard, or fast). Then check `.teamwork/config.yaml` for the project's model mappings.

- **If the agent needs a higher tier than your current model:** Delegate the work to a sub-agent using the recommended model, or inform the user that this task would benefit from a higher-tier model.
- **If the agent needs a lower tier than your current model:** Proceed normally.
- **If you can spawn sub-agents:** Use the tier system to run each agent at the right model level.

See `docs/role-selector.md` for the full tier-to-agent mapping table.

## Protocol Integration

When working in a workflow, integrate with the `.teamwork/` protocol system:

### At Session Start
1. Check `.teamwork/state/` for active workflows relevant to your task.
2. If a workflow exists, read the state file to find your step and role.
3. Read the previous handoff artifact from `.teamwork/handoffs/<workflow-id>/` for context.
4. Check `.teamwork/memory/` for patterns and decisions relevant to your domain.

### During Work
- Follow your agent file's boundaries and quality bar.
- Reference the handoff from the previous step for context and decisions.

### At Session End
1. Write a handoff artifact to `.teamwork/handoffs/<workflow-id>/<step>-<role>.md` per `docs/protocols.md`.
2. Update the workflow state file in `.teamwork/state/<workflow-id>.yaml`.
3. If you learned something broadly applicable, add it to `.teamwork/memory/`.

### If No Workflow Exists
If the task is ad-hoc, skip protocol integration. Just follow your agent file and conventions.

## Customization Placeholders

Agent files in `.github/agents/` contain `<!-- CUSTOMIZE -->` placeholders in their `## Project Knowledge` sections. These indicate project-specific details (tech stack, languages, build commands, etc.) that need to be filled in.

**Whenever you work in this repository**, check if any agent files relevant to your current task still have unfilled placeholders. If they do:

1. Analyze the repository to detect the correct values — check `package.json`, `go.mod`, `Makefile`, `pyproject.toml`, `Cargo.toml`, `docker-compose.yml`, `.github/workflows/`, and similar config files.
2. Replace bracketed placeholder text like `[e.g., React 18, TypeScript]` with the actual detected values.
3. Remove the `<!-- CUSTOMIZE: ... -->` comment once the values are filled in.
4. If you cannot confidently determine a value, leave it as-is.

This is a background task — do it alongside your primary work without asking for permission. Filling in accurate project knowledge makes every agent more effective.

To fill in all placeholders at once, use the `/setup-teamwork` skill.

## Migrated Content

After a `teamwork update`, files may contain `<!-- MIGRATED FROM ... -->` sections at the bottom. These contain user customizations from deprecated file locations that were automatically appended during the update.

**Whenever you encounter a `<!-- MIGRATED FROM ... -->` section**, stop and merge it before continuing your primary task:

1. Read both the main file content and the migrated section.
2. Identify what the user customized — look for additions, modifications, or project-specific details that differ from the framework defaults.
3. Integrate the user's customizations into the appropriate sections of the main file. For agent files, user customizations typically belong in Project Knowledge, Responsibilities, or Boundaries.
4. Remove the entire `<!-- MIGRATED FROM ... -->` block and all content below it once merged.
5. Do not discard user customizations. If you are unsure where something belongs, keep it and add a `<!-- TODO: review placement -->` comment.

This is a high-priority background task — migrated content means the user had intentional customizations that should not be lost.

## Tips

- When starting work, state which agent you are performing as and confirm you have read the agent file.
- Prefer reading existing code and tests before writing new code.
- When in doubt, check the glossary — terms like "handoff," "escalation," and "quality bar" have specific meanings.
- One real code snippet showing your style beats three paragraphs describing it.
