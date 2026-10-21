# eMASS package audit agent

Created: 2026-10-07

## Purpose and decision

User request: create a reusable agent that understands RMF, cloud-provider duties,
and mission-owner capabilities and audits SPIN's components, services, and data
flows against the goal of exporting a complete eMASS package.

Keep the existing RMF advisor unchanged. Introduce `emass-package-auditor` as an
evidence-driven architecture and package-lineage reviewer. It produces analysis
and recommendations, not automatic application changes or authorization decisions.

The project prompt is `.cursor/agents/emass-package-auditor.md`. A matching body
is provided in `.github/agents/emass-package-auditor.agent.md` for the repository's
existing agent catalog. These are prompt definitions, not a runtime SPIN BaseAgent
or proof of native discovery by every coding client.

## Audit contract

Trace user workflow → API/MCP contract → domain service → persisted/versioned
record → human review → generated artifact → export mapping and validation.
Evaluate provider-published scope separately from mission applicability,
capability adoption, customer/shared duties, and accepted inheritance.

Establish the target eMASS instance, supported format and required artifact profile
from authoritative references before claiming completeness. Unknown requirements
remain explicit. Inspect export payloads and evidence manifests, not just UI saves.

Default output: audit scope and revision; evidence-based architecture matrix;
RMF/responsibility matrix; package lineage; prioritized gaps; acceptance checks;
and a sequenced recommendation backlog. Planned specs and mocks are not evidence
of executed functionality. No coverage percentage without a justified denominator.

## Use

Ask an agent-capable client to use `emass-package-auditor`, or give Codex the
project prompt file explicitly:

> Read `.cursor/agents/emass-package-auditor.md` and follow it to audit SPIN's
> provider-to-mission capability flow and eMASS package export. Return verified
> findings and a prioritized recommendation backlog. Do not modify application code.

Start with a bounded audit slice if a full export fixture, instance requirements,
or execution environment is unavailable. State limits rather than asserting
missing functionality from a failed search. The user can manually run each
recommended acceptance check locally.

## Validation and rollback

Validate frontmatter, unique names, referenced repository paths, and matching
prompt bodies; exercise a bounded read-only audit. No runtime files, dependencies,
or migrations change. Remove the two agent definitions and this design record to
undo the addition; remove its tooling note from Feature 079 as well.
