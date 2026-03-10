# Release Process

This document describes how to version, prepare, and publish releases for
Teamwork. It covers both the main repo
([JoshLuedeman/teamwork](https://github.com/JoshLuedeman/teamwork)) and the
GitHub CLI extension
([JoshLuedeman/gh-teamwork](https://github.com/JoshLuedeman/gh-teamwork)).

For the full agent-coordinated release workflow, see
[`.github/skills/release-workflow/SKILL.md`](../.github/skills/release-workflow/SKILL.md).

## Versioning Strategy

Teamwork follows [Semantic Versioning](https://semver.org/):

```
vMAJOR.MINOR.PATCH
```

- **MAJOR** — Breaking changes: removed commands, incompatible config schema
  changes, renamed CLI flags.
- **MINOR** — New features: new CLI commands, new agent files, new config
  options. All backward compatible.
- **PATCH** — Bug fixes, security patches, documentation corrections.

Pre-release labels use dotted identifiers: `alpha`, `beta`, `rc`
(e.g., `v2.0.0-rc.1`).

### Version-to-Milestone Mapping

| Version Pattern | When to Use                          | Example                          |
|-----------------|--------------------------------------|----------------------------------|
| `vX.0.0`        | Major phase completion or breaking   | `v1.0.0` = Phase 3              |
| `vX.Y.0`        | Feature milestone complete           | `v1.1.0` = Phase 4: MCP         |
| `vX.Y.Z`        | Bug fix or security patch            | `v1.1.1` = fix CVE              |

## When to Release

Cut a release when any of these conditions are met:

- **Milestone closed** — All issues in a GitHub milestone are resolved.
  Each milestone maps to a version (see table above).
- **Significant feature accumulation** — The `[Unreleased]` section in
  `CHANGELOG.md` has 5 or more entries.
- **Security fix** — Any security patch is released immediately as a
  PATCH version. Do not batch security fixes with feature work.
- **User request** — A user needs access to changes that exist only on
  `main`.
- **Scheduled cadence** — Not currently used. If adopted, document the
  schedule here.

## CHANGELOG Conventions

The project uses [Keep a Changelog](https://keepachangelog.com/en/1.1.0/)
format. The changelog lives at the repository root: `CHANGELOG.md`.

### Rules

1. Always maintain an `[Unreleased]` section at the top of the file.
2. Every merged PR has a corresponding changelog entry.
3. Use these categories (in this order): **Added**, **Changed**,
   **Deprecated**, **Removed**, **Fixed**, **Security**.
4. Reference issue and PR numbers for traceability
   (e.g., `- Added foo command (#42)`).
5. Group related changes under bold subsection headers
   (e.g., **Phase 4: MCP Integration**).

### Cutting a Release

When releasing, transform the changelog:

```markdown
## [Unreleased]

### Added
- ...

## [v1.0.0] — 2026-03-08
```

becomes:

```markdown
## [Unreleased]

## [v1.1.0] — 2026-04-15

### Added
- ...

## [v1.0.0] — 2026-03-08
```

Move all entries from `[Unreleased]` into the new version section. Add a
fresh, empty `[Unreleased]` section above it.

## Release Checklist

Follow these steps in order. The `make release` target automates steps
4–8.

1. **Verify milestone issues are closed.** Open the GitHub milestone and
   confirm zero open issues. Defer or close any stragglers.

2. **Verify CI is green on `main`.** Do not release from a broken build.

3. **Update `CHANGELOG.md`.** Rename `[Unreleased]` to
   `[vX.Y.Z] — YYYY-MM-DD` and add a new empty `[Unreleased]` section.
   Commit this change to `main`.

4. **Run the full test suite.**
   ```bash
   go test ./internal/... ./cmd/...
   ```

5. **Cross-compile binaries** for all supported platforms:
   - `linux/amd64`
   - `linux/arm64`
   - `darwin/amd64`
   - `darwin/arm64`

6. **Create an annotated git tag.**
   ```bash
   git tag -a vX.Y.Z -m "Release vX.Y.Z"
   ```

7. **Push the tag.**
   ```bash
   git push origin vX.Y.Z
   ```

8. **Create a GitHub Release.** Attach the four compiled binaries and
   paste the changelog section as release notes.

9. **Sync `gh-teamwork`.** See [Dual-Repo Release Sync](#dual-repo-release-sync)
   below.

10. **Close the GitHub milestone.**

## Dual-Repo Release Sync

Two repositories release together with matching version numbers:

| Repo | Purpose |
|------|---------|
| [JoshLuedeman/teamwork](https://github.com/JoshLuedeman/teamwork) | Go CLI + template files + MCP servers |
| [JoshLuedeman/gh-teamwork](https://github.com/JoshLuedeman/gh-teamwork) | GitHub CLI extension wrapping `teamwork install`/`update` |

### Process

1. **Release `teamwork` first.** Complete the full release checklist
   (tag, binaries, GitHub Release).
2. **Update `gh-teamwork`** to reference the new teamwork version
   (update the download URL or version constant).
3. **Release `gh-teamwork`** with the same version tag (e.g., both repos
   tag `v1.1.0`).
4. **Verify end-to-end.** Run `gh teamwork init` in a fresh repo to
   confirm the extension fetches the correct teamwork version.

Both releases use the same version number. If only `gh-teamwork` needs a
fix, release it as a PATCH and note the version divergence in its
changelog.

## Automated Release

The `make release` target handles the mechanical steps:

```bash
make release VERSION=v1.1.0
```

What it does:

1. Runs `go test ./internal/... ./cmd/...`
2. Cross-compiles four binaries to `dist/`:
   - `dist/teamwork-linux-amd64`
   - `dist/teamwork-linux-arm64`
   - `dist/teamwork-darwin-amd64`
   - `dist/teamwork-darwin-arm64`
3. Verifies a `CHANGELOG.md` entry exists for the specified version
4. Creates an annotated git tag: `git tag -a vX.Y.Z -m "Release vX.Y.Z"`
5. Creates a GitHub Release with binaries attached via `gh release create`

You still need to manually: update the changelog (step 3 of the
checklist), sync `gh-teamwork`, and close the milestone.

## Version Embedding

The version string is embedded at build time via Go linker flags:

```bash
go build -ldflags "-X main.version=v1.1.0" -o bin/teamwork ./cmd/teamwork
```

- `teamwork --version` displays the embedded version.
- Local dev builds (e.g., `make build-cli`) show `dev` as the version.
- The `make release` target sets the version automatically from the
  `VERSION` parameter.
