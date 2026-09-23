# Implementation Plan: Feature 016 — Comprehensive User & Persona Documentation

**Branch**: `016-user-documentation` | **Date**: 2026-02-28 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/016-user-documentation/spec.md`

## Summary

Create comprehensive, all-encompassing user documentation for Security Posture Intelligence Navigator organized by persona (ISSM, ISSO, SCA, AO, Engineer, Administrator). The documentation covers natural language interaction patterns, RMF phase workflows, document production, assessment guidance, cross-persona collaboration, portfolio management, air-gapped operations, onboarding, and troubleshooting. Delivered as a static documentation site (MkDocs) generated from Markdown files in `docs/`.

**Technical Approach**: Restructure and expand the existing `docs/guides/` Markdown files into a comprehensive documentation site using MkDocs with Material theme. No production code changes — this is a documentation-only feature. Existing guides are preserved and enhanced; new pages are added for gaps identified in the spec (getting started, portfolio management, troubleshooting, NL query reference, document catalog).

## Technical Context

**Language/Version**: Markdown + MkDocs (Python-based static site generator)
**Primary Dependencies**: MkDocs, mkdocs-material theme, mkdocs-search plugin
**Storage**: N/A (static Markdown files in `docs/`)
**Testing**: Link validation (markdown-link-check), spell check, build verification (`mkdocs build --strict`)
**Target Platform**: Static HTML site hosted alongside repo (GitHub Pages or internal web server)
**Project Type**: Documentation site
**Performance Goals**: N/A (static site)
**Constraints**: Must work in air-gapped environments (offline-capable static site), all content must be unclassified
**Scale/Scope**: ~30 documentation pages, 6 persona guides, 114 tool references, 7 RMF phase walkthroughs

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Applicable? | Status | Notes |
|-----------|-------------|--------|-------|
| I. Documentation as Source of Truth | ✅ Yes | **PASS** | This feature *is* documentation. All content follows `/docs/` conventions. |
| II. BaseAgent/BaseTool Architecture | ❌ N/A | **PASS** | No agent/tool code changes. |
| III. Testing Standards | ✅ Yes | **PASS** | Documentation build verification (`mkdocs build --strict`), link validation, and spell check serve as "tests" for this feature. No code changes require unit tests. |
| IV. Azure Government & Compliance First | ✅ Yes | **PASS** | Air-gapped operation notes included per spec clarification. All content unclassified. |
| V. Observability & Structured Logging | ❌ N/A | **PASS** | No runtime code. |
| VI. Code Quality & Maintainability | ✅ Yes | **PASS** | Markdown files follow consistent structure, naming conventions, and cross-reference patterns. |
| VII. User Experience Consistency | ✅ Yes | **PASS** | Consistent navigation, persona-based organization, glossary, and quick-reference cards ensure predictable UX. |
| VIII. Performance Requirements | ❌ N/A | **PASS** | Static site — no runtime performance concerns. |

**Gate Result**: ✅ **PASS** — No violations. Proceed to Phase 0.

## Project Structure

### Documentation (this feature)

```text
specs/016-user-documentation/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output (documentation site structure)
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output (page templates/conventions)
└── tasks.md             # Phase 2 output (/speckit.tasks)
```

### Source — Documentation Site Layout

```text
docs/
├── index.md                          # Landing page / overview
├── getting-started/
│   ├── index.md                      # General onboarding overview
│   ├── issm.md                       # ISSM first-time setup
│   ├── isso.md                       # ISSO first-time setup
│   ├── sca.md                        # SCA first-time setup
│   ├── ao.md                         # AO first-time setup
│   └── engineer.md                   # Engineer first-time setup
├── personas/
│   ├── index.md                      # Persona overview & responsibility matrix
│   ├── issm.md                       # ISSM comprehensive guide (expanded from issm-guide.md)
│   ├── isso.md                       # ISSO comprehensive guide (new)
│   ├── sca.md                        # SCA comprehensive guide (expanded from sca-guide.md)
│   ├── ao.md                         # AO comprehensive guide (expanded from ao-quick-reference.md)
│   ├── engineer.md                   # Engineer guide (expanded from engineer-guide.md)
│   └── administrator.md             # Administrator guide (new)
├── rmf-phases/
│   ├── index.md                      # RMF lifecycle overview
│   ├── prepare.md                    # Phase 0: Prepare
│   ├── categorize.md                 # Phase 1: Categorize
│   ├── select.md                     # Phase 2: Select
│   ├── implement.md                  # Phase 3: Implement
│   ├── assess.md                     # Phase 4: Assess
│   ├── authorize.md                  # Phase 5: Authorize
│   └── monitor.md                    # Phase 6: Monitor
├── guides/                           # Existing guides (preserved, cross-linked)
│   ├── compliance-watch.md           # Existing — enhanced with air-gapped notes
│   ├── remediation-kanban.md         # Existing — enhanced
│   ├── teams-bot-guide.md            # Existing
│   ├── chat-app.md                   # Existing
│   ├── knowledgebase.md              # Existing
│   ├── portfolio-management.md       # NEW — multi-system workflows
│   ├── document-catalog.md           # NEW — all documents produced
│   └── nl-query-reference.md        # NEW — natural language query examples
├── reference/                        # Existing reference docs (preserved)
│   ├── glossary.md                   # Existing — expanded with new terms
│   ├── tool-inventory.md            # NEW — complete 114-tool reference
│   ├── troubleshooting.md           # NEW — error scenarios & resolution
│   ├── quick-reference-cards.md     # NEW — per-persona cheat sheets
│   ├── rmf-process.md               # Existing
│   ├── nist-controls.md             # Existing
│   ├── nist-coverage.md             # Existing
│   ├── impact-levels.md             # Existing
│   └── stig-coverage.md             # Existing
├── scenarios/
│   └── full-lifecycle.md            # NEW — end-to-end ACME Portal scenario
├── api/                              # Existing API docs (preserved)
├── architecture/                     # Existing arch docs (preserved)
├── deployment.md                     # Existing
├── dev/                              # Existing dev docs (preserved)
└── getting-started.md               # Existing (becomes redirect to getting-started/index.md)
```

**Structure Decision**: Documentation-only feature using the existing `docs/` directory. New pages are added under `getting-started/`, `personas/`, `rmf-phases/`, `scenarios/`, and `reference/`. Existing `docs/guides/` files are preserved in-place and cross-linked from persona pages. A `mkdocs.yml` configuration file is added at the repo root.

## Complexity Tracking

No constitution violations — no tracking needed.

---

## Phased Implementation Plan

### Phase 1: Site Infrastructure & Onboarding (Foundation)

**Goal**: Stand up the MkDocs site skeleton and per-persona onboarding pages so the site is navigable and buildable.

**Prerequisite**: None (greenfield)

| Task | File(s) | Source | Priority |
|------|---------|--------|----------|
| Create MkDocs configuration | `mkdocs.yml` | research.md R5 | P0 |
| Create landing page | `docs/index.md` | spec §1 | P0 |
| Create getting-started index | `docs/getting-started/index.md` | spec §3.2–§7.2 intro | P0 |
| Create ISSM getting-started | `docs/getting-started/issm.md` | spec §3.2 | P0 |
| Create ISSO getting-started | `docs/getting-started/isso.md` | spec §4.2 | P0 |
| Create SCA getting-started | `docs/getting-started/sca.md` | spec §5.2 | P0 |
| Create AO getting-started | `docs/getting-started/ao.md` | spec §6.2 | P0 |
| Create Engineer getting-started | `docs/getting-started/engineer.md` | spec §7.2 | P0 |
| Redirect existing getting-started.md | `docs/getting-started.md` | research.md R6 | P0 |

**Validation**: `mkdocs build --strict` passes; site serves locally with all getting-started pages navigable.

**Estimated Tasks**: 9

---

### Phase 2: Persona Guides (Core Content)

**Goal**: Create comprehensive per-persona guides with full RMF phase workflows, tool references, NL queries, and air-gapped callouts.

**Prerequisite**: Phase 1 complete (site is buildable)

| Task | File(s) | Source | Priority |
|------|---------|--------|----------|
| Create persona overview / RACI matrix | `docs/personas/index.md` | spec §2 | P1 |
| Create ISSO comprehensive guide | `docs/personas/isso.md` | spec §4 | P1 |
| Create Administrator guide | `docs/personas/administrator.md` | spec §8 | P1 |
| Enhance ISSM guide with air-gapped notes + cross-links | `docs/guides/issm-guide.md` | spec §3, research.md R2 | P1 |
| Enhance SCA guide with RBAC + evidence notes | `docs/guides/sca-guide.md` | spec §5, research.md R2 | P1 |
| Enhance AO guide with portfolio view | `docs/guides/ao-quick-reference.md` | spec §6, research.md R2 | P1 |
| Enhance Engineer guide with Kanban + IaC | `docs/guides/engineer-guide.md` | spec §7, research.md R2 | P1 |
| Enhance Compliance Watch with air-gapped notes | `docs/guides/compliance-watch.md` | spec §13.4, research.md R2 | P1 |

**Validation**: All persona pages render; each page conforms to `contracts/persona-guide.md` template. Cross-links resolve.

**Estimated Tasks**: 8

---

### Phase 3: RMF Phase Pages (Workflow Reference)

**Goal**: Create per-phase RMF workflow pages showing all persona responsibilities, gates, documents, and transitions.

**Prerequisite**: Phase 2 complete (persona guides exist for cross-linking)

| Task | File(s) | Source | Priority |
|------|---------|--------|----------|
| Create RMF overview | `docs/rmf-phases/index.md` | spec §10 | P1 |
| Create Prepare page | `docs/rmf-phases/prepare.md` | spec §3.4 Phase 0 | P1 |
| Create Categorize page | `docs/rmf-phases/categorize.md` | spec §3.4 Phase 1 | P1 |
| Create Select page | `docs/rmf-phases/select.md` | spec §3.4 Phase 2 | P1 |
| Create Implement page | `docs/rmf-phases/implement.md` | spec §3.4 Phase 3, §4.4, §7.4 | P1 |
| Create Assess page | `docs/rmf-phases/assess.md` | spec §5.5, §4.4 | P1 |
| Create Authorize page | `docs/rmf-phases/authorize.md` | spec §6.4 | P1 |
| Create Monitor page | `docs/rmf-phases/monitor.md` | spec §3.4 Phase 6, §4.4 | P1 |

**Validation**: All phase pages render; each conforms to `contracts/rmf-phase.md` template. Previous/Next links resolve. Gate conditions reference real tools.

**Estimated Tasks**: 8

---

### Phase 4: Guides & Reference Pages (Depth Content)

**Goal**: Create reference material — tool inventory, NL query catalog, document catalog, portfolio management, troubleshooting, and quick reference cards.

**Prerequisite**: Phase 2 complete (persona guides exist for cross-linking from reference pages)

| Task | File(s) | Source | Priority |
|------|---------|--------|----------|
| Create NL Query Reference | `docs/guides/nl-query-reference.md` | spec §12 | P1 |
| Create Document Catalog | `docs/guides/document-catalog.md` | spec §11 | P1 |
| Create Portfolio Management guide | `docs/guides/portfolio-management.md` | spec §9.4 | P1 |
| Create Tool Inventory (114 tools) | `docs/reference/tool-inventory.md` | spec Appendix A | P1 |
| Create Troubleshooting guide | `docs/reference/troubleshooting.md` | spec Appendix F | P1 |
| Create Quick Reference Cards | `docs/reference/quick-reference-cards.md` | spec Appendix D | P2 |
| Expand Glossary with new terms | `docs/reference/glossary.md` | spec Appendix C, research.md R2 | P2 |

**Validation**: All reference pages render; tool names match codebase identifiers; troubleshooting covers 25+ scenarios. Pages conform to `contracts/reference.md`.

**Estimated Tasks**: 7

---

### Phase 5: Scenarios, Polish & Validation (Final)

**Goal**: Create end-to-end scenario, polish all cross-links, run full validation.

**Prerequisite**: Phases 1–4 complete

| Task | File(s) | Source | Priority |
|------|---------|--------|----------|
| Create full lifecycle scenario | `docs/scenarios/full-lifecycle.md` | spec Appendix B | P2 |
| Validate all internal cross-links | All files | data-model.md Cross-Reference Map | P0 |
| Run `mkdocs build --strict` and fix warnings | All files | quickstart.md | P0 |
| Review all air-gapped callouts for consistency | Persona & RMF phase pages | spec clarification Q3 | P1 |
| Verify tool name accuracy against codebase | Tool Inventory, persona guides | ComplianceMcpTools.cs | P1 |

**Validation**: `mkdocs build --strict` produces zero warnings. All pages render correctly. Site is self-contained for `file://` browsing.

**Estimated Tasks**: 5

---

## Phase Summary

| Phase | Focus | Pages | Tasks | Priority |
|-------|-------|-------|-------|----------|
| 1 | Infrastructure & Onboarding | 9 | 9 | P0 |
| 2 | Persona Guides | 8 | 8 | P1 |
| 3 | RMF Phase Pages | 8 | 8 | P1 |
| 4 | Guides & Reference | 7 | 7 | P1–P2 |
| 5 | Scenarios & Validation | 1 + all | 5 | P0–P2 |
| **Total** | | **33** | **37** | |

## Constitution Re-Check (Post-Design)

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Documentation as Source of Truth | **PASS** | All documentation lives in `docs/`, versioned in Git, single source of truth. |
| III. Testing Standards | **PASS** | `mkdocs build --strict` validates all links and structure. Phase 5 includes explicit validation tasks. |
| IV. Azure Government & Compliance First | **PASS** | Air-gapped callouts present in 4+ pages. `use_directory_urls: false` enables offline `file://` browsing. Self-contained `site/` bundle. |
| VI. Code Quality & Maintainability | **PASS** | 4 page templates in `contracts/` enforce consistency. Cross-reference map ensures discoverability. |
| VII. User Experience Consistency | **PASS** | Consistent navigation via MkDocs nav. Getting-started for each persona. Quick reference cards for at-a-glance lookup. |

**Gate Result**: ✅ **PASS** — No violations after design phase. Proceed to task generation.
