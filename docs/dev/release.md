# Release Guide

> Versioning strategy, changelog management, Docker image tagging, and extension publishing.

---

## Versioning

Security Posture Intelligence Navigator follows [Semantic Versioning](https://semver.org/) (`MAJOR.MINOR.PATCH`):

| Component | Version Source | Example |
|-----------|---------------|---------|
| MCP Server | `Ato.Copilot.Mcp.csproj` | `1.15.0` |
| Core Library | `Ato.Copilot.Core.csproj` | `1.15.0` |
| Agents Library | `Ato.Copilot.Agents.csproj` | `1.15.0` |
| M365 Extension | `extensions/m365/package.json` | `1.15.0` |
| VS Code Extension | `extensions/vscode/package.json` | `1.15.0` |

All components share the same version number and are released together.

### Version Bumping Rules

| Change Type | Version Bump | Examples |
|------------|-------------|---------|
| Breaking API changes | MAJOR | Tool renamed, parameter removed, entity schema change |
| New features | MINOR | New tool, new entity, new Adaptive Card |
| Bug fixes, docs | PATCH | Fix assessment logic, update guide |

---

## Changelog

Maintain `CHANGELOG.md` in the repository root using [Keep a Changelog](https://keepachangelog.com/) format:

```markdown
# Changelog

## [1.15.0] - 2025-01-15

### Added
- Feature 015: Persona-Driven RMF Workflows
  - 56 new MCP tools across 7 RMF phases
  - 18 new EF Core entities for RMF lifecycle
  - AuthorizingOfficial RBAC role
  - 4 new Adaptive Cards (System Summary, Categorization, Authorization, Dashboard)
  - RMF Overview webview panel for VS Code
  - IaC compliance diagnostics with code actions

### Changed
- Updated deployment guide with Feature 015 configuration
- ComplianceAgent constructor expanded for new tool injection

### Fixed
- AsyncLocal context propagation in singleton agent
```

### Sections

- **Added** — New features
- **Changed** — Changes to existing functionality
- **Deprecated** — Soon-to-be removed features
- **Removed** — Removed features
- **Fixed** — Bug fixes
- **Security** — Vulnerability fixes

---

## Docker

### Image Tagging

```bash
# Build with version tag
docker build -t ato-copilot:1.15.0 .
docker tag ato-copilot:1.15.0 ato-copilot:latest

# For development
docker compose -f docker-compose.mcp.yml build
```

### Tag Strategy

| Tag | Purpose |
|-----|---------|
| `x.y.z` | Specific release version |
| `latest` | Most recent stable release |
| `main` | Latest build from main branch (CI) |
| `x.y.z-rc.N` | Release candidate |

### Docker Compose (Development)

```bash
docker compose -f docker-compose.mcp.yml up --build
```

---

## Extension Publishing

### VS Code Extension

```bash
cd extensions/vscode
npm run package          # Creates .vsix
vsce publish             # Publish to VS Code Marketplace
```

### M365 Teams App

```bash
cd extensions/m365
npm run build
# Upload manifest to Teams Admin Center or use Teams Toolkit
```

---

## Release Checklist

1. **Pre-Release**
   - [ ] All tests pass (`dotnet test`, `npm test` in extensions)
   - [ ] No CAT I/II findings in compliance gate
   - [ ] Update version numbers across all projects
   - [ ] Update `CHANGELOG.md` with all changes
   - [ ] Architecture docs reflect current state

2. **Build & Tag**
   - [ ] Create release branch: `release/x.y.z`
   - [ ] Build Docker image with version tag
   - [ ] Package VS Code extension (.vsix)
   - [ ] Build M365 app package

3. **Validation**
   - [ ] Docker container starts and MCP server responds
   - [ ] Quickstart scenario completes successfully
   - [ ] VS Code extension activates and connects to MCP
   - [ ] Teams bot responds to commands

4. **Release**
   - [ ] Merge release branch to `main`
   - [ ] Tag: `git tag v1.15.0`
   - [ ] Push Docker image to registry
   - [ ] Publish VS Code extension
   - [ ] Deploy Teams app update

5. **Post-Release**
   - [ ] Verify production deployment
   - [ ] Update documentation links if needed
   - [ ] Announce release to stakeholders

---

## CI/CD Pipeline

### GitHub Actions Workflow

| Job | Purpose |
|-----|---------|
| `build` | `dotnet build`, `npm run build` |
| `test` | `dotnet test`, `npm test` |
| `compliance-gate` | IaC scanning, STIG checks |
| `docker` | Build and push Docker image |
| `publish` | Publish extensions (on tag) |

### Compliance Gate

The `ato-compliance-gate` GitHub Action runs on every PR:
- Scans IaC files (Terraform, Bicep, ARM templates)
- Blocks merge on CAT I/II findings
- Respects risk acceptances from the Security Posture Intelligence Navigator database
- Adds annotations with finding details

See `.github/actions/ato-compliance-gate/action.yml` for configuration.
