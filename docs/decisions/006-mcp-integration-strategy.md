# ADR-006: MCP Integration Strategy

**Status:** proposed

**Date:** 2026-03-03

## Context

Teamwork defines 8 roles that AI agents fill during development workflows. Each role has distinct responsibilities — the Security Auditor scans for vulnerabilities, the Coder writes and tests code, the Documenter maintains docs, and so on. Today, agents filling these roles rely solely on the tools their host environment provides (file editing, terminal commands, web search). They have no structured way to access specialized external tools tailored to their role.

The Model Context Protocol (MCP) is a standard for connecting AI agents to external tool servers. MCP servers expose domain-specific capabilities (GitHub operations, static analysis, documentation lookup) that agents can invoke during their work. A growing ecosystem of actively maintained MCP servers exists for software development use cases.

Phase 4 of the Teamwork roadmap adds MCP server integration so that:

1. Projects can declare which MCP servers are available for agent use.
2. Each role can specify which MCP servers benefit its work.
3. Instruction files (`.github/copilot-instructions.md`) surface MCP configuration so agents discover available servers.
4. The `teamwork` CLI can list, generate, and validate MCP configuration.

Key constraints:

- MCP server configuration must live in `.teamwork/config.yaml` alongside existing project settings (model tiers, roles, workflows). Adding a separate config file creates fragmentation.
- Role files in `.github/agents/` are framework files managed by `teamwork install`/`teamwork update` (ADR-005). Role-to-MCP-server mappings should be part of these files so they update with the framework.
- Different AI clients (Claude Code, Cursor, GitHub Copilot) each have their own MCP configuration format. Teamwork should not duplicate these — it should document which servers to configure, not replace client-specific config.
- MCP servers require different runtimes (Node.js, Python, Docker). Teamwork should recommend servers but cannot guarantee runtime availability.

## Decision

We will add MCP server support to Teamwork across four layers: config schema, role references, instruction file surfacing, and CLI commands.

### 1. MCP Server Configuration in `.teamwork/config.yaml`

Add a top-level `mcp_servers` key to `.teamwork/config.yaml`. Each entry defines a named MCP server with its launch configuration:

```yaml
# MCP server definitions
# These declare which MCP servers are available for agents in this project.
# Agents reference these by name; actual client configuration (Claude, Cursor, etc.)
# is managed separately per docs/mcp-setup.md.
mcp_servers:
  github:
    description: "GitHub repository operations — issues, PRs, code search, commits"
    command: "npx"
    args: ["-y", "@modelcontextprotocol/server-github"]
    env:
      GITHUB_PERSONAL_ACCESS_TOKEN: "${GITHUB_TOKEN}"
    required: true

  semgrep:
    description: "Static analysis and security scanning via Semgrep"
    command: "uvx"
    args: ["semgrep-mcp"]
    env:
      SEMGREP_APP_TOKEN: "${SEMGREP_APP_TOKEN}"
    required: false

  context7:
    description: "Up-to-date library documentation lookup"
    command: "npx"
    args: ["-y", "@upstash/context7-mcp"]
    env: {}
    required: false

  playwright:
    description: "Browser automation for E2E testing"
    command: "npx"
    args: ["@playwright/mcp@latest"]
    env: {}
    required: false

  terraform:
    description: "Terraform Registry lookups, provider docs, module search, and HCP Terraform workspace management"
    command: "npx"
    args: ["-y", "terraform-mcp-server@latest"]
    env:
      TF_TOKEN: "${TF_TOKEN}"
    required: false
```

**Field definitions:**

| Field | Type | Required | Description |
|---|---|---|---|
| `description` | string | yes | Human-readable description of what the server provides. Included in instruction file output. |
| `command` | string | yes | Executable to launch the server (`npx`, `uvx`, `node`, `docker`, etc.). |
| `args` | string[] | yes | Arguments passed to the command. |
| `env` | map[string]string | no | Environment variables for the server process. Values starting with `$` or `${...}` reference the host environment. |
| `required` | bool | no | If `true`, `teamwork validate` reports a warning when the server's runtime is not available. Default: `false`. |
| `url` | string | no | For remote/hosted MCP servers. Mutually exclusive with `command`. |
| `headers` | map[string]string | no | HTTP headers for remote servers. Only valid when `url` is set. |

Remote servers (hosted endpoints) use `url` instead of `command`:

```yaml
mcp_servers:
  semgrep-hosted:
    description: "Semgrep hosted scanning service"
    url: "https://mcp.semgrep.ai/mcp"
    headers:
      SEMGREP_APP_TOKEN: "${SEMGREP_APP_TOKEN}"
    required: false
```

### 2. Role References to MCP Servers

Each agent file in `.github/agents/` will include a new `## MCP Servers` section listing which MCP servers benefit that agent. This section is informational — it tells agents which servers to look for, not which are guaranteed to be configured.

Format (added after the `## Model Requirements` section in each role file):

```markdown
## MCP Servers

The following MCP servers enhance this role's capabilities when available:

| Server | Purpose | Priority |
|---|---|---|
| github | Read issues, PRs, commits, and code across repos | recommended |
| context7 | Look up current library documentation | optional |
```

**Priority levels:**

- **recommended** — The server significantly improves the role's effectiveness. Agents should check if it is available and use it when configured.
- **optional** — The server is useful but not essential. The role functions well without it.

Role files do not contain server launch configuration (that lives in `config.yaml`). They reference servers by name only.

**Role-to-server mapping:**

| Role | github | semgrep | context7 | playwright | terraform |
|---|---|---|---|---|---|
| Planner | recommended | — | optional | — | — |
| Architect | recommended | — | optional | — | optional |
| Coder | recommended | — | recommended | optional | optional |
| Tester | recommended | — | optional | recommended | — |
| Reviewer | recommended | optional | — | — | — |
| Security Auditor | recommended | recommended | — | — | — |
| Documenter | recommended | — | recommended | — | — |
| Orchestrator | recommended | — | — | — | — |
| DevOps | recommended | — | — | — | recommended |

### 3. Instruction File Surfacing

The instruction file (`.github/copilot-instructions.md`) will include a section that tells agents how to discover MCP server configuration. This section is part of the framework files maintained by `teamwork install`/`teamwork update`.

Add the following section to the instruction file:

```markdown
## MCP Servers

If your environment has MCP servers configured, use them to enhance your work.
Run `teamwork mcp list` to see which servers are defined for this project.
Check your agent file's "MCP Servers" section for which servers benefit your role.

Common servers:
- **github** — Repository operations (issues, PRs, code search). Available to all roles.
- **semgrep** — Static analysis and security scanning. Primary for security-auditor.
- **context7** — Up-to-date library documentation. Useful for coder and documenter.
- **playwright** — Browser automation. Useful for tester.
```

This section is deliberately brief. It points agents to `teamwork mcp list` for the authoritative list rather than duplicating config. The instruction file is a framework file — it cannot contain project-specific MCP server lists because those vary per project.

### 4. The `teamwork mcp` CLI Command

Add a `teamwork mcp` command group with three subcommands:

**`teamwork mcp list`**

```
Usage: teamwork mcp list [flags]

Lists all MCP servers defined in .teamwork/config.yaml.

Flags:
  --role string   Filter to servers relevant to a specific role
  --json          Output as JSON
```

Behavior:
1. Read `mcp_servers` from `.teamwork/config.yaml`.
2. If `--role` is set, cross-reference with the role's MCP server mapping and filter.
3. Print each server's name, description, command/url, and required status.
4. Exit 0 on success, 2 if config is unreadable.

Example output:

```
MCP Servers (3 configured):

  github (required)
    GitHub repository operations — issues, PRs, code search, commits
    Command: npx -y @modelcontextprotocol/server-github

  semgrep
    Static analysis and security scanning via Semgrep
    Command: uvx semgrep-mcp

  context7
    Up-to-date library documentation lookup
    Command: npx -y @upstash/context7-mcp
```

**`teamwork mcp config`**

```
Usage: teamwork mcp config [flags]

Generates MCP server configuration snippets for specific AI clients.

Flags:
  --client string   Target client: claude, cursor, copilot (required)
  --role string     Filter to servers relevant to a specific role
```

Behavior:
1. Read `mcp_servers` from `.teamwork/config.yaml`.
2. Transform each server entry into the target client's configuration format.
3. Print the generated config to stdout.
4. Exit 0 on success, 1 on error.

Example for `--client claude`:

```json
{
  "mcpServers": {
    "github": {
      "command": "npx",
      "args": ["-y", "@modelcontextprotocol/server-github"],
      "env": {
        "GITHUB_PERSONAL_ACCESS_TOKEN": "${GITHUB_TOKEN}"
      }
    },
    "semgrep": {
      "command": "uvx",
      "args": ["semgrep-mcp"],
      "env": {
        "SEMGREP_APP_TOKEN": "${SEMGREP_APP_TOKEN}"
      }
    }
  }
}
```

**`teamwork mcp validate`**

```
Usage: teamwork mcp validate [flags]

Validates MCP server configuration in .teamwork/config.yaml.

Flags:
  --json    Output as JSON
```

Behavior:
1. Parse `mcp_servers` from config.
2. For each server, check:
   - Required fields present (`description`, and either `command`+`args` or `url`).
   - `command` and `url` are not both set (mutually exclusive).
   - If `required: true`, check that the command is available on `$PATH` (using `exec.LookPath`).
   - Environment variable references (`${VAR}`) in `env` resolve to non-empty values (warning, not error).
3. Print results in the same format as `teamwork validate` (✓/✗ lines, `--json` for structured output).
4. Exit 0 if all checks pass, 1 if any fail, 2 if config is unreadable.

### 5. Integration with `teamwork validate`

The existing `teamwork validate` command (ADR-004) will be extended to include MCP validation checks:

- **MCP config parses** — If `mcp_servers` key exists, each entry must have the required fields.
- **MCP required servers available** — For each server with `required: true`, check that the command is on `$PATH`.

These checks are non-fatal — MCP misconfiguration should produce warnings, not prevent other validation from running. This matches the continue-on-error design from ADR-004.

### 6. Recommended MCP Servers

The following MCP servers are recommended as defaults for new Teamwork projects. These are actively maintained, widely adopted, and cover the core needs of the 8 roles.

**GitHub MCP Server** — `github/github-mcp-server`

| Field | Value |
|---|---|
| Repository | https://github.com/github/github-mcp-server |
| Maintainer | GitHub (official) |
| Runtime | Node.js 18+ |
| Install | `npx -y @modelcontextprotocol/server-github` |
| Auth | `GITHUB_PERSONAL_ACCESS_TOKEN` (required) |
| Roles | All roles |

Provides: file contents, commit history, issue and PR management, code search, branch operations. Every role benefits from GitHub access — planners read issues, coders search code, reviewers read PRs, orchestrators track workflow state.

**Semgrep MCP Server** — `semgrep/mcp`

| Field | Value |
|---|---|
| Repository | https://github.com/semgrep/mcp |
| Maintainer | Semgrep |
| Runtime | Python 3.10+ (via `uvx`) or Docker |
| Install | `uvx semgrep-mcp` |
| Auth | `SEMGREP_APP_TOKEN` (optional, enables Pro rules) |
| Hosted | `https://mcp.semgrep.ai/mcp` (no auth for public scanning) |
| Roles | Security Auditor (recommended), Reviewer (optional) |

Provides: static code analysis, security scanning, custom rule execution, AST inspection. Primary tool for the Security Auditor role. Reviewers benefit from automated security checks during PR review.

**Context7 MCP Server** — `upstash/context7`

| Field | Value |
|---|---|
| Repository | https://github.com/upstash/context7 |
| Maintainer | Upstash |
| Runtime | Node.js 18+ |
| Install | `npx -y @upstash/context7-mcp` |
| Auth | None required (API key optional for higher rate limits) |
| Roles | Coder (recommended), Documenter (recommended), Planner (optional), Architect (optional) |

Provides: current, version-specific documentation for libraries and frameworks. Prevents agents from using deprecated APIs or hallucinating function signatures. Valuable for any role that references external library documentation.

**Playwright MCP Server** — `microsoft/playwright-mcp`

| Field | Value |
|---|---|
| Repository | https://github.com/microsoft/playwright-mcp |
| Maintainer | Microsoft |
| Runtime | Node.js 18+ |
| Install | `npx @playwright/mcp@latest` |
| Auth | None required |
| Roles | Tester (recommended), Coder (optional) |

Provides: browser automation via accessibility tree snapshots, page navigation, element interaction, screenshot capture. Enables AI-driven E2E test authoring and debugging. Primary tool for the Tester role when working on web applications.

**Terraform MCP Server** — `hashicorp/terraform-mcp-server`

| Field | Value |
|---|---|
| Repository | https://github.com/hashicorp/terraform-mcp-server |
| Maintainer | HashiCorp (official) |
| Runtime | Node.js 18+ or Docker |
| Install | `npx -y terraform-mcp-server@latest` |
| Docker | `docker run -i --rm hashicorp/terraform-mcp-server` |
| Auth | `TF_TOKEN` (optional, required for HCP Terraform/Enterprise features and private registry) |
| Roles | DevOps (recommended), Architect (optional), Coder (optional) |

Provides: Terraform Registry lookups (providers, modules, Sentinel policies), provider capability discovery, module version details, and HCP Terraform workspace management (list orgs/projects/workspaces, manage variables and tags, run management). Enables agents to generate accurate, up-to-date Terraform configurations using real provider schemas rather than potentially outdated training data. Primary tool for the DevOps role when working with infrastructure-as-code. Architects benefit from provider and module discovery during design. Coders benefit when writing or modifying Terraform configurations.

### 7. What the Coder Needs to Implement

1. **Update `internal/config/config.go`** — Add `MCPServers map[string]MCPServer` field to the `Config` struct. Define `MCPServer` struct with fields: `Description`, `Command`, `Args`, `Env`, `Required`, `URL`, `Headers`.

2. **Create `cmd/teamwork/cmd/mcp.go`** — Parent cobra command for the `mcp` command group. Subcommands: `list`, `config`, `validate`.

3. **Create `cmd/teamwork/cmd/mcp_list.go`** — Implements `teamwork mcp list`. Flags: `--role`, `--json`.

4. **Create `cmd/teamwork/cmd/mcp_config.go`** — Implements `teamwork mcp config`. Flags: `--client`, `--role`.

5. **Create `cmd/teamwork/cmd/mcp_validate.go`** — Implements `teamwork mcp validate`. Flags: `--json`.

6. **Update `internal/validate/validate.go`** — Add MCP validation checks to the existing `Run` function.

7. **Update agent files** — Add `## MCP Servers` section to each of the agent files in `.github/agents/`.

8. **Update instruction file** — Add the `## MCP Servers` section to `.github/copilot-instructions.md`.

9. **Update `docs/cli.md`** — Add `teamwork mcp` section documenting subcommands, flags, and examples.

## Consequences

- **Positive:** Projects get a single, declarative place to define MCP server availability. Agents filling any role can discover which servers are configured and use them to enhance their work.
- **Positive:** Role files explicitly document which MCP servers are relevant, reducing trial-and-error by agents. A Security Auditor knows to look for Semgrep; a Coder knows to look for Context7.
- **Positive:** The `teamwork mcp config` command eliminates manual translation between Teamwork's config format and client-specific formats (Claude, Cursor, Copilot). Users configure once in `.teamwork/config.yaml` and generate client configs.
- **Positive:** Validation catches misconfigurations (missing fields, unavailable runtimes, unresolved env vars) before agents encounter them at runtime.
- **Negative:** The `mcp_servers` config schema is a Teamwork-specific abstraction over client-specific formats. If a client adds MCP features that don't map to this schema (e.g., client-specific auth flows), users must configure those outside Teamwork.
- **Negative:** Runtime availability checking (`exec.LookPath`) is best-effort. A command may be on `$PATH` but fail to start (wrong version, missing dependencies). Teamwork validates presence, not functionality.
- **Negative:** MCP server recommendations will become outdated as the ecosystem evolves. The recommended servers list in this ADR and in default config will need periodic review.
- **Neutral:** Teamwork does not manage MCP server installation or lifecycle. It documents what's available and validates configuration, but `npm install`, `pip install`, and runtime setup remain the user's responsibility.

## Alternatives Considered

| Alternative | Why It Was Rejected |
|---|---|
| Separate `.teamwork/mcp.yaml` config file | Fragments configuration. Users already manage `.teamwork/config.yaml` for roles, model tiers, and workflows. MCP servers are another dimension of the same orchestration config. A single file keeps everything discoverable. |
| Store MCP config directly in instruction file (`.github/copilot-instructions.md`) | Instruction file is a framework file updated by `teamwork update`. Project-specific MCP server lists would be overwritten on update. Config belongs in `.teamwork/config.yaml` which is a starter file (never overwritten). |
| Auto-install MCP server runtimes | Out of scope. Runtime management (Node.js, Python, Docker) is a system-level concern. Teamwork is a development template, not a package manager. Users install runtimes; Teamwork validates they're available. |
| Embed MCP server configs in agent files | Agent files are framework files, not project-specific. Different projects need different MCP servers. The agent files should say "I benefit from server X" (informational), not "server X is at this command" (configuration). |
| Skip the `teamwork mcp config` command and let users manually configure each client | Manual configuration is error-prone and tedious when multiple MCP servers and multiple clients are involved. The `config` command generates correct snippets from a single source of truth. Low implementation cost, high user value. |
| Use only remote/hosted MCP servers (no local command support) | Many MCP servers are local-only or work better locally (filesystem access, faster response). The schema must support both local (`command`+`args`) and remote (`url`+`headers`) servers. |
