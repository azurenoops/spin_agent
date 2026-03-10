# Roadmap: v1.3.0 — v1.5.0

**Date:** 2026-03-09
**Author:** Planner Agent
**Scope:** Next 3 minor releases after v1.2.0 (Phase 5: Developer Experience)

---

## Table of Contents

1. [Backlog Analysis — Sizing & Dependencies](#1-backlog-analysis--sizing--dependencies)
2. [New Feature Proposals](#2-new-feature-proposals)
3. [Release Plan](#3-release-plan)
   - [v1.3.0 — Workflow Resilience & Quality Gates](#v130--workflow-resilience--quality-gates)
   - [v1.4.0 — Smart Context & Agent Intelligence](#v140--smart-context--agent-intelligence)
   - [v1.5.0 — Adoption & Extensibility](#v150--adoption--extensibility)
4. [Dependency Graph](#4-dependency-graph)
5. [Success Metrics](#5-success-metrics)

---

## 1. Backlog Analysis — Sizing & Dependencies

### Existing Issues (8)

| # | Title | Complexity | Estimated Scope | Dependencies | Risk | Notes |
|---|-------|-----------|-----------------|--------------|------|-------|
| **#39** | Workflow checkpointing and resume | **Medium** | 3–4 files, ~300 lines | None | Medium | Needs clear definition of "resume" semantics — state.go already persists to disk, but there's no `teamwork resume` command or snapshot/restore beyond current step. Risk: scope creep into full session replay. |
| **#40** | Programmatic state query/filtering | **Medium** | 3–5 files, ~250 lines | None | Low | Well-defined: add `--filter`, `--json`, `--sort` flags to `teamwork status`. Builds on existing `state.LoadAll()`. Straightforward CLI flag work + output formatting. |
| **#41** | Semantic search over memory | **Large** | 6–8 files, ~600 lines | None (but synergizes with #44, #62) | High | "Semantic" could range from keyword/fuzzy matching to embedding-based vector search. Current `memory.Search()` only does domain-tag lookup. Recommend scoping to full-text keyword search first, embeddings as follow-up. |
| **#44** | Structured feedback loop | **Medium** | 4–5 files, ~350 lines | None (enhanced by #41) | Medium | Needs to define feedback capture format, storage (likely extends `memory.Feedback` category), and surfacing mechanism. Risk: unclear how to trigger "auto-capture" — likely requires GitHub API integration for PR review comments. |
| **#46** | Automated secrets scanning gate | **Small** | 2–3 files, ~150 lines | None | Low | Well-defined: add a quality gate that shells out to gitleaks/trufflehog. Config addition to `quality_gates` section, validation integration. Low risk — gitleaks is mature, interface is simple. |
| **#55** | Example projects | **Medium** | 5–8 files, ~400 lines (content) | None | Low | Content-heavy, no Go code. Needs 2–3 example repos/directories showing complete workflows. Risk: maintenance burden — examples rot if not tested. |
| **#61** | Workflow analytics | **Medium** | 3–5 files, ~300 lines | None (builds on existing `metrics` pkg) | Low | Extends `metrics.SummarizeAll()` with aggregate views: counts by type, avg durations, trends. New `teamwork analytics` command or enhancement to `teamwork metrics summary`. |
| **#62** | Context distillation | **Large** | 6–10 files, ~700 lines | Enhanced by #41, #44 | High | Ambitious: auto-assemble context packages per workflow step from memory, handoffs, ADRs, and code. Needs to understand what each role needs. Risk: hard to define "good enough" — too little context and agents underperform, too much and they exceed context windows. |

### Complexity Distribution

```
Small  (1–2 files, <200 lines):  #46
Medium (3–5 files, 200–500 lines): #39, #40, #44, #55, #61
Large  (6+ files, 500+ lines):   #41, #62
```

---

## 2. New Feature Proposals

### NF-1: Step-level retry and rollback

**Description:** Allow retrying a failed or blocked workflow step without failing the entire workflow. Add `teamwork retry <workflow-id>` to re-run the current step, and `teamwork rollback-step <workflow-id>` to revert to the previous step. Currently, a failed step means the whole workflow fails — this makes recovery expensive.

**Why it matters:** Workflows fail for transient reasons (bad prompt, incomplete context, timeout). Forcing a full workflow restart wastes all prior steps' work. Step-level retry is the single biggest quality-of-life improvement for daily use.

**Complexity:** Medium (3–4 files: `internal/workflow/engine.go`, `internal/state/state.go`, new CLI commands)
**Best release:** v1.3.0

---

### NF-2: Pluggable quality gate hooks

**Description:** Allow users to define custom quality gate scripts in `config.yaml` that run at step boundaries. Example: `after_step_4: ["scripts/check-coverage.sh", "scripts/lint-api-spec.sh"]`. The existing `extra_gates` config key is defined but has no runtime implementation — this feature wires it up.

**Why it matters:** Every project has unique quality requirements (minimum coverage, API spec validation, compliance checks). Currently quality gates are hardcoded to handoff-complete, tests-pass, and lint-pass. Custom hooks let teams enforce their own standards without forking the framework.

**Complexity:** Medium (3–4 files: `internal/workflow/engine.go`, `internal/validate/`, new `internal/gates/` package)
**Best release:** v1.3.0

---

### NF-3: Workflow timeline visualization

**Description:** Add `teamwork timeline <workflow-id>` that renders a visual timeline of completed and pending steps. Output as a formatted ASCII table (terminal) or Mermaid Gantt diagram (`--mermaid` flag). Shows step number, role, status, duration, and handoff file for each step.

**Why it matters:** `teamwork history` outputs raw state data. A timeline gives immediate visual understanding of where a workflow is, how long each step took, and where bottlenecks occurred. Essential for retrospectives and debugging slow workflows.

**Complexity:** Small (2 files: new CLI command, rendering logic — data already exists in state + metrics)
**Best release:** v1.3.0

---

### NF-4: Agent performance scoring

**Description:** Extend the metrics system to score agent effectiveness per workflow. Track: steps completed without rework, quality gate pass rate, defect escape rate per role, and average step duration. Add `teamwork metrics agents` command to surface a per-agent scorecard.

**Why it matters:** Teams need data to know which agent configurations work well and which need prompt tuning. Without scoring, optimization is guesswork. This also enables A/B testing of different model tiers or prompt versions for the same role.

**Complexity:** Medium (3–4 files: `internal/metrics/`, new CLI command, scoring logic)
**Best release:** v1.4.0

---

### NF-5: Handoff templates per workflow type

**Description:** Generate pre-structured handoff templates when a workflow step starts, populated with role-specific guidance and sections. Instead of agents starting from the generic handoff template in `docs/protocols.md`, `teamwork next` (or a new `teamwork handoff init`) produces a step-specific template: e.g., the Coder→Tester handoff template includes sections for "Files Changed," "Test Commands," and "Edge Cases Considered."

**Why it matters:** Handoff quality is the #1 driver of downstream agent effectiveness. Generic templates lead to inconsistent handoffs — role-specific templates ensure the right information is captured every time. Reduces rework caused by incomplete handoffs.

**Complexity:** Medium (4–5 files: template definitions per workflow type, template generation logic, CLI integration)
**Best release:** v1.4.0

---

### NF-6: Context window budget tracking

**Description:** Add an estimated token count to each workflow step, tracking how much context is consumed by the handoff, memory entries, and referenced files. Surface in `teamwork status` and `teamwork timeline`. Warn when estimated context exceeds configurable thresholds (e.g., 80% of a model's context window).

**Why it matters:** Context window overflow is a silent failure mode — agents start dropping information when context is too large, but nothing reports this. Budget tracking prevents "context amnesia" where critical instructions or context get truncated by the model.

**Complexity:** Medium (3–4 files: token estimation utility, integration into workflow engine, config for model context limits)
**Best release:** v1.4.0

---

### NF-7: Custom workflow definitions

**Description:** Allow users to define their own workflow types in `config.yaml` beyond the 10 built-in types. A `custom_workflows` section would specify the step sequence, roles, and quality gates. The workflow engine already uses a `definitions` map — this feature loads user-defined entries into it at startup.

**Why it matters:** Every team has unique processes. A data team might need a "data-pipeline" workflow. A design team might want a "design-review" workflow. Currently, custom workflows require forking the Go source. Config-driven definitions make Teamwork adaptable without code changes.

**Complexity:** Medium (3–4 files: config schema extension, workflow engine loader, validation, CLI updates)
**Best release:** v1.5.0

---

### NF-8: Config presets for popular stacks

**Description:** Add `teamwork init --preset <name>` with built-in presets for common stacks: `go-api`, `react-ts`, `python-ml`, `fullstack`. Each preset pre-fills `config.yaml` with appropriate roles, quality gates, skip steps, and MCP server recommendations. Store presets as embedded YAML files in the binary.

**Why it matters:** First-run configuration is the highest-friction moment in adoption. Most users don't know which roles to enable or which MCP servers to configure. Presets eliminate analysis paralysis and give users a working configuration in seconds. Directly addresses the "blank canvas" problem reported in onboarding.

**Complexity:** Medium (3–4 files: preset definitions, init command extension, preset selection logic)
**Best release:** v1.5.0

---

### NF-9: Guided first-workflow tutorial

**Description:** Add `teamwork tutorial` that walks a user through creating and completing a real workflow end-to-end. Interactive prompts guide the user through: creating a feature issue, starting a workflow, advancing through each step, writing a handoff, and completing the workflow. Uses a sample "add a greeting endpoint" task with pre-built handoff examples.

**Why it matters:** The learning curve for Teamwork's workflow model is steep — users need to understand roles, handoffs, quality gates, and state transitions before they can be productive. A guided tutorial reduces time-to-first-workflow from ~30 minutes of doc reading to ~5 minutes of interactive learning.

**Complexity:** Medium (4–5 files: tutorial command, step scripts, sample data, progress tracking)
**Best release:** v1.5.0

---

### NF-10: Configuration drift detection

**Description:** Add `teamwork update --check` (dry-run) that compares local framework files against the upstream template version and reports drift: files modified locally, files added upstream, files deleted locally. Output as a diff summary with actionable recommendations. Extends the existing `teamwork update` command.

**Why it matters:** After `teamwork update`, users don't know which of their local customizations conflict with upstream changes. The current update process uses SHA-256 manifest comparison but only at update time. Drift detection lets teams audit their configuration health proactively, especially important before major version upgrades.

**Complexity:** Small (2–3 files: extends existing installer package's manifest comparison, new CLI flag)
**Best release:** v1.5.0

---

### NF-11: Exportable workflow reports

**Description:** Add `teamwork report <workflow-id> --format md|html|json` that generates a comprehensive report for a completed workflow. Includes: goal, all steps with durations, handoff summaries, quality gate results, metrics summary, and timeline. Markdown output suitable for pasting into GitHub issues or wikis. HTML output for sharing with non-technical stakeholders.

**Why it matters:** Completed workflows generate valuable data spread across state files, handoffs, and metrics. There's no single view that captures the full story. Reports enable retrospectives, stakeholder updates, and compliance documentation. The JSON format enables integration with external dashboards.

**Complexity:** Medium (3–4 files: report generation logic, template rendering, CLI command)
**Best release:** v1.5.0

---

### NF-12: Agent file linting and health checks

**Description:** Extend `teamwork validate` (or add `teamwork doctor --agents`) to lint agent files in `.github/agents/`. Check for: unfilled `<!-- CUSTOMIZE -->` placeholders, missing required sections (Identity, Responsibilities, Boundaries, MCP Tools), invalid model tier references, and role names that don't match config. Report as warnings (not errors) to avoid blocking workflows.

**Why it matters:** Agent file quality directly impacts agent effectiveness. Currently `teamwork validate` checks config and state but not agent files. Teams customize agents and unknowingly break the structure — e.g., removing the Boundaries section eliminates guardrails. Automated linting catches these issues before they cause silent failures.

**Complexity:** Small (2–3 files: validation rules for agent markdown, integration into validate command)
**Best release:** v1.3.0

---

## 3. Release Plan

### v1.3.0 — Workflow Resilience & Quality Gates

**Theme:** Make existing workflows more robust, recoverable, and observable. Add quality infrastructure that teams need before trusting Teamwork for production work.

**Target:** 7 items (2 small, 4 medium, 1 medium-stretch)

| Priority | Issue | Title | Complexity | Type |
|----------|-------|-------|-----------|------|
| 1 | **#40** | Programmatic state query/filtering (`--filter`, `--json`, `--sort`) | Medium | Backlog |
| 2 | **#46** | Automated secrets scanning gate | Small | Backlog |
| 3 | **NF-12** | Agent file linting and health checks | Small | New |
| 4 | **#39** | Workflow checkpointing and resume | Medium | Backlog |
| 5 | **NF-1** | Step-level retry and rollback | Medium | New |
| 6 | **NF-2** | Pluggable quality gate hooks | Medium | New |
| 7 | **NF-3** | Workflow timeline visualization | Small | New |

**Rationale:**
- Starts with quick wins (#40, #46, NF-12) that deliver immediate value and build momentum
- Workflow resilience (#39, NF-1) addresses the most common pain point: recovering from failed steps
- Quality gate hooks (NF-2) wire up the already-defined-but-unimplemented `extra_gates` config
- Timeline visualization (NF-3) leverages existing state + metrics data with minimal new code
- No external dependencies or risky scope — all items build on existing internal packages
- Total estimated effort: ~1,600 lines across ~20 files

**What ships:**
- `teamwork status --filter role=coder --json --sort updated`
- `teamwork resume <workflow-id>` and `teamwork retry <workflow-id>`
- `teamwork timeline <workflow-id>` with optional `--mermaid`
- Secrets scanning quality gate via gitleaks integration
- Agent file linting in `teamwork validate`
- User-defined quality gate scripts in `config.yaml`

---

### v1.4.0 — Smart Context & Agent Intelligence

**Theme:** Make agents significantly more effective by giving them better context, learning from past work, and tracking what works. This is the "agents get smarter over time" release.

**Target:** 6 items (0 small, 4 medium, 2 large)

| Priority | Issue | Title | Complexity | Type |
|----------|-------|-------|-----------|------|
| 1 | **#44** | Structured feedback loop | Medium | Backlog |
| 2 | **NF-5** | Handoff templates per workflow type | Medium | New |
| 3 | **#61** | Workflow analytics (aggregate reporting) | Medium | Backlog |
| 4 | **NF-4** | Agent performance scoring | Medium | New |
| 5 | **#41** | Semantic search over memory | Large | Backlog |
| 6 | **#62** | Context distillation | Large | Backlog |

**Rationale:**
- Feedback loop (#44) is foundational — later features (#41, #62) are more effective when there's rich feedback data to work with
- Handoff templates (NF-5) improve handoff quality immediately, making context distillation (#62) more reliable
- Analytics (#61) and scoring (NF-4) pair naturally — both extend the metrics system and surface insights about workflow effectiveness
- Semantic search (#41) should be scoped to full-text keyword search with fuzzy matching (not embeddings) to manage risk; embeddings can be a v1.6+ follow-up
- Context distillation (#62) is the capstone — it synthesizes memory, handoffs, search, and feedback into per-step context packages
- The two large items (#41, #62) are placed last in priority so they can be cut or deferred if the release needs to ship
- Total estimated effort: ~2,200 lines across ~25 files

**What ships:**
- Auto-captured PR review feedback stored in `.teamwork/memory/feedback.yaml`
- Role-specific handoff templates generated by `teamwork next` or `teamwork handoff init`
- `teamwork analytics` with aggregate workflow counts, durations, trends, and success rates
- `teamwork metrics agents` with per-agent scorecards (pass rate, rework rate, avg duration)
- `teamwork memory search <query>` with full-text keyword search across all memory + handoffs
- `teamwork context <workflow-id>` assembling focused context packages per step

---

### v1.5.0 — Adoption & Extensibility

**Theme:** Make Teamwork dramatically easier to adopt for new teams and extensible for advanced users. Lower the barrier to entry while raising the ceiling for customization.

**Target:** 6 items (1 small, 4 medium, 1 medium-content)

| Priority | Issue | Title | Complexity | Type |
|----------|-------|-------|-----------|------|
| 1 | **NF-10** | Configuration drift detection | Small | New |
| 2 | **NF-8** | Config presets for popular stacks | Medium | New |
| 3 | **NF-9** | Guided first-workflow tutorial | Medium | New |
| 4 | **NF-7** | Custom workflow definitions | Medium | New |
| 5 | **NF-11** | Exportable workflow reports | Medium | New |
| 6 | **#55** | Example projects | Medium | Backlog |

**Rationale:**
- Drift detection (NF-10) is a quick win that extends existing installer infrastructure
- Presets (NF-8) and tutorial (NF-9) directly address the two biggest adoption barriers: configuration complexity and learning curve
- Custom workflows (NF-7) is the highest-impact extensibility feature — it transforms Teamwork from a fixed-workflow framework to a configurable platform
- Reports (NF-11) enable stakeholder communication and compliance use cases, broadening appeal
- Example projects (#55) are last because they benefit from showcasing all features from v1.3 and v1.4
- NF-6 (Context window budget tracking) is deliberately deferred to v1.6+ — it's valuable but depends on model-specific token counting that is rapidly evolving
- Total estimated effort: ~1,800 lines across ~22 files

**What ships:**
- `teamwork update --check` showing local vs. upstream drift
- `teamwork init --preset go-api|react-ts|python-ml|fullstack`
- `teamwork tutorial` interactive first-workflow walkthrough
- User-defined workflows in `config.yaml` under `custom_workflows`
- `teamwork report <workflow-id> --format md|html|json`
- 2–3 example projects demonstrating end-to-end Teamwork workflows

---

## 4. Dependency Graph

```
v1.3.0 — Workflow Resilience & Quality Gates
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
  #40 Status query/filtering  ──────────────────── (independent, ship first)
  #46 Secrets scanning gate   ──────────────────── (independent, ship first)
  NF-12 Agent file linting    ──────────────────── (independent, ship first)
  #39 Checkpointing & resume  ──┐
                                ├── NF-1 must build on #39's resume semantics
  NF-1 Step retry/rollback    ──┘
  NF-2 Quality gate hooks     ──────────────────── (independent)
  NF-3 Timeline visualization ──────────────────── (independent, benefits from #40's --json)

v1.4.0 — Smart Context & Agent Intelligence
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
  #44 Structured feedback     ──┐
                                ├── #41 is more valuable with feedback data
  NF-5 Handoff templates      ──┤
                                ├── #62 synthesizes output from #41, #44, NF-5
  #61 Workflow analytics      ──┤
                                ├── NF-4 extends #61's metrics infrastructure
  NF-4 Agent scoring          ──┘
  #41 Semantic search         ──┐
                                ├── #62 depends on search to assemble context
  #62 Context distillation    ──┘

v1.5.0 — Adoption & Extensibility
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
  NF-10 Drift detection       ──────────────────── (independent, extends installer)
  NF-8  Config presets        ──┐
                                ├── NF-9 benefits from presets being available
  NF-9  Guided tutorial       ──┘
  NF-7  Custom workflows      ──────────────────── (independent)
  NF-11 Exportable reports    ──────────────────── (benefits from v1.4 analytics)
  #55   Example projects      ──────────────────── (last: showcases all prior features)
```

### Cross-release dependencies

```
NF-1 (v1.3) ← depends on → #39 (v1.3)     ✅ Same release
NF-4 (v1.4) ← extends →    #61 (v1.4)     ✅ Same release  
#62  (v1.4) ← depends on → #41 (v1.4)     ✅ Same release
#62  (v1.4) ← enhanced by → #44 (v1.4)    ✅ Same release
NF-11(v1.5) ← benefits from → #61 (v1.4)  ✅ Correct order
#55  (v1.5) ← showcases →  v1.3 + v1.4    ✅ Correct order

No circular dependencies. No cross-release blockers.
```

---

## 5. Success Metrics

### v1.3.0 — Workflow Resilience & Quality Gates

| Metric | Target | How to Measure |
|--------|--------|---------------|
| **Step recovery rate** | >80% of failed steps recovered via retry without full workflow restart | Track `retry` action events in metrics JSONL |
| **Status query adoption** | `--json` or `--filter` used in >50% of `teamwork status` invocations | Usage analytics in metrics (if opted-in) or community feedback |
| **Quality gate coverage** | ≥1 custom quality gate configured in 3+ community projects | Survey/issue scan of repos using the template |
| **Secrets scan catch rate** | Secrets scanning gate blocks ≥1 commit with leaked secrets in testing | E2E test with intentional secret in test fixture |
| **Agent file health** | Zero unfilled `<!-- CUSTOMIZE -->` placeholders reported after `teamwork validate` in configured projects | Validate command output |
| **Timeline usability** | Timeline visualization renders correctly for workflows with 1–15 steps | Unit tests + manual verification |

**Qualitative signal:** Users report fewer "workflow failed, had to start over" complaints.

---

### v1.4.0 — Smart Context & Agent Intelligence

| Metric | Target | How to Measure |
|--------|--------|---------------|
| **Feedback capture rate** | >90% of PR reviews with comments produce a feedback memory entry | Count `feedback-*` entries vs. merged PRs with review comments |
| **Search relevance** | Memory search returns relevant results for >80% of queries (top-3) | Manual evaluation on test corpus of 50+ memory entries |
| **Handoff completeness** | Role-specific handoff templates reduce incomplete handoffs by >50% | Compare quality gate fail rates for handoff completeness before/after |
| **Agent score variance** | Performance scores differ meaningfully across roles (not all 100%) | Statistical variance in `teamwork metrics agents` output |
| **Context package size** | Distilled context packages are <50% of raw context size while retaining key information | Token count comparison: raw vs. distilled |
| **Analytics accuracy** | Aggregate analytics match manual count of workflow states | Cross-reference `teamwork analytics` output with `teamwork status --json` |

**Qualitative signal:** Users report agents "know more about the project" and produce fewer context-related errors.

---

### v1.5.0 — Adoption & Extensibility

| Metric | Target | How to Measure |
|--------|--------|---------------|
| **Time to first workflow** | New users complete first workflow in <10 minutes using tutorial | Time from `teamwork tutorial` start to workflow completion |
| **Preset adoption** | >60% of `teamwork init` invocations use a `--preset` flag | Init command tracking or community survey |
| **Custom workflow creation** | ≥3 community-contributed custom workflow definitions shared | GitHub issues/discussions or example repos |
| **Drift detection coverage** | `teamwork update --check` correctly identifies >95% of local modifications | Test suite with known modifications |
| **Example project completeness** | Each example project passes `teamwork validate` and demonstrates ≥3 workflow types | CI validation of example projects |
| **Report generation** | Reports render correctly in GitHub markdown and HTML for all 10+ workflow types | E2E tests generating reports for each built-in workflow type |

**Qualitative signal:** New teams adopt Teamwork without needing to read all documentation first. Advanced teams build custom workflows without forking.

---

## Summary

### Release timeline and effort estimates

| Release | Theme | Backlog Issues | New Features | Total Items | Est. Lines | Est. Files |
|---------|-------|---------------|-------------|-------------|-----------|-----------|
| **v1.3.0** | Workflow Resilience & Quality Gates | #39, #40, #46 | NF-1, NF-2, NF-3, NF-12 | 7 | ~1,600 | ~20 |
| **v1.4.0** | Smart Context & Agent Intelligence | #41, #44, #61, #62 | NF-4, NF-5 | 6 | ~2,200 | ~25 |
| **v1.5.0** | Adoption & Extensibility | #55 | NF-7, NF-8, NF-9, NF-10, NF-11 | 6 | ~1,800 | ~22 |

### Backlog distribution

All 8 existing backlog issues are assigned to a release:

| Issue | Release | Rationale |
|-------|---------|-----------|
| #39 Checkpointing/resume | v1.3.0 | Core resilience feature |
| #40 State query/filtering | v1.3.0 | Quick win, pairs with status improvements |
| #41 Semantic search | v1.4.0 | Foundation for context intelligence |
| #44 Feedback loop | v1.4.0 | Feeds into search and context distillation |
| #46 Secrets scanning | v1.3.0 | Quick win, quality gate infrastructure |
| #55 Example projects | v1.5.0 | Showcases all features from prior releases |
| #61 Workflow analytics | v1.4.0 | Pairs with agent scoring |
| #62 Context distillation | v1.4.0 | Capstone intelligence feature |

### Deliberately deferred (v1.6+)

| Feature | Reason for deferral |
|---------|-------------------|
| **NF-6: Context window budget tracking** | Depends on model-specific tokenizer APIs that are rapidly evolving; premature to standardize now |
| **Embedding-based semantic search** | v1.4.0 ships keyword/fuzzy search; embeddings require infrastructure decisions (local vs. API, vector storage) better made after keyword search validates the UX |
| **Agent marketplace / community registry** | Requires ecosystem maturity — needs enough users creating custom agents/workflows to justify a sharing platform |
| **Multi-model A/B testing** | Interesting for agent optimization but requires performance scoring (v1.4.0) to be in place first |
