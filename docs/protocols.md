# Coordination Protocols

This document defines the file-based coordination protocols that agents, the orchestrator, and the application use to share state, pass context, and track work. These protocols are the foundation of Phase 2 — everything else reads and writes these files.

## Directory Structure

```
.teamwork/
├── config.yaml               # Project-level orchestration settings
├── state/                    # Workflow execution state (one file per workflow)
│   └── <workflow-id>.yaml    # e.g., feature/42-add-oauth.yaml
├── handoffs/                 # Artifacts passed between roles
│   └── <workflow-id>/        # Grouped by workflow instance
│       └── <step>-<role>.md  # e.g., 01-planner.md, 02-architect.md
├── memory/                   # Structured project memory
│   ├── patterns.yaml         # What works well
│   ├── antipatterns.yaml     # What to avoid
│   ├── decisions.yaml        # Key decisions with rationale
│   ├── feedback.yaml         # Reviewer and human feedback
│   └── index.yaml            # Searchable index by domain/topic
└── metrics/                  # Agent activity logs (gitignored)
    └── <workflow-id>.jsonl   # One log file per workflow instance
```

### Git Tracking

| Directory | Tracked | Reason |
|-----------|---------|--------|
| `.teamwork/config.yaml` | Yes | Project configuration, shared by all contributors |
| `.teamwork/state/` | Yes | Audit trail of workflow progression |
| `.teamwork/handoffs/` | Yes | Artifacts are part of the project record |
| `.teamwork/memory/` | Yes | Persistent knowledge across sessions |
| `.teamwork/metrics/` | No | Unbounded JSONL logs, local runtime data |

### Workflow IDs

Workflow IDs are human-readable slugs used as file and directory names:

- **With issue:** `<workflow-type>/<issue-number>-<short-description>` → `feature/42-add-oauth`
- **Without issue:** `<workflow-type>/<short-description>` → `refactor/extract-auth-module`

Workflow types match branch prefixes: `feature`, `bugfix`, `refactor`, `hotfix`, `docs`, `chore`, `security`, `spike`, `release`, `rollback`.

Rules:
- Lowercase, kebab-case for the description portion
- No spaces, no special characters beyond hyphens
- Keep descriptions under 40 characters
- The workflow ID becomes both the state filename and the handoffs directory name

---

## Workflow State Schema

Each active workflow instance has a YAML state file at `.teamwork/state/<workflow-id>.yaml`.

### Schema

```yaml
# Workflow identity
id: feature/42-add-oauth          # Matches filename (without .yaml)
type: feature                      # One of: feature, bugfix, refactor, hotfix,
                                   #   security, docs, spike, release, rollback
status: active                     # One of: active, blocked, completed, failed, cancelled

# Origin
goal: "Add OAuth2 login support"   # Human's original goal statement
issue: 42                          # GitHub issue number (optional)
branch: feature/42-add-oauth       # Git branch name (optional)
pull_request: null                 # PR number, set when opened (optional)

# Current position in the workflow
current_step: 3                    # Step number (1-indexed)
current_role: coder                # Role currently responsible

# Step history — append-only log of completed steps
steps:
  - step: 1
    role: planner
    action: "Broke goal into 4 tasks with acceptance criteria"
    started: "2026-03-01T10:00:00Z"
    completed: "2026-03-01T10:15:00Z"
    handoff: "01-planner.md"       # Filename in handoffs/ directory
    quality_gate: passed

  - step: 2
    role: architect
    action: "Designed OAuth flow, wrote ADR-003"
    started: "2026-03-01T10:20:00Z"
    completed: "2026-03-01T11:00:00Z"
    handoff: "02-architect.md"
    quality_gate: passed

# Blockers (if status is 'blocked')
blockers: []
  # - reason: "Unclear whether to support Google and GitHub or just GitHub"
  #   raised_by: architect
  #   raised_at: "2026-03-01T10:30:00Z"
  #   escalated_to: human

# Metadata
created_at: "2026-03-01T09:55:00Z"
updated_at: "2026-03-01T11:00:00Z"
created_by: orchestrator
```

### Status Values

| Status | Meaning |
|--------|---------|
| `active` | Workflow is in progress; a role is currently working |
| `blocked` | Workflow cannot proceed; see `blockers` field |
| `completed` | All steps finished, quality gates passed |
| `failed` | Workflow could not be completed; requires human decision |
| `cancelled` | Human cancelled the workflow before completion |

### State Transitions

```
          ┌──────────┐
          │  active   │◄─────────────────────┐
          └────┬──────┘                      │
               │                             │
        ┌──────┼──────┐                      │
        ▼      ▼      ▼                      │
   blocked  completed  failed           (unblocked)
        │                                    │
        └────────────────────────────────────┘
```

- `active` → `blocked`: When a blocker is raised
- `blocked` → `active`: When all blockers are resolved
- `active` → `completed`: When the final step passes its quality gate
- `active` → `failed`: When a step fails and cannot be retried
- Any state → `cancelled`: Human cancels the workflow

---

## Handoff Artifact Format

Handoff artifacts are markdown files in `.teamwork/handoffs/<workflow-id>/`. Each file represents the output of one workflow step, structured so the next role can start without re-reading the entire repo.

### Filename Convention

`<step-number>-<role>.md` — e.g., `01-planner.md`, `02-architect.md`, `03-coder.md`

Zero-padded step numbers ensure correct sort order.

### Template

```markdown
# Handoff: <Role> → <Next Role>

**Workflow:** <workflow-id>
**Step:** <step-number>
**Date:** <ISO 8601 timestamp>

## Summary

One paragraph: what was done and the key outcome.

## Artifacts Produced

List of concrete outputs:
- Files created or modified (with paths)
- Decisions made (with brief rationale)
- Issues or PRs created

## Context for Next Role

Information the next role needs to do its work effectively:
- Key constraints or requirements
- Relevant code locations
- Dependencies or sequencing notes

## Acceptance Criteria Status

Status of the criteria from the task or goal:
- [x] Criterion that was met
- [ ] Criterion that remains (for future steps)

## Open Questions or Risks

Issues that need attention (may require escalation):
- Question or risk with brief context

## Quality Gate

- [ ] Handoff reviewed by orchestrator
- [ ] All required fields populated
- [ ] Artifacts exist at referenced paths
```

### Rules

1. Every handoff artifact must populate all sections. Use "None" if a section is empty, not omission.
2. The orchestrator validates the handoff before advancing the workflow to the next step.
3. Handoff artifacts are append-only — once written, they are not modified (corrections go in the next handoff).
4. File paths in "Artifacts Produced" must be relative to the repository root.

---

## Memory Structure

Structured project memory lives in `.teamwork/memory/`. This extends (but does not replace) `MEMORY.md` — the human-readable summary remains at the project root.

### Files

| File | Purpose |
|------|---------|
| `patterns.yaml` | Approaches that work well — repeat these |
| `antipatterns.yaml` | Approaches that failed — avoid these |
| `decisions.yaml` | Key decisions with rationale and date |
| `feedback.yaml` | Reviewer and human feedback that applies broadly |
| `index.yaml` | Searchable index mapping domains/topics to entries |

### Entry Format

All memory files use the same entry structure:

```yaml
entries:
  - id: pattern-001
    date: "2026-03-01"
    source: "PR #42 review"           # Where this was learned
    domain: ["auth", "api"]           # Topic tags for searchability
    content: "Use middleware for auth checks rather than per-route validation"
    context: "Discovered during OAuth implementation — per-route was error-prone"
```

### Index Format

The index maps domains to entry IDs for fast lookup:

```yaml
# .teamwork/memory/index.yaml
domains:
  auth:
    - pattern-001
    - decision-003
    - feedback-007
  api:
    - pattern-001
    - antipattern-002
  testing:
    - pattern-004
    - feedback-012
```

### Maintenance

- **Adding entries:** Agents append entries after learning something that future agents should know. Use the next available ID in the file (e.g., `pattern-005`).
- **Archiving:** When a file exceeds 50 entries, move the oldest half to `<file>.archive.yaml`. The archive is tracked but not read by default.
- **Pruning:** Entries explicitly marked obsolete by a human can be removed during archival.
- **Sync with MEMORY.md:** Significant entries should also be reflected in `MEMORY.md` for human readability. The documenter role handles this sync.

---

## Metrics Logging Format

Metrics are stored as JSONL (JSON Lines) files in `.teamwork/metrics/`. One file per workflow instance. These files are gitignored — they are local runtime data for reporting.

### File Convention

`.teamwork/metrics/<workflow-id>.jsonl`

Slashes in workflow IDs are replaced with double underscores in filenames: `feature/42-add-oauth` → `feature__42-add-oauth.jsonl`

### Entry Format

One JSON object per line, appended as events occur:

```json
{"ts":"2026-03-01T10:00:00Z","workflow":"feature/42-add-oauth","step":1,"role":"planner","action":"start","detail":"Breaking goal into tasks"}
{"ts":"2026-03-01T10:15:00Z","workflow":"feature/42-add-oauth","step":1,"role":"planner","action":"complete","detail":"4 tasks created","duration_sec":900}
{"ts":"2026-03-01T10:16:00Z","workflow":"feature/42-add-oauth","step":1,"role":"orchestrator","action":"quality_gate","detail":"Handoff validated","result":"passed"}
{"ts":"2026-03-01T10:20:00Z","workflow":"feature/42-add-oauth","step":2,"role":"architect","action":"start","detail":"Designing OAuth flow"}
```

### Fields

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `ts` | ISO 8601 string | Yes | Timestamp of the event |
| `workflow` | string | Yes | Workflow ID |
| `step` | integer | Yes | Step number in the workflow |
| `role` | string | Yes | Role that performed the action |
| `action` | string | Yes | One of: `start`, `complete`, `fail`, `escalate`, `quality_gate`, `block`, `unblock` |
| `detail` | string | Yes | Human-readable description |
| `duration_sec` | integer | No | Duration in seconds (for `complete` and `fail` actions) |
| `result` | string | No | Outcome (for `quality_gate`: `passed` or `failed`) |
| `cost_estimate` | string | No | Token/cost estimate if available |
| `error` | string | No | Error message (for `fail` actions) |

### Aggregation

The application (Layer 3) aggregates metrics for reporting. Common queries:
- Average duration per role across workflows
- Failure rate per workflow type
- Most common escalation reasons
- Quality gate pass/fail rates

---

## Config Schema

Project-level orchestration configuration lives at `.teamwork/config.yaml`.

### Schema

```yaml
# .teamwork/config.yaml — Project orchestration settings

# Project identity
project:
  name: "my-project"
  repo: "owner/repo"               # GitHub owner/repo

# Active roles — only these roles participate in workflows
roles:
  core:
    - planner
    - architect
    - coder
    - tester
    - reviewer
    - security-auditor
    - documenter
    - orchestrator
  optional: []                      # Add: triager, devops, dependency-manager, refactorer

# Workflow customizations
workflows:
  # Skip steps for specific workflow types
  skip_steps:
    documentation:
      - security-auditor            # Docs-only changes don't need security audit
    spike:
      - tester                      # Spikes produce research, not testable code
      - security-auditor

  # Additional quality gates beyond the defaults
  extra_gates: {}
    # feature:
    #   after_step_3:
    #     - "All unit tests pass"
    #     - "Coverage above 80%"

# Quality gate defaults (apply to all workflows unless overridden)
quality_gates:
  handoff_complete: true            # All handoff sections must be populated
  tests_pass: true                  # Tests must pass before handoff
  lint_pass: true                   # Linting must pass before handoff

# Memory settings
memory:
  archive_threshold: 50             # Archive entries when file exceeds this count
  sync_to_memory_md: true           # Keep MEMORY.md in sync with structured memory

# Multi-repo coordination (hub-spoke model)
# The repo where teamwork runs is the hub. Spoke repos are listed here.
# repos:
#   - name: "api"                   # Short identifier used in commands
#     path: "../api"                # Local path (relative to hub root)
#     repo: "owner/api"            # GitHub owner/repo slug
#   - name: "frontend"
#     path: "../frontend"
#     repo: "owner/frontend"
```

### Customization

Projects customize this file to:
- Enable/disable optional roles
- Skip workflow steps that don't apply (e.g., security audit for docs changes)
- Add project-specific quality gates
- Configure memory management
- (Future) Define multi-repo coordination

---

## Quality Gates

Quality gates are checkpoints the orchestrator enforces between workflow steps. A step's output must pass its quality gate before the next step begins.

### Default Gates

These apply to every handoff unless disabled in config:

| Gate | What It Checks | How |
|------|---------------|-----|
| **Handoff complete** | All required sections in the handoff artifact are populated | Orchestrator validates the markdown structure |
| **Tests pass** | Relevant tests pass after the step's changes | `make test` (when applicable) |
| **Lint pass** | Code meets linting standards | `make lint` (when applicable) |

### Role-Specific Gates

| After Role | Additional Gate |
|-----------|----------------|
| Coder | PR opened, tests written for new code |
| Reviewer | Explicit approval recorded |
| Security Auditor | No critical/high findings, or findings acknowledged |
| Tester | All acceptance criteria verified |

### Gate Results

Gates produce one of three results:
- **Passed** — workflow advances to the next step
- **Failed** — workflow stays at current step; role must address the failure
- **Escalated** — gate cannot be evaluated automatically; human must decide

### Configuring Gates

Projects can skip gates, add gates, or modify gate behavior in `.teamwork/config.yaml`. See the config schema above.

---

## Multi-Repo Coordination

Teamwork supports coordinating work across multiple repositories using a **hub-and-spoke model**.

### Hub-and-Spoke Model

- **Hub repo** — The repository where `teamwork` is run and `.teamwork/` lives. Stores workflow state, handoffs, and memory.
- **Spoke repos** — Additional repositories listed in the `repos:` section of `config.yaml`. Each has a name, local path, and GitHub slug.

### How It Works

1. Configure spoke repos in `.teamwork/config.yaml` under the `repos:` key.
2. Use `teamwork repos` to verify configured repos are accessible.
3. Workflow steps can target specific repos via the `repo` field in `StepRecord`.
4. `teamwork status` and `teamwork next` show which repo each step targets.
5. Use `teamwork memory sync --repo <name> --domain <domains>` to copy relevant memory entries from the hub to a spoke repo.

### Configuration

```yaml
repos:
  - name: "api"
    path: "../api"
    repo: "owner/api"
  - name: "frontend"
    path: "../frontend"
    repo: "owner/frontend"
```

| Field | Required | Description |
|-------|----------|-------------|
| `name` | Yes | Short identifier used in commands and state files |
| `path` | Yes | Local filesystem path (relative to hub root or absolute) |
| `repo` | Yes | GitHub `owner/repo` slug |
