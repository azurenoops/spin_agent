<!--
  SYNC IMPACT REPORT
  ==================
  Version change: 1.1.0 → 2.0.0 (MAJOR)
  Bump rationale: §III "Testing Standards" is redefined as §VI
  "Test-Driven Development (NON-NEGOTIABLE)" requiring failing-test-
  first (Red-Green-Refactor) and AAA structure — backward-incompatible
  with the previous "tests accompany behavior changes" posture.
  Several principles are renumbered. New foundational principles
  (Simplicity, YAGNI, SRP) and new sections (Security, DevOps) are
  introduced.

  Renumbered / redefined principles:
    - III. Testing Standards → VI. Test-Driven Development
      (NON-NEGOTIABLE) — redefined: failing test first, AAA mandatory,
      Test Data Separation enforced.
    - IV. Azure Government & Compliance First → moved to "Azure
      Government & Compliance Requirements" section (was always
      partly a section; now consolidated).
    - V. Observability & Structured Logging → VII. Observability &
      Structured Logging (renumbered).
    - VI. Code Quality & Maintainability → "Code Quality Standards"
      section (demoted from principle to standards section; content
      preserved and expanded).
    - VII. User Experience Consistency → "User Experience Standards"
      section (demoted; content preserved).
    - VIII. Performance Requirements → "Performance Standards"
      section (demoted; content preserved).

  Added principles:
    - II. Simplicity
    - III. YAGNI (You Aren't Gonna Need It)
    - IV. Single Responsibility Principle

  Added sections:
    - "Security" — TLS floor, token handling, input validation,
      Zero-Trust Architecture (5 sub-rules), SFI compliance.
    - "DevOps" — Infrastructure as Code, Reproducible Deployments,
      CI/CD Pipeline, Coverage Gate, Environment Parity, Rollback,
      CI/CD Zero Warnings, GitHub Issue Discipline (NON-NEGOTIABLE).
    - "Local Type-Checking Parity" — added under Development
      Workflow & Quality Gates (NON-NEGOTIABLE).
    - "Complexity Justification" — added under Governance.

  Removed sections: None.

  Templates requiring updates:
    ✅ .specify/templates/plan-template.md — No changes needed.
       Constitution Check is dynamically populated; Technical Context
       already includes Performance Goals / Constraints; Complexity
       Tracking table already exists at line 97 and now has explicit
       backing in Governance § Complexity Justification.
    ✅ .specify/templates/spec-template.md — No changes needed.
       Success Criteria already accommodate performance, UX, and
       security metrics.
    ✅ .specify/templates/tasks-template.md — No changes needed.
       Test phases align with redefined TDD principle (tests-before-
       implementation already mandated by template structure).
    ✅ .specify/templates/checklist-template.md — No changes needed.
    ✅ .specify/templates/agent-file-template.md — No changes needed.

  Follow-up TODOs:
    - Audit existing 048 features against §VI TDD: many predate
      Red-Green-Refactor enforcement and may need regression-test
      backfill before next MAJOR release.
    - Wire CI gate for Local Type-Checking Parity (`dotnet build`,
      `tsc --noEmit` for extensions/vscode, extensions/m365,
      Ato.Copilot.Dashboard).
    - Add GitHub Issue sync validator script per §DevOps GitHub
      Issue Discipline.
-->

# ATO Copilot Constitution

## Core Principles

### I. Documentation as Source of Truth

All design and code changes MUST follow guidance documented under `/docs/`. If guidance conflicts
with "general best practices," repo guidance wins. Missing guidance MUST NOT be invented; instead,
propose an ADR or new document in `/docs/standards`.

**Rationale**: Ensures consistency across contributors and AI-assisted development. Every
recommendation MUST cite the doc path + section heading. If no rule exists, state so and suggest
where it should live.

### II. Simplicity

- Every solution MUST prefer the most straightforward approach that satisfies the requirement.
- New abstractions, layers, or indirection MUST be justified by a concrete, present-day need —
  not a hypothetical future one.
- When two designs solve the same problem, the one with fewer moving parts MUST be chosen
  unless measurable evidence demonstrates the simpler option is insufficient.
- Any deviation from this principle MUST be recorded in the plan's Complexity Tracking table
  with a rejected simpler alternative (see Governance § Complexity Justification).

**Rationale**: ATO Copilot ships 130+ MCP tools across 48+ features. Unnecessary complexity
compounds maintenance burden, slows onboarding, and obscures compliance-critical logic.

### III. YAGNI (You Aren't Gonna Need It)

- Features, configuration options, and extension points MUST NOT be built until they are
  explicitly required by a current user story or specification under `specs/NNN-feature-name/`.
- Speculative generalization (e.g., plugin systems beyond what `BaseAgent`/`BaseTool` already
  provide, multi-cloud abstractions beyond AzureGov + AzureCloud, provider-agnostic AI
  wrappers) is prohibited unless a specification demands it.
- Code that exists without a covering requirement MUST be removed or justified in a plan
  document.

**Rationale**: Premature features create dead code, widen the test surface, and obscure the
intent of compliance-critical software where every line is auditable.

### IV. Single Responsibility Principle

- Every function, class, and module MUST have one reason to change. If a unit does two things,
  split it.
- Helper functions that combine "fetch the data" and "decide what to do with it" MUST be
  separated so each half is independently testable.
- When a function is hard to test, that is a design signal — extract the untestable part into
  a unit that CAN be tested.
- No method SHOULD exceed 50 lines of logic (excluding braces and blank lines). Methods
  exceeding this limit MUST be refactored or justified in a code-review comment.

**Rationale**: SRP keeps units small, testable, and composable. Violations surface as
untestable branches, mock complexity, and tests that break for unrelated reasons — all of
which are unacceptable in a compliance platform.

### V. BaseAgent/BaseTool Architecture

All agents MUST extend `BaseAgent`. All tools MUST extend `BaseTool`. This pattern is
NON-NEGOTIABLE and applies to every feature touching the agent layer.

- Agents MUST implement: `AgentId`, `AgentName`, `Description`, `GetSystemPrompt()`
- Tools MUST implement: `Name`, `Description`, `Parameters`, `ExecuteAsync()`
- System prompts MUST be externalized in `*.prompt.txt` files
- Tools MUST be registered via `RegisterTool()` in agent constructors

**Rationale**: Standardized abstractions enable multi-agent orchestration, consistent function
calling, and maintainable code across the MCP server, VS Code extension, M365 Teams bot, and
Web Chat client.

### VI. Test-Driven Development (NON-NEGOTIABLE)

All new functionality MUST follow the Red-Green-Refactor cycle: write a failing test,
implement the minimum code to pass, then refactor.

- **Failing Test First**: Tests MUST be written and confirmed to fail before any production
  code is written for that behavior. Pull requests that introduce production code without a
  prior failing-test commit (or co-located commit pair) MUST be rejected.
- **Acceptance Coverage**: Every user story MUST have at least one acceptance-level test that
  can be executed independently against `Tests.Integration/` or `Tests.Manual/`.
- **Refactoring Discipline**: Refactoring MUST NOT change externally observable behavior; the
  existing test suite MUST continue to pass.
- **Coverage Target**: Combined unit + integration coverage MUST reach 100% on modified code
  paths. Lowering this threshold requires a constitution amendment (MAJOR version bump).
- **Arrange-Act-Assert (AAA)**: All tests MUST follow the AAA pattern. Each test method MUST
  contain three clearly separated sections marked with `// Arrange`, `// Act`, and `// Assert`
  comments. Sections MAY be omitted only when they would be genuinely empty (e.g., no
  arrangement needed for a static helper with no dependencies); remaining sections MUST still
  be commented.
- **Test Data Separation (NON-NEGOTIABLE)**: Automated tests (unit, integration, CI) MUST use
  mock or synthetic data exclusively — never live/production data and never real
  PII/CUI/classified content. Manual user testing MAY use sanitized live data via
  `Tests.Manual/` only. Test suites MUST NOT depend on network connectivity to Azure, Graph,
  or external services unless explicitly mocked via WireMock or equivalent.
- **Boundary & Edge-Case Coverage**: All numeric inputs, collection parameters, and nullable
  fields MUST have boundary-value tests (empty, null, max-length, overflow).
- **Flaky-Test Policy**: A test that fails intermittently MUST be quarantined within 24 hours,
  root-caused within one sprint, and either fixed or removed.
- **Regression Tests**: Every bug fix MUST include a regression test that reproduces the
  original defect before verifying the fix.

Build/test discipline: Every change proposal MUST include exact `dotnet build Ato.Copilot.sln`
and `dotnet test Ato.Copilot.sln` commands with expected outcomes.

**Rationale**: TDD produces verifiable, regression-resistant code and ensures every feature
is exercised by automated tests. Test Data Separation is mandatory because ATO Copilot
processes DoD compliance artifacts; a test suite that pulls real ATO data is a data-spill
risk, not a quality gate.

### VII. Observability & Structured Logging

All services MUST implement structured logging with Serilog. Logging requirements:

- Console + file sinks MUST be configured for development
- Application Insights sink MUST be configured for production
- Tool executions MUST log: input parameters (redacted for sensitive fields), execution
  duration, success/failure
- Agent invocations MUST log: selected agent, tool chain, termination reason
- Logging MUST NOT introduce measurable performance degradation (>5% latency impact at p95)

**Rationale**: Compliance operations require full traceability. Logs enable auditing
tool execution chains and compliance assessment history under FedRAMP and DoD audit regimes.

## Code Quality Standards

All code MUST meet the following quality standards to ensure long-term maintainability of
compliance-critical software.

- **Dependency Injection**: All dependencies MUST be injected via constructor injection. The
  service-locator pattern is prohibited. `IServiceProvider` MUST NOT be passed as a
  constructor parameter except in composition roots.
- **XML Documentation**: All public types and members MUST have XML documentation comments
  (`<summary>`, `<param>`, `<returns>`). Internal types SHOULD have documentation when their
  purpose is non-obvious.
- **No Magic Values**: Magic numbers and string literals MUST be extracted to named constants,
  enums, or configuration values. Exception: `0`, `1`, `-1`, `string.Empty`, and `null` in
  idiomatic C# patterns.
- **Code Duplication**: Duplicated logic spanning 5+ lines MUST be extracted into a shared
  method or abstraction. Copy-paste code MUST be flagged during review.
- **Warnings-as-Errors**: `dotnet build` MUST produce zero warnings in modified files. New
  `#pragma warning disable` directives MUST include a justification comment.
- **Naming Conventions**: Follow .NET naming guidelines — `PascalCase` for public members,
  `_camelCase` for private fields, `I`-prefix for interfaces. Acronyms of 3+ characters
  MUST use PascalCase (e.g., `McpServer`, not `MCPServer`).

## User Experience Standards

All user-facing interfaces (MCP tools, HTTP endpoints, CLI output, Dashboard, VS Code
extension, M365 Teams bot, Web Chat) MUST deliver a predictable, accessible experience across
interaction modes.

- **Consistent Response Schema**: All MCP tool responses MUST follow a uniform envelope
  structure containing: `status` (success/error), `data` (result payload), `metadata`
  (execution time, tool name, timestamp).
- **Actionable Error Messages**: Error responses MUST include: a human-readable `message`,
  a machine-readable `errorCode`, and a `suggestion` field with corrective guidance. Stack
  traces MUST NOT be exposed to end users in production.
- **Mode Parity**: Stdio and HTTP modes MUST produce functionally equivalent results for
  identical inputs. Any mode-specific behavior MUST be documented in `/docs/`.
- **Compliance Context**: All compliance-related output MUST include the relevant control
  family identifier, framework reference (e.g., NIST 800-53 Rev 5), and assessment scope.
- **Progress Feedback**: Operations exceeding 2 seconds MUST provide progress indicators
  (streaming updates in stdio mode, polling status or SignalR in HTTP/Dashboard mode).
- **Accessibility**: All documentation, error messages, and user guidance MUST use plain
  language. Jargon MUST be accompanied by a brief definition on first use within a session.

## Performance Standards

All components MUST meet baseline performance targets to ensure responsiveness during
time-sensitive ATO and compliance workflows.

- **MCP Tool Response Time**: Simple queries (status, control lookup, audit log retrieval)
  MUST complete within 5 seconds. Complex operations (full compliance assessment, document
  generation) MUST complete within 30 seconds for a single-subscription scope.
- **HTTP Endpoint Latency**: Health and status endpoints MUST respond within 200ms (p95).
  Tool invocation endpoints MUST respond within the tool-specific time limits above.
- **Memory Budget**: Steady-state memory consumption MUST remain under 512MB for standard
  operations. Bulk operations (multi-subscription scans, large document generation) MUST
  NOT exceed 1GB.
- **Bounded Result Sets**: All database queries and API responses returning collections
  MUST support pagination. Unbounded `SELECT *` queries are prohibited. Default page size
  MUST be configurable (default: 50 items).
- **Cancellation Support**: All async operations MUST accept and honor `CancellationToken`.
  Long-running operations MUST check for cancellation at meaningful intervals.
- **Startup Time**: The MCP server MUST be ready to accept requests within 10 seconds of
  process start in both stdio and HTTP modes.

## Azure Government & Compliance Requirements

All code interacting with Azure services MUST adhere to:

| Requirement | Standard |
|-------------|----------|
| Authentication | Managed Identity (prod), Azure CLI (dev) via `DefaultAzureCredential` |
| Cloud Targets | `AzureUSGovernment` (primary), `AzureCloud` (secondary) |
| Secrets | Azure Key Vault only; no hardcoded values |
| Networking | Private endpoints preferred; firewall rules documented |
| Compliance | NIST 800-53 Rev 5 control mapping for security-relevant features |
| Data Residency | US Government regions only (usgovvirginia, usgovarizona, usgovtexas) |

Infrastructure code (Bicep/Terraform) MUST include compliance annotations and policy
assignments.

## Security

- **Transport Encryption**: All communication between clients (Web Chat, Dashboard, VS Code,
  M365 Teams) and the server MUST use TLS 1.2 or higher. Plain-text HTTP endpoints MUST NOT
  be exposed.
- **Authentication**: Every API request from a client MUST be authenticated. Anonymous access
  to server-side endpoints is prohibited unless explicitly scoped in a specification (e.g.,
  health probes).
- **Authorization**: The server MUST enforce authorization checks on every request. Clients
  MUST NOT be trusted to self-authorize. Tenant isolation (per feature 048) MUST be enforced
  server-side on every query path.
- **Secrets Management**: API keys, tokens, and credentials MUST NOT be stored in source
  code, client bundles, container images, or `appsettings.*.json` committed to the repo.
  Secrets MUST be managed through Azure Key Vault or environment variables sourced from a
  secure store.
- **Token Handling**: Authentication tokens MUST have a bounded lifetime and MUST be
  refreshed or reissued before expiry. Long-lived static tokens are prohibited. CAC/PIM
  session tokens (feature 003) MUST follow this rule strictly.
- **Input Validation**: The server MUST validate and sanitize all input received from
  clients. Client-side validation alone is insufficient. All MCP tool parameters MUST be
  validated against their declared schema before execution.
- **Zero-Trust Architecture (NON-NEGOTIABLE)**: The implementation MUST follow zero-trust
  principles:
  - **Never Trust, Always Verify**: Every request MUST be authenticated and authorized
    regardless of its origin, including requests from internal services and components.
  - **Least Privilege**: Each component, service, and user MUST be granted only the minimum
    permissions required for its function. Over-scoped roles are prohibited.
  - **Assume Breach**: The system MUST be designed so that compromise of any single
    component does not grant access to the entire system. Segment trust boundaries between
    services (MCP server, Chat, Dashboard, extensions).
  - **Explicit Verification**: Network location (e.g., being on the same VNET or subnet)
    MUST NOT be treated as proof of trust. Identity-based verification is required at every
    boundary.
  - **No Implicit Trust Between Tiers**: The server MUST NOT trust client applications, and
    internal services (MCP, Chat, Channels, State) MUST NOT trust each other without
    explicit, per-request credential verification.
- **Microsoft Secure Future Initiative (SFI) Compliance**: When deployed to Microsoft-internal
  Azure subscriptions, infrastructure MUST comply with SFI policies enforced on the
  subscription. SFI policies are externally managed and cannot be overridden — infrastructure
  configurations MUST be designed to work with these policies, not against them. Specifically:
  - Storage accounts MUST assume `publicNetworkAccess: Disabled` and
    `allowSharedKeyAccess: false` as SFI-enforced defaults. CD pipelines MAY temporarily
    toggle `publicNetworkAccess` to `Enabled` (with `defaultAction: Deny`) for deployment,
    and MUST restore `Disabled` in always-run cleanup steps.
  - Key Vaults MUST assume `publicNetworkAccess: Disabled` as the SFI-enforced default. The
    same temporary toggle pattern applies for CD pipeline access.
  - Private endpoints MUST be used for runtime service-to-service communication. Public
    network access toggles are permitted only for CI/CD operations and MUST be restored to
    Disabled after use.
  - Bicep/Terraform resource configurations MUST align with SFI-enforced settings to prevent
    state drift.

**Rationale**: ATO Copilot processes DoD compliance artifacts that may include FOUO, CUI,
and ATO-relevant evidence. Zero-trust ensures that no component is implicitly trusted,
limiting blast radius in the event of a compromise.

## Development Workflow & Quality Gates

### Required Output Format

For any change proposal, output:

1. **Guidance Compliance Report**: PASS/FAIL with rule-by-rule citations
2. **Architecture Decision**: If architecture/design is impacted, document rationale
3. **Code Changes**: Files changed + why + build/test commands + rollback procedure

### Quality Gates

| Gate | Requirement |
|------|-------------|
| Build | `dotnet build Ato.Copilot.sln` MUST pass with zero warnings |
| Unit Tests | `dotnet test` MUST pass with 80%+ coverage on modified paths |
| Linting | No new warnings in modified files |
| Type Checking | All static type checkers (see Local Type-Checking Parity) MUST pass |
| Performance | New endpoints MUST include response-time assertions |
| UX Consistency | Tool responses MUST conform to standard envelope schema |
| Documentation | New features MUST update relevant `/docs/*.md` and `specs/NNN-*` |

### Local Type-Checking Parity (NON-NEGOTIABLE)

For every language in the project that has a static type checker, the project MUST provide a
local command that runs the same type-checking step that CI enforces. Type errors MUST be
catchable locally before pushing — developers MUST NOT have to wait for CI to discover
type-checking failures. Specifically:

- **C# (.NET 9)**: `dotnet build Ato.Copilot.sln` MUST be runnable locally and is the
  authoritative type checker.
- **TypeScript (extensions/vscode, extensions/m365, src/Ato.Copilot.Dashboard, src/Ato.Copilot.Chat
  client)**: Each project MUST expose `npm run typecheck` (or equivalent) that runs
  `tsc --noEmit` independently of test execution.
- **Python (docs tooling, scripts/)**: If a Python module declares type hints, `mypy` or
  `pyright` MUST be runnable locally.
- Test runners that skip type checking (e.g., Vitest with esbuild, pytest) do NOT satisfy
  this requirement — the actual type checker MUST be invokable separately.
- The local type-check command MUST be documented in the component's `package.json` scripts,
  `Makefile`, or equivalent task runner.

**Rationale**: Test runners often use fast transpilers (esbuild, Babel) that strip types
without checking them. If CI runs a strict type checker but local development only runs
tests, type errors become invisible until push — wasting CI cycles and developer time.

### Branch Strategy

- Feature branches: `NNN-feature-name` format (e.g., `048-tenant-isolation`)
- All changes via pull request with minimum 1 reviewer
- CI pipeline MUST pass before merge
- Commits follow Conventional Commits (`feat:`, `fix:`, `docs:`, `test:`, `refactor:`, `chore:`)

## DevOps

- **Infrastructure as Code**: All cloud infrastructure, platform configuration, and
  environment setup MUST be defined in version-controlled IaC templates (Bicep or Terraform).
  Manual creation of cloud resources via portal or ad-hoc CLI for production environments is
  prohibited.
- **Reproducible Deployments**: Every deployment MUST be reproducible from the repository
  alone. Given the same commit and configuration, deploying to a fresh environment MUST
  produce an identical result.
- **Minimize Manual Steps**: Manual deployment steps MUST be minimized. Any remaining manual
  step (e.g., one-time secret seeding, DNS delegation, CAC enrollment) MUST be documented in
  the quickstart or runbook with exact commands.
- **CI/CD Pipeline**: A CI/CD pipeline MUST build, test, and deploy every change that
  reaches the main branch. The pipeline MUST enforce the full test suite gate (Principle VI)
  before deployment proceeds.
- **CI Coverage Gate**: The CI pipeline MUST enforce the 80% coverage threshold defined in
  Principle VI as a hard gate. The pipeline MUST fail the build if coverage on modified
  paths falls below the threshold. Lowering this threshold requires a constitution amendment.
- **CI Test Coverage**: All tests across all components (`Tests.Unit`, `Tests.Integration`,
  extensions) MUST be executed during branch CI on every push. Smoke tests MUST be run
  against staging after each deployment. No test suite may exist in the repository without a
  corresponding CI job that executes it.
- **Environment Parity (NON-NEGOTIABLE)**: Staging and production environments MUST be
  identical in architecture, security posture, network topology, and configuration —
  differing only in parameter values (names, scale, SKUs). If a security control, network
  rule, or architectural pattern exists in production, it MUST exist in staging. A change
  that cannot be validated in staging MUST NOT be promoted to production.
- **Rollback Capability**: Every deployment MUST support rollback to the previous version.
  Container-based deployments MUST use immutable, tagged images — `:latest` tags are
  prohibited in production.
- **Local Deployment Automation**: Local deployments (e.g., `docker-compose.mcp.yml`,
  `scripts/bootstrap.sh`) MUST include scripted setup that builds, configures, and starts
  all services with minimal manual interaction. The operator MUST only need to provide
  environment-specific values via a configuration file or environment variables; all other
  steps MUST be automated.
- **CI/CD Zero Warnings**: All errors and warnings reported by GitHub Actions during push and
  pull-request workflows MUST be analyzed and resolved. Each CI/CD run MUST complete with
  zero warnings and zero errors. Persistent warnings that cannot be fixed MUST be suppressed
  with an inline justification comment explaining why.
- **GitHub Issue Discipline (NON-NEGOTIABLE)**:
  - **Traceability**: Every User Story issue in GitHub MUST be a sub-issue of its parent
    Feature issue. Clean traceability from Feature → User Story MUST be maintained at all
    times via GitHub's sub-issue relationships. Issues MUST NOT exist without proper parent
    linkage.
  - **Synchronization**: Every Feature and User Story defined in spec-kit documents
    (`specs/NNN-*/spec.md`, `plan.md`) MUST have a corresponding GitHub issue. GitHub issues
    MUST be kept in sync with spec-kit documents — when an issue is added, updated, or
    completed, the corresponding spec-kit document MUST be updated accordingly. GitHub
    issues are the source of truth; spec-kit documents reflect that truth so AI agents
    (Copilot, Cursor, Claude Code) can function effectively.
  - **Task Tracking**: Tasks defined in `tasks.md` do not require individual GitHub issues.
    Tasks MUST be reflected as checklist items in their parent User Story issue body. Task
    completion is tracked in both `tasks.md` and the User Story issue checklist.

**Rationale**: Infrastructure as code ensures auditability, reproducibility, and eliminates
configuration drift. GitHub Issue Discipline ensures the spec-kit workflow stays coherent
across human contributors and AI agents.

## Governance

This constitution supersedes all other development practices for the ATO Copilot project.
When a conflict arises, the constitution is authoritative.

### Amendments

Any change to this constitution MUST be documented with a version bump, rationale, and
updated date. Amendments follow semantic versioning:

- **MAJOR**: Principle removal or backward-incompatible redefinition.
- **MINOR**: New principle or section added, or material expansion.
- **PATCH**: Clarifications, wording fixes, non-semantic refinements.

Amendment process:

1. **Proposal**: Open issue or PR with proposed change and rationale.
2. **Review**: Minimum 1 maintainer approval required.
3. **Migration**: Breaking changes MUST include a migration plan.
4. **Sync Impact Report**: Every amendment MUST update the Sync Impact Report comment at the
   top of this file and verify which `.specify/templates/*` files require updates.

### Compliance Review

Every plan and implementation MUST include a Constitution Check gate verifying alignment with
these principles. PRs and code reviews MUST verify compliance.

### Complexity Justification

Any deviation from Principle II (Simplicity) or Principle III (YAGNI) MUST be documented in
the plan's Complexity Tracking table (`plan.md`) with:

- The specific principle being deviated from
- The concrete, present-day need driving the deviation
- The simpler alternative that was rejected, and why

Plans without a populated Complexity Tracking table for known deviations MUST NOT pass the
Constitution Check gate.

### Technical Decision-Making Framework

When making implementation choices, principles MUST be applied in the following priority
order:

1. **Compliance & Security** (Principles I, V; Azure Gov & Security sections): Government
   compliance requirements and documented guidance always take precedence. If a performance
   optimization or code-quality improvement conflicts with compliance, compliance wins.
2. **Correctness & Testing** (Principle VI): All code MUST be provably correct through tests
   before optimizations are considered. Untested performance improvements are rejected.
3. **Simplicity & YAGNI** (Principles II, III): Prefer the simpler design unless a higher-
   priority principle demands otherwise.
4. **User Experience** (UX Standards section): Consistent UX MUST NOT be sacrificed for
   internal code elegance or marginal performance gains.
5. **Performance** (Performance Standards section): Performance targets MUST be met, but MUST
   NOT justify skipping tests, violating architecture patterns, or degrading UX.
6. **Code Quality** (Principle IV; Code Quality Standards section): Clean code and
   architecture patterns are mandatory but MUST yield to higher-priority principles when
   genuine trade-offs arise.
7. **Observability** (Principle VII): Logging and tracing MUST be included in every feature
   but MUST NOT introduce measurable performance degradation (>5% latency impact at p95).

When trade-offs arise between principles at the same priority level, the decision MUST be
documented as an Architecture Decision Record (ADR) in `/docs/standards/` and cited in the
pull request description.

**Version**: 2.0.0 | **Ratified**: 2025-01-01 | **Last Amended**: 2026-05-08
