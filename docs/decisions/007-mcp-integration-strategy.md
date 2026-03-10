# ADR-007: MCP Integration Strategy

**Status:** accepted

**Date:** 2026-03-03

## Context

Teamwork defines 8 core roles (plus 7 extended roles) that AI agents fill during development workflows. Each role has distinct responsibilities — the Security Auditor scans for vulnerabilities, the Coder writes and tests code, the Documenter maintains docs, and so on. Today, agents filling these roles rely solely on the tools their host environment provides (file editing, terminal commands, web search). They have no structured way to access specialized external tools tailored to their role.

The Model Context Protocol (MCP) is a standard for connecting AI agents to external tool servers. MCP servers expose domain-specific capabilities (GitHub operations, static analysis, documentation lookup, web search, sandboxed execution, vulnerability databases, diagram generation, infrastructure management) that agents can invoke during their work. A growing ecosystem of actively maintained MCP servers exists for software development use cases.

Phase 4 of the Teamwork roadmap adds MCP server integration so that:

1. Projects can declare which MCP servers are available for agent use.
2. Each role can specify which MCP servers benefit its work and which tools to call.
3. Instruction files (`.github/copilot-instructions.md`) surface MCP guidance so agents discover available servers.
4. The `teamwork` CLI can list, generate, and validate MCP configuration.

Key constraints:

- MCP server configuration must live in `.teamwork/config.yaml` alongside existing project settings (model tiers, roles, workflows). Adding a separate config file creates fragmentation.
- Agent files in `.github/agents/*.agent.md` are framework files managed by `teamwork install`/`teamwork update` (ADR-005). Role-to-MCP-server mappings should be part of these files so they update with the framework.
- Different AI clients (Claude Desktop, VS Code, GitHub Copilot) each have their own MCP configuration format. The `teamwork mcp config` command generates paste-ready JSON for each client from the single config source.
- MCP servers require different runtimes (Node.js, Python, Docker). Teamwork should recommend servers but cannot guarantee runtime availability.

## Decision

We will add MCP server support to Teamwork across four layers: config schema, agent file references, instruction file surfacing, and CLI commands.

### 1. Server Selection Rationale

We selected 8 MCP servers that cover the core capabilities needed by Teamwork's roles. Selection criteria:

- **Actively maintained** — Regular commits, responsive maintainers, not abandoned
- **Widely adopted** — Significant community usage, not experimental
- **Free tier available** — Usable without paid subscriptions for basic workflows
- **Role-relevant** — Each server maps to specific role responsibilities
- **No overlap** — Each server fills a distinct capability gap

The 8 selected servers:

| Server | Package | What It Provides |
|--------|---------|------------------|
| GitHub MCP | `github/github-mcp-server` | GitHub repos, PRs, issues, CI workflows, Dependabot alerts |
| Context7 | `upstash/context7` | Real-time library documentation — prevents API hallucination |
| Semgrep | `semgrep/mcp` | SAST security scanning — 5000+ rules across 30+ languages |
| Tavily | `tavily-ai/tavily-mcp` | Web search and content extraction for research tasks |
| E2B | `e2b-dev/e2b-mcp` | Cloud-sandboxed Python and JavaScript code execution |
| OSV | `StacklokLabs/osv-mcp` | Open Source Vulnerability database — CVEs by package and version |
| Mermaid | `Narasimhaponnada/mermaid-mcp` | Architecture and flow diagram generation from text descriptions |
| Terraform | `hashicorp/terraform-mcp-server` | Terraform Registry lookups, provider docs, module search, HCP workspace management |

**What was not chosen and why:**

| Server | Why Not Selected |
|--------|------------------|
| Playwright (`microsoft/playwright-mcp`) | Browser-specific, not universal enough for core set. Useful for web app testing but most Teamwork projects don't require browser automation. See nice-to-have issue #30. |
| Sentry / Datadog | Paid services, project-specific APM configuration |
| Atlassian / Linear | Not universal — teams use different project management tools |
| SonarQube | Requires running server instance, complex setup |

**Nice-to-have custom servers** for future consideration are tracked in issues #30–#34.

### 2. Role-to-Server Mapping

Each role benefits from a specific subset of MCP servers. This mapping drives the `## MCP Tools` sections in agent files and the `--role` filter in `teamwork mcp list`.

| Role | GitHub | Context7 | Semgrep | Tavily | E2B | OSV | Mermaid | Terraform |
|------|--------|----------|---------|--------|-----|-----|---------|-----------|
| Planner | ✓ | — | — | ✓ | — | — | — | — |
| Architect | ✓ | ✓ | — | ✓ | — | — | ✓ | ✓ |
| Coder | ✓ | ✓ | ✓ | — | ✓ | — | — | — |
| Tester | ✓ | — | — | — | ✓ | — | — | — |
| Reviewer | ✓ | — | ✓ | — | — | ✓ | — | — |
| Security Auditor | ✓ | — | ✓ | ✓ | — | ✓ | — | — |
| Documenter | ✓ | ✓ | — | — | — | — | ✓ | — |
| Orchestrator | ✓ | — | — | — | — | — | — | — |
| Triager | ✓ | — | — | — | — | — | — | — |
| DevOps | ✓ | — | — | — | — | — | — | ✓ |
| Dependency Manager | ✓ | — | — | — | — | ✓ | — | — |
| Refactorer | ✓ | — | ✓ | — | — | — | — | — |
| Lint Agent | ✓ | — | ✓ | — | — | — | — | — |
| API Agent | ✓ | ✓ | — | — | — | — | — | — |
| DBA Agent | ✓ | ✓ | — | — | — | — | — | — |

### 3. Config Schema Design

Add a top-level `mcp_servers` key to `.teamwork/config.yaml`. Each entry defines a named MCP server with a simplified schema optimized for readability:

```yaml
# MCP server definitions
# Agents check this section to know which MCP tools are available.
# Install only the servers your workflow needs.
mcp_servers:
  github:
    description: "GitHub repos, PRs, issues, CI workflows, Dependabot alerts"
    url: "https://api.githubcopilot.com/mcp/"
    roles: [planner, architect, coder, tester, reviewer, security-auditor, documenter, orchestrator]
    env_vars: [GH_TOKEN]
    install: "gh extension install github/gh-mcp"

  context7:
    description: "Real-time library documentation — prevents API hallucination"
    url: "https://mcp.context7.com/mcp"
    roles: [architect, coder, documenter]
    env_vars: []
    install: "npx -y @upstash/context7-mcp"

  semgrep:
    description: "SAST security scanning — 5000+ rules across 30+ languages"
    command: "uvx semgrep-mcp"
    roles: [security-auditor, reviewer, coder]
    env_vars: [SEMGREP_APP_TOKEN]
    install: "pip install semgrep-mcp"

  tavily:
    description: "Web search and content extraction for research tasks"
    url: "https://mcp.tavily.com/mcp/"
    roles: [planner, architect, security-auditor]
    env_vars: [TAVILY_API_KEY]
    install: "npx -y tavily-mcp"

  e2b:
    description: "Cloud-sandboxed Python and JavaScript code execution"
    command: "uvx e2b-mcp"
    roles: [coder, tester]
    env_vars: [E2B_API_KEY]
    install: "pip install e2b-mcp"

  osv:
    description: "Open Source Vulnerability database — CVEs by package and version"
    command: "uvx osv-mcp"
    roles: [security-auditor, reviewer]
    env_vars: []
    install: "pip install osv-mcp"

  mermaid:
    description: "Architecture and flow diagram generation from text descriptions"
    command: "npx -y mermaid-mcp"
    roles: [architect, documenter]
    env_vars: []
    install: "npm install -g mermaid-mcp"

  terraform:
    description: "Terraform Registry lookups, provider docs, module search, HCP workspace management"
    command: "npx -y terraform-mcp-server@latest"
    roles: [devops, architect]
    env_vars: [TF_TOKEN]
    install: "npx -y terraform-mcp-server@latest"
```

**Field definitions:**

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `description` | string | yes | One-line purpose of the server |
| `url` | string | one of url/command | For remote/hosted MCP servers (HTTP type) |
| `command` | string | one of url/command | For local MCP servers that run as a subprocess (stdio type). Mutually exclusive with `url`. |
| `roles` | string[] | yes | List of Teamwork role names that use this server |
| `env_vars` | string[] | yes | Required environment variable names (can be empty list) |
| `install` | string | yes | One-liner install command for the user |

**Design choices:**

- **`url` vs `command`**: Remote servers use `url` (translate to MCP `http` type); local servers use `command` (translate to MCP `stdio` type). These are mutually exclusive.
- **`roles` list**: Enables `teamwork mcp list --role coder` filtering and drives agent file MCP Tools sections.
- **`env_vars`**: Simple list of names, not values. `teamwork mcp list` checks `os.Getenv()` to show status. Values are never stored in config.
- **`install`**: Tells users how to install; `teamwork` never installs servers itself.

### 4. Agent File MCP Tools Sections

Each agent file in `.github/agents/*.agent.md` includes a `## MCP Tools` section after `## Model Requirements`. Unlike the ADR's earlier concept of a priority table, the Phase 4 implementation specifies which tools to call from each server:

```markdown
## MCP Tools
- **GitHub MCP** — `search_code`, `get_file_contents` — understand codebase structure
- **Context7** — `resolve-library-id`, `get-library-docs` — look up library documentation
```

This is prescriptive — it tells agents exactly which MCP tools to use for their role's tasks, not just which servers are available. See issue #22 for the complete per-role specifications.

### 5. Instruction File Surfacing

The instruction file (`.github/copilot-instructions.md`) includes an `## MCP Tools` section that tells agents:

1. Check `.teamwork/config.yaml` for available servers
2. Check their agent file for which tools to use
3. Prefer MCP tools over improvised alternatives (e.g., GitHub MCP over `gh` CLI in bash, Context7 over training memory, Semgrep MCP over manual grep)
4. Fall back gracefully if a server is unavailable
5. Never install MCP servers — that's the user's responsibility

### 6. The `teamwork mcp` CLI Command

Add a `teamwork mcp` command group with two subcommands:

**`teamwork mcp list`**

Reads `mcp_servers` from config and displays a table with server name, roles, env var status (✓ set / ✗ missing), and description.

Flags: `--role` (filter by role), `--json` (JSON output)

**`teamwork mcp config`**

Generates paste-ready JSON configuration for AI clients. Transforms `url` servers to `http` type and `command` servers to `stdio` type.

Flags: `--format` (claude-desktop or vscode), `--only-ready` (exclude servers with missing env vars)

### 7. Integration with `teamwork validate`

The existing `teamwork validate` command (ADR-004) is extended with MCP config checks:

- **Schema validation**: description present, url XOR command, valid URL format, known role names
- **Env var warnings**: Missing env vars produce warnings (not failures) — MCP is optional
- **Silent pass**: If `mcp_servers` section is absent, pass silently

### 8. Recommended Server Details

**GitHub MCP Server** — `github/github-mcp-server`

| Field | Value |
|-------|-------|
| Repository | https://github.com/github/github-mcp-server |
| Maintainer | GitHub (official) |
| Install | `gh extension install github/gh-mcp` |
| Auth | `GH_TOKEN` (required) |
| Roles | All roles |
| Free tier | Unlimited (within GitHub API rate limits) |

**Context7** — `upstash/context7`

| Field | Value |
|-------|-------|
| Repository | https://github.com/upstash/context7 |
| Maintainer | Upstash |
| Install | `npx -y @upstash/context7-mcp` |
| Auth | None required |
| Roles | Architect, Coder, Documenter |
| Free tier | Unlimited |

**Semgrep** — `semgrep/mcp`

| Field | Value |
|-------|-------|
| Repository | https://github.com/semgrep/mcp |
| Maintainer | Semgrep |
| Install | `pip install semgrep-mcp` or `uvx semgrep-mcp` |
| Auth | `SEMGREP_APP_TOKEN` (optional, enables Pro rules) |
| Roles | Security Auditor, Reviewer, Coder |
| Free tier | Unlimited local scans; app token needed for cloud dashboard |

**Tavily** — `tavily-ai/tavily-mcp`

| Field | Value |
|-------|-------|
| Repository | https://github.com/tavily-ai/tavily-mcp |
| Maintainer | Tavily AI |
| Install | `npx -y tavily-mcp` |
| Auth | `TAVILY_API_KEY` (required) |
| Roles | Planner, Architect, Security Auditor |
| Free tier | 1,000 searches/month |

**E2B** — `e2b-dev/e2b-mcp`

| Field | Value |
|-------|-------|
| Repository | https://github.com/e2b-dev/e2b-mcp |
| Maintainer | E2B |
| Install | `pip install e2b-mcp` or `uvx e2b-mcp` |
| Auth | `E2B_API_KEY` (required) |
| Roles | Coder, Tester |
| Free tier | 100 hours/month sandbox time |

**OSV** — `StacklokLabs/osv-mcp`

| Field | Value |
|-------|-------|
| Repository | https://github.com/StacklokLabs/osv-mcp |
| Maintainer | Stacklok |
| Install | `pip install osv-mcp` or `uvx osv-mcp` |
| Auth | None required |
| Roles | Security Auditor, Reviewer |
| Free tier | Unlimited (uses Google's OSV.dev API) |

**Mermaid MCP** — `Narasimhaponnada/mermaid-mcp`

| Field | Value |
|-------|-------|
| Repository | https://github.com/Narasimhaponnada/mermaid-mcp |
| Maintainer | Community |
| Install | `npm install -g mermaid-mcp` |
| Auth | None required |
| Roles | Architect, Documenter |
| Free tier | Unlimited (runs locally) |

**Terraform MCP** — `hashicorp/terraform-mcp-server`

| Field | Value |
|-------|-------|
| Repository | https://github.com/hashicorp/terraform-mcp-server |
| Maintainer | HashiCorp (official) |
| Install | `npx -y terraform-mcp-server@latest` |
| Auth | `TF_TOKEN` (optional, required for HCP Terraform/Enterprise and private registry) |
| Roles | DevOps, Architect |
| Free tier | Unlimited Registry lookups; HCP features require HCP account |

## Consequences

- **Positive:** Projects get a single, declarative place to define MCP server availability. Agents filling any role can discover which servers are configured and use them to enhance their work.
- **Positive:** Agent files explicitly document which MCP tools to call, reducing trial-and-error. A Security Auditor knows to use `semgrep_scan`; a Coder knows to use `resolve-library-id` from Context7.
- **Positive:** The `teamwork mcp config` command generates paste-ready JSON for Claude Desktop and VS Code, eliminating manual translation from the single config source.
- **Positive:** Validation catches misconfigurations (missing fields, both url and command, invalid roles, unresolved env vars) before agents encounter them at runtime.
- **Positive:** 8 servers cover the full spectrum of development tasks — from code search to security scanning to infrastructure provisioning — without requiring paid subscriptions for basic use.
- **Negative:** The `mcp_servers` config schema is a Teamwork-specific abstraction. If a client adds MCP features that don't map to this schema, users must configure those outside Teamwork.
- **Negative:** MCP server recommendations will become outdated as the ecosystem evolves. The recommended servers list needs periodic review.
- **Neutral:** Teamwork does not manage MCP server installation or lifecycle. It documents, configures, and validates — but runtime setup remains the user's responsibility.

## Alternatives Considered

| Alternative | Why It Was Rejected |
|-------------|---------------------|
| Separate `.teamwork/mcp.yaml` config file | Fragments configuration. Users already manage `.teamwork/config.yaml` for roles, model tiers, and workflows. MCP servers are another dimension of the same config. |
| Store MCP config in instruction file | Instruction file is a framework file updated by `teamwork update`. Project-specific MCP server lists would be overwritten. Config belongs in `.teamwork/config.yaml` (starter file, never overwritten). |
| Auto-install MCP server runtimes | Out of scope. Runtime management (Node.js, Python, Docker) is a system-level concern. Teamwork is a development template, not a package manager. |
| Embed MCP server launch configs in agent files | Agent files are framework files. Different projects need different servers. Agent files say "use these tools" (prescriptive), not "launch this server" (configuration). |
| Skip `teamwork mcp config` command | Manual configuration is error-prone with 8 servers and multiple clients. The `config` command generates correct snippets from a single source of truth. |
| Use only remote/hosted MCP servers | Many servers are local-only or work better locally (filesystem access, faster response). Must support both `url` (remote) and `command` (local). |
| Include 5 servers (original proposal) | Insufficient coverage. Adding Tavily (web research), E2B (sandboxed execution), OSV (vulnerability data), and Mermaid (diagrams) fills critical gaps for Planner, Tester, and Security Auditor roles. Terraform was retained from the original for infrastructure-as-code support. |
