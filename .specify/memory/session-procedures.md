# Session Procedures

Procedures the AI agent MUST execute verbatim when the User issues the trigger
phrases below. These exist so every session starts from a known state and ends with
a clean paper trail.

---

## Trigger: "Start up"

When the User says **"Start up"** (case-insensitive), execute the following
in order. Do NOT skip steps. If a step fails, stop and report.

### 1. Establish ground truth

1. Read [.specify/memory/constitution.md](../../.specify/memory/constitution.md) and
   note the current version (top of file: `**Version**: X.Y.Z`).
2. Read [AGENTS.md](../../AGENTS.md) and
   [.github/copilot-instructions.md](../../.github/copilot-instructions.md).
3. Read the current branch name: `git rev-parse --abbrev-ref HEAD`.
4. If on a feature branch (`NNN-feature-name`), read:
   - `specs/NNN-*/spec.md`
   - `specs/NNN-*/plan.md` (if present)
   - `specs/NNN-*/tasks.md` (if present)

### 2. Check repo health

Run these in parallel (they are read-only):

```bash
git status --short
git log --oneline -5
```

If `git status` shows uncommitted changes, list them and ask the User whether to
proceed before any edits.

### 3. Check active terminals & background work

- List active terminals; identify any long-running processes (Docker, MCP server,
  test watchers).
- If a previous session left a `docker compose` stack running, surface the
  container list (`docker ps --filter name=ato-copilot`).

### 4. Surface session focus

Ask the User one question only:

> "Ready. Branch is `<branch>`. Constitution v<version>. <N> uncommitted files.
> What's the goal for this session?"

### 5. Memory check

- View `/memories/session/` (if present) for in-progress notes from the prior
  session.
- View `/memories/repo/` for repository-scoped facts.
- Do NOT auto-create memory files unless the User asks.

---

## Trigger: "Shutdown"

When the User says **"Shutdown"** (case-insensitive), execute the following in
order.

### 1. Capture session outcomes

Summarize, in this exact structure:

- **Files changed** (paths only, with the relative line count if material)
- **Tests added / changed** (count + filenames)
- **Tasks completed** (cite `tasks.md` IDs if applicable)
- **Open questions** carried into next session
- **Blocked items** with the specific blocker

### 2. Update spec-kit artifacts

For each completed task:

- Mark the task `[X]` in `specs/NNN-*/tasks.md`.
- If a User Story was completed end-to-end, update the GitHub issue checklist
  (per Constitution § DevOps GitHub Issue Discipline — but DO NOT push unless
  the User has approved the change).

### 3. Verify build & tests still pass

Run only the suites relevant to the day's changes; do not block shutdown on
unrelated flakes:

```bash
dotnet build Ato.Copilot.sln
dotnet test  Ato.Copilot.sln --filter "FullyQualifiedName~<area>"
```

If any frontend / extension was touched:

```bash
cd extensions/vscode              && npm run compile
cd extensions/m365                && npm run build
cd src/Ato.Copilot.Dashboard      && npm run typecheck
```

### 4. Persist session memory

Update or create one note under `/memories/session/<topic>.md` capturing:

- What was attempted
- What worked / what didn't
- Concrete next step

Keep it concise. This is the breadcrumb the next session reads first.

### 5. Confirm push posture

State explicitly:

- Number of local commits ahead of `origin/<branch>`.
- Whether a push is queued or pending User approval.
- Per Verification Protocol rule 9: **never push without explicit User
  approval**.

### 6. Stop background processes (only if the User asks)

- Do NOT auto-stop Docker stacks or watchers. Ask first.
- If approved: `docker compose -f docker-compose.mcp.yml down`.

---

## Notes

- These procedures are deliberately tool-agnostic so they work for Copilot,
  Cursor, Claude Code, Codex CLI, and any other agent reading
  [AGENTS.md](../../AGENTS.md).
- Amendments to this file are PATCH-level changes and do not require a
  Constitution version bump.
