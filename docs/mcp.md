# MCP Servers

Teamwork agents become more capable when paired with MCP (Model Context Protocol) servers. MCP servers expose specialized tools — GitHub operations, library documentation, security scanning, web research, sandboxed execution, vulnerability databases, diagram generation, and infrastructure management — that agents invoke during their work.

Configure MCP servers in `.teamwork/config.yaml` and agents automatically discover them. Each agent file (`.github/agents/*.agent.md`) specifies which MCP tools to use for that role.

## Quick Reference

| Role | Servers |
|------|---------|
| Planner | GitHub MCP, Tavily, Changelog |
| Architect | GitHub MCP, Context7, Tavily, Mermaid, Terraform, ADR, Complexity |
| Coder | GitHub MCP, Context7, E2B, Semgrep, Commits, ADR |
| Tester | GitHub MCP, E2B, Coverage, Complexity |
| Reviewer | GitHub MCP, Semgrep, OSV, Coverage, Commits, Complexity |
| Security Auditor | Semgrep, GitHub MCP, OSV, Tavily |
| Documenter | GitHub MCP, Context7, Mermaid, Changelog |
| Orchestrator | GitHub MCP, Coverage, Commits, ADR, Changelog |
| Triager | GitHub MCP |
| DevOps | GitHub MCP, Terraform |
| Dependency Manager | GitHub MCP, OSV |
| Refactorer | GitHub MCP, Semgrep |
| Lint Agent | GitHub MCP, Semgrep |
| API Agent | GitHub MCP, Context7 |
| DBA Agent | GitHub MCP, Context7 |

## Server Setup

### GitHub MCP

GitHub's official MCP server for repository operations — issues, PRs, code search, CI workflows, Dependabot alerts.

**Install:**

```bash
gh extension install github/gh-mcp
```

**Required env vars:** `GH_TOKEN`

- Generate at: <https://github.com/settings/tokens>
- Minimum scopes: `repo`, `read:org`

**Claude Desktop config:**

```json
{
  "github": {
    "type": "http",
    "url": "https://api.githubcopilot.com/mcp/"
  }
}
```

**VS Code config:**

```json
{
  "github": {
    "type": "http",
    "url": "https://api.githubcopilot.com/mcp/"
  }
}
```

### Context7

Real-time, version-specific library documentation. Prevents agents from hallucinating API signatures by fetching current docs instead of relying on training data.

**Install:**

```bash
npx -y @upstash/context7-mcp
```

**Required env vars:** None

**Claude Desktop config:**

```json
{
  "context7": {
    "type": "http",
    "url": "https://mcp.context7.com/mcp"
  }
}
```

**VS Code config:**

```json
{
  "context7": {
    "type": "http",
    "url": "https://mcp.context7.com/mcp"
  }
}
```

### Semgrep

Static Application Security Testing (SAST) with 5000+ rules across 30+ languages. Runs locally — no code leaves your machine.

**Install:**

```bash
pip install semgrep-mcp
```

**Required env vars:** `SEMGREP_APP_TOKEN` (optional — enables Pro rules and cloud dashboard)

- Sign up at: <https://semgrep.dev/login>
- Free tier: unlimited local scans

**Claude Desktop config:**

```json
{
  "semgrep": {
    "type": "stdio",
    "command": "uvx",
    "args": ["semgrep-mcp"],
    "env": { "SEMGREP_APP_TOKEN": "${SEMGREP_APP_TOKEN}" }
  }
}
```

**VS Code config:**

```json
{
  "semgrep": {
    "type": "stdio",
    "command": "uvx",
    "args": ["semgrep-mcp"],
    "env": { "SEMGREP_APP_TOKEN": "${SEMGREP_APP_TOKEN}" }
  }
}
```

### Tavily

Web search and content extraction for research tasks. Useful for investigating unfamiliar domains, looking up RFCs, and researching security advisories.

**Install:**

```bash
npx -y tavily-mcp
```

**Required env vars:** `TAVILY_API_KEY`

- Sign up at: <https://tavily.com>
- Free tier: 1,000 searches/month

**Claude Desktop config:**

```json
{
  "tavily": {
    "type": "http",
    "url": "https://mcp.tavily.com/mcp/"
  }
}
```

**VS Code config:**

```json
{
  "tavily": {
    "type": "http",
    "url": "https://mcp.tavily.com/mcp/"
  }
}
```

### E2B

Cloud-sandboxed Python and JavaScript code execution. Code runs in isolated containers — no risk to your local environment.

**Install:**

```bash
pip install e2b-mcp
```

**Required env vars:** `E2B_API_KEY`

- Sign up at: <https://e2b.dev>
- Free tier: 100 hours/month sandbox time

**Claude Desktop config:**

```json
{
  "e2b": {
    "type": "stdio",
    "command": "uvx",
    "args": ["e2b-mcp"],
    "env": { "E2B_API_KEY": "${E2B_API_KEY}" }
  }
}
```

**VS Code config:**

```json
{
  "e2b": {
    "type": "stdio",
    "command": "uvx",
    "args": ["e2b-mcp"],
    "env": { "E2B_API_KEY": "${E2B_API_KEY}" }
  }
}
```

### OSV MCP

Open Source Vulnerability database. Queries Google's OSV.dev API for CVEs by package name and version.

**Install:**

```bash
pip install osv-mcp
```

**Required env vars:** None

**Claude Desktop config:**

```json
{
  "osv": {
    "type": "stdio",
    "command": "uvx",
    "args": ["osv-mcp"]
  }
}
```

**VS Code config:**

```json
{
  "osv": {
    "type": "stdio",
    "command": "uvx",
    "args": ["osv-mcp"]
  }
}
```

### Mermaid MCP

Architecture and flow diagram generation from text descriptions. Produces Mermaid syntax for embedding in Markdown documentation.

**Install:**

```bash
npm install -g mermaid-mcp
```

**Required env vars:** None

**Claude Desktop config:**

```json
{
  "mermaid": {
    "type": "stdio",
    "command": "npx",
    "args": ["-y", "mermaid-mcp"]
  }
}
```

**VS Code config:**

```json
{
  "mermaid": {
    "type": "stdio",
    "command": "npx",
    "args": ["-y", "mermaid-mcp"]
  }
}
```

### Terraform MCP

Terraform Registry lookups, provider documentation, module search, and HCP Terraform workspace management. Official HashiCorp server.

**Install:**

```bash
npx -y terraform-mcp-server@latest
```

**Required env vars:** `TF_TOKEN` (optional — required for HCP Terraform/Enterprise and private registry)

- Create at: HCP Terraform Settings → API Tokens
- Free tier: unlimited Registry lookups; HCP features require HCP account

**Claude Desktop config:**

```json
{
  "terraform": {
    "type": "stdio",
    "command": "npx",
    "args": ["-y", "terraform-mcp-server@latest"],
    "env": { "TF_TOKEN": "${TF_TOKEN}" }
  }
}
```

**VS Code config:**

```json
{
  "terraform": {
    "type": "stdio",
    "command": "npx",
    "args": ["-y", "terraform-mcp-server@latest"],
    "env": { "TF_TOKEN": "${TF_TOKEN}" }
  }
}
```

### Coverage MCP (Teamwork)

Test coverage report analysis. Parses lcov, Istanbul JSON, and Go `cover.out` reports to surface coverage gaps to agents.

**Install:**

```bash
pip install teamwork-mcp-coverage
```

**Required env vars:** None

**Claude Desktop config:**

```json
{
  "coverage": {
    "type": "stdio",
    "command": "uvx",
    "args": ["teamwork-mcp-coverage"]
  }
}
```

**VS Code config:**

```json
{
  "coverage": {
    "type": "stdio",
    "command": "uvx",
    "args": ["teamwork-mcp-coverage"]
  }
}
```

### Commits MCP (Teamwork)

Conventional commit message generation and validation. Reads git diffs and produces structured, conventional-commit-compliant messages.

**Install:**

```bash
pip install teamwork-mcp-commits
```

**Required env vars:** None

**Claude Desktop config:**

```json
{
  "commits": {
    "type": "stdio",
    "command": "uvx",
    "args": ["teamwork-mcp-commits"]
  }
}
```

**VS Code config:**

```json
{
  "commits": {
    "type": "stdio",
    "command": "uvx",
    "args": ["teamwork-mcp-commits"]
  }
}
```

### ADR MCP (Teamwork)

Architecture Decision Record search, creation, and management. Works with MADR-format markdown files in `docs/decisions/`.

**Install:**

```bash
pip install teamwork-mcp-adr
```

**Required env vars:** None

**Claude Desktop config:**

```json
{
  "adr": {
    "type": "stdio",
    "command": "uvx",
    "args": ["teamwork-mcp-adr"]
  }
}
```

**VS Code config:**

```json
{
  "adr": {
    "type": "stdio",
    "command": "uvx",
    "args": ["teamwork-mcp-adr"]
  }
}
```

### Changelog MCP (Teamwork)

Changelog generation and release notes using git-cliff. Generates changelogs, previews release notes, lists unreleased commits, and suggests the next semantic version.

**Install:**

```bash
pip install teamwork-mcp-changelog
```

**Required env vars:** None

**Claude Desktop config:**

```json
{
  "changelog": {
    "type": "stdio",
    "command": "uvx",
    "args": ["teamwork-mcp-changelog"]
  }
}
```

**VS Code config:**

```json
{
  "changelog": {
    "type": "stdio",
    "command": "uvx",
    "args": ["teamwork-mcp-changelog"]
  }
}
```

### Complexity MCP (Teamwork)

Code complexity analysis using the lizard static analysis library. Computes cyclomatic complexity for 30+ languages.

**Install:**

```bash
pip install teamwork-mcp-complexity
```

**Required env vars:** None

**Claude Desktop config:**

```json
{
  "complexity": {
    "type": "stdio",
    "command": "uvx",
    "args": ["teamwork-mcp-complexity"]
  }
}
```

**VS Code config:**

```json
{
  "complexity": {
    "type": "stdio",
    "command": "uvx",
    "args": ["teamwork-mcp-complexity"]
  }
}
```

## Full Client Configuration

### Claude Desktop

Add to `~/Library/Application Support/Claude/claude_desktop_config.json` (macOS) or `%APPDATA%\Claude\claude_desktop_config.json` (Windows):

```json
{
  "mcpServers": {
    "github": {
      "type": "http",
      "url": "https://api.githubcopilot.com/mcp/"
    },
    "context7": {
      "type": "http",
      "url": "https://mcp.context7.com/mcp"
    },
    "semgrep": {
      "type": "stdio",
      "command": "uvx",
      "args": ["semgrep-mcp"],
      "env": { "SEMGREP_APP_TOKEN": "${SEMGREP_APP_TOKEN}" }
    },
    "tavily": {
      "type": "http",
      "url": "https://mcp.tavily.com/mcp/"
    },
    "e2b": {
      "type": "stdio",
      "command": "uvx",
      "args": ["e2b-mcp"],
      "env": { "E2B_API_KEY": "${E2B_API_KEY}" }
    },
    "osv": {
      "type": "stdio",
      "command": "uvx",
      "args": ["osv-mcp"]
    },
    "mermaid": {
      "type": "stdio",
      "command": "npx",
      "args": ["-y", "mermaid-mcp"]
    },
    "terraform": {
      "type": "stdio",
      "command": "npx",
      "args": ["-y", "terraform-mcp-server@latest"],
      "env": { "TF_TOKEN": "${TF_TOKEN}" }
    },
    "coverage": {
      "type": "stdio",
      "command": "uvx",
      "args": ["teamwork-mcp-coverage"]
    },
    "commits": {
      "type": "stdio",
      "command": "uvx",
      "args": ["teamwork-mcp-commits"]
    },
    "adr": {
      "type": "stdio",
      "command": "uvx",
      "args": ["teamwork-mcp-adr"]
    },
    "changelog": {
      "type": "stdio",
      "command": "uvx",
      "args": ["teamwork-mcp-changelog"]
    },
    "complexity": {
      "type": "stdio",
      "command": "uvx",
      "args": ["teamwork-mcp-complexity"]
    }
  }
}
```

### VS Code

Add to `.vscode/mcp.json` in your project root:

```json
{
  "servers": {
    "github": {
      "type": "http",
      "url": "https://api.githubcopilot.com/mcp/"
    },
    "context7": {
      "type": "http",
      "url": "https://mcp.context7.com/mcp"
    },
    "semgrep": {
      "type": "stdio",
      "command": "uvx",
      "args": ["semgrep-mcp"],
      "env": { "SEMGREP_APP_TOKEN": "${SEMGREP_APP_TOKEN}" }
    },
    "tavily": {
      "type": "http",
      "url": "https://mcp.tavily.com/mcp/"
    },
    "e2b": {
      "type": "stdio",
      "command": "uvx",
      "args": ["e2b-mcp"],
      "env": { "E2B_API_KEY": "${E2B_API_KEY}" }
    },
    "osv": {
      "type": "stdio",
      "command": "uvx",
      "args": ["osv-mcp"]
    },
    "mermaid": {
      "type": "stdio",
      "command": "npx",
      "args": ["-y", "mermaid-mcp"]
    },
    "terraform": {
      "type": "stdio",
      "command": "npx",
      "args": ["-y", "terraform-mcp-server@latest"],
      "env": { "TF_TOKEN": "${TF_TOKEN}" }
    },
    "coverage": {
      "type": "stdio",
      "command": "uvx",
      "args": ["teamwork-mcp-coverage"]
    },
    "commits": {
      "type": "stdio",
      "command": "uvx",
      "args": ["teamwork-mcp-commits"]
    },
    "adr": {
      "type": "stdio",
      "command": "uvx",
      "args": ["teamwork-mcp-adr"]
    },
    "changelog": {
      "type": "stdio",
      "command": "uvx",
      "args": ["teamwork-mcp-changelog"]
    },
    "complexity": {
      "type": "stdio",
      "command": "uvx",
      "args": ["teamwork-mcp-complexity"]
    }
  }
}
```

Or run `teamwork mcp config --format vscode` to generate this automatically.

## Security Notes

- Never commit API keys or tokens — use environment variables only
- GitHub token scopes: minimum required is `repo` and `read:org`
- Semgrep: free tier allows unlimited local scans; app token only needed for cloud dashboard
- E2B: sandboxed by default — code cannot access your host filesystem
- Run local MCP servers (those using `command`) in Docker with `--network none` for sensitive repos
- Review MCP server source code before granting access to private repositories

## Free Tier Limits

| Server | Free Tier | Paid Required For |
|--------|-----------|-------------------|
| GitHub MCP | Unlimited (within API rate limits) | — |
| Context7 | Unlimited | Higher rate limits |
| Semgrep | Unlimited local scans | Pro rules, cloud dashboard |
| Tavily | 1,000 searches/month | Higher volume |
| E2B | 100 hours/month sandbox time | More compute time |
| OSV | Unlimited | — |
| Mermaid | Unlimited (runs locally) | — |
| Terraform | Unlimited Registry lookups | HCP features, private registry |
| Coverage (Teamwork) | Unlimited (runs locally) | — |
| Commits (Teamwork) | Unlimited (runs locally) | — |
| ADR (Teamwork) | Unlimited (runs locally) | — |
| Changelog (Teamwork) | Unlimited (runs locally) | — |
| Complexity (Teamwork) | Unlimited (runs locally) | — |

## Future MCP Servers

Additional MCP servers are being considered for future releases:

- Custom changelog server ([#30](https://github.com/JoshLuedeman/teamwork/issues/30))
- Custom conventions server ([#31](https://github.com/JoshLuedeman/teamwork/issues/31))
- Custom handoff server ([#32](https://github.com/JoshLuedeman/teamwork/issues/32))
- Custom metrics server ([#33](https://github.com/JoshLuedeman/teamwork/issues/33))
- Custom template server ([#34](https://github.com/JoshLuedeman/teamwork/issues/34))

See the [Phase 4 milestone](https://github.com/JoshLuedeman/teamwork/milestone/5) for details.
