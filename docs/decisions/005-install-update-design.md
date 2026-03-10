# ADR-005: Install and Update Command Design

**Status:** proposed

**Date:** 2025-07-18

## Context

Teamwork is a framework that lives in the `JoshLuedeman/teamwork` repository but is designed to be installed into other projects. Users need two commands:

1. **`teamwork install`** — Fetch framework files from the upstream repo and write them into the current project directory, along with starter files that bootstrap project-specific state.
2. **`teamwork update`** — Pull newer versions of framework files without destroying user modifications to starter files or intentional local edits to framework files.

Today there is no mechanism to do either. The `teamwork init` command only creates the `.teamwork/` directory structure and config — it does not fetch `agents/`, `docs/`, `CLAUDE.md`, `.cursorrules`, or `.github/copilot-instructions.md`. Users must copy these manually.

Key constraints:
- The upstream repo is public on GitHub. Authentication is not required for read access.
- The project already uses `net/http` from the standard library and wraps the `gh` CLI in `internal/github`. No HTTP client library is in `go.mod`.
- Framework files change regularly. Users should be able to update without losing their project-specific customizations (`MEMORY.md`, `CHANGELOG.md`, etc.).
- The CLI already has a `--dir` flag on the root command for specifying the project root.

## Decision

We will implement `teamwork install` and `teamwork update` as two cobra commands backed by a new `internal/installer` package. The design covers four areas: file classification, fetch strategy, version tracking, and conflict detection.

### 1. File Classification

Files are classified into two categories that determine install and update behavior:

**Framework files** — owned by the upstream repo, updated on `teamwork update`:
- `agents/` (entire directory tree)
- `docs/` (entire directory tree)
- `.github/copilot-instructions.md`
- `CLAUDE.md`
- `.cursorrules`
- `Makefile`

**Starter files** — created from templates on `teamwork install`, never overwritten by `teamwork update`:
- `MEMORY.md`
- `CHANGELOG.md`
- `README.md` (only created if no `README.md` exists)
- `.teamwork/config.yaml` (via existing `init` logic)
- `.teamwork/memory/*.yaml` (via existing `init` logic)

The authoritative list of framework files and starter files lives in the `internal/installer` package as Go constants (slices of glob patterns), not in a config file. This avoids a bootstrap problem — you cannot read a config that tells you what to install before you have installed it.

### 2. GitHub API Strategy: Archive Tarball

We will fetch the upstream repository as a gzipped tarball using a single HTTP GET request:

```
GET https://api.github.com/repos/JoshLuedeman/teamwork/tarball/{ref}
```

Where `{ref}` defaults to `main` but can be overridden via `--ref` flag.

The response is a `.tar.gz` archive containing the full repository at that ref. The installer extracts only framework files (and starter files on first install), discarding everything else (Go source, tests, CI config, etc.).

**Why tarball over Contents API:**
- The Contents API (`GET /repos/{owner}/{repo}/contents/{path}`) requires one request per file. The framework currently has ~40 files across `agents/` and `docs/`, and will grow. At GitHub's unauthenticated rate limit of 60 requests/hour, a single install could consume most of the budget.
- The tarball is a single request regardless of file count. The archive is small (the framework is text files, well under 1 MB compressed).
- The tarball endpoint returns a `Location` redirect to a CDN URL, which does not count against the API rate limit for the actual download.

**Why not `gh` CLI:** The `internal/github` package wraps `gh` for issue/PR operations. However, `gh` requires authentication setup and is not guaranteed to be installed on every machine where `teamwork` runs. The tarball endpoint works without authentication for public repos, and using `net/http` from the standard library keeps the dependency footprint at zero.

### 3. Version Tracking

After a successful install or update, the installer writes the resolved commit SHA to:

```
.teamwork/framework-version.txt
```

This is a single-line plain text file containing the full 40-character SHA. Example:

```
a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2
```

The SHA is obtained from the tarball response. GitHub's tarball endpoint returns a `Content-Disposition` header containing the SHA, and the archive's root directory is named `{owner}-{repo}-{short-sha}`, either of which can be parsed.

**`teamwork update`** reads this file to determine the currently installed version. If the file is missing, update treats the installation as untracked and proceeds as a fresh install (with conflict detection still active for existing files).

**`teamwork install`** refuses to run if `.teamwork/framework-version.txt` already exists, printing a message directing the user to `teamwork update` instead. This prevents accidental re-installs that could overwrite user modifications. The `--force` flag overrides this check.

### 4. Conflict Detection: Manifest with Hashes

At install time (and after each successful update), the installer writes a manifest file:

```
.teamwork/framework-manifest.json
```

This file maps each installed framework file path to its SHA-256 hash at the time it was written:

```json
{
  "version": "a1b2c3d4e5f6...",
  "files": {
    "agents/roles/coder.md": "e3b0c44298fc1c149afbf4c8996fb924...",
    "docs/conventions.md": "d7a8fbb307d7809469ca9abcb0082e4f...",
    "CLAUDE.md": "2c26b46b68ffc68ff99b453c1d30413413..."
  }
}
```

On `teamwork update`, the installer uses the manifest to classify each framework file into one of three categories:

| Condition | Action |
|---|---|
| File hash matches manifest (user has not modified it) | Overwrite silently with new version |
| File hash differs from manifest (user modified it) | Print a warning and skip the file, unless `--force` is set |
| File does not exist locally (new in upstream or user deleted it) | Write the new file |
| File exists locally but is not in manifest (untracked framework file) | Write the file and add to manifest |

When a file is skipped due to user modification, the installer prints the path and suggests the user review the upstream changes manually. It does **not** attempt to merge or produce diffs — that is out of scope for v1.

**Why manifest hashes over git diff:** The manifest approach is self-contained. It does not require that the project use git, and it works correctly even if the user has not committed the framework files. Git-based diffing would fail in non-git projects and would conflate "uncommitted" with "user-modified."

**Why not always prompt:** Prompting on every changed file adds friction for users who haven't modified anything. The manifest lets us skip unchanged files silently, only surfacing files where the user made intentional edits.

### 5. Package Structure

Create `internal/installer/installer.go` with the following public API:

```
// Install fetches framework files from upstream and writes them to dir.
// It also creates starter files if they do not exist.
// Returns an error if already installed (use Update instead) unless force is true.
func Install(dir string, opts Options) error

// Update fetches the latest framework files and updates changed files.
// User-modified files are skipped unless force is true.
func Update(dir string, opts Options) error

// Options configures install/update behavior.
type Options struct {
    Ref     string // Git ref to fetch (default: "main")
    Force   bool   // Overwrite user-modified files and bypass already-installed check
    Repo    string // Upstream repo (default: "JoshLuedeman/teamwork")
}
```

Internal (unexported) functions:

- `fetchArchive(repo, ref string) (io.ReadCloser, string, error)` — HTTP GET the tarball, return the reader and resolved SHA.
- `extractFrameworkFiles(r io.Reader, dir string, manifest *Manifest) error` — Walk the tar, extract matching files.
- `writeManifest(dir string, m *Manifest) error` — Write `.teamwork/framework-manifest.json`.
- `readManifest(dir string) (*Manifest, error)` — Read the existing manifest, or return empty if missing.
- `writeVersion(dir string, sha string) error` — Write `.teamwork/framework-version.txt`.
- `readVersion(dir string) (string, error)` — Read the current version SHA.
- `hashFile(path string) (string, error)` — Compute SHA-256 of a file on disk.

### 6. Command Design

**`teamwork install`**

```
Usage: teamwork install [flags]

Flags:
  --ref string   Git ref to install from (branch, tag, or SHA) (default "main")
  --repo string  Source repository (default "JoshLuedeman/teamwork")
  --force        Overwrite existing installation
```

Behavior:
1. Check if `.teamwork/framework-version.txt` exists. If yes and `--force` is not set, exit with error.
2. Fetch tarball from upstream at the specified ref.
3. Extract framework files to the project directory.
4. Create starter files that do not already exist.
5. Run the existing `init` logic (create `.teamwork/` subdirectories, default config, memory files) if `.teamwork/` does not exist.
6. Write `framework-version.txt` and `framework-manifest.json`.
7. Print summary: files written, version installed.

Exit codes: `0` success, `1` error.

**`teamwork update`**

```
Usage: teamwork update [flags]

Flags:
  --ref string   Git ref to update to (branch, tag, or SHA) (default "main")
  --repo string  Source repository (default "JoshLuedeman/teamwork")
  --force        Overwrite user-modified files without warning
```

Behavior:
1. Read `framework-version.txt`. If missing, warn and proceed (treat as untracked install).
2. Read `framework-manifest.json`. If missing, treat all existing files as potentially user-modified.
3. Fetch tarball from upstream at the specified ref.
4. For each framework file in the archive:
   - If file exists locally and hash matches manifest → overwrite silently.
   - If file exists locally and hash differs from manifest → skip with warning (or overwrite if `--force`).
   - If file does not exist locally → write it.
5. Update `framework-version.txt` and `framework-manifest.json`.
6. Print summary: files updated, files skipped (with paths), new version.

Exit codes: `0` success (including when files were skipped), `1` error.

### 7. Integration with Existing `init` Command

`teamwork install` subsumes `teamwork init`. After install lands, `init` should be deprecated in favor of `install`. However, `init` will remain functional for backward compatibility — it continues to create only the `.teamwork/` directory structure without fetching upstream files. The `install` command calls the same `config.Default()` and directory-creation logic internally.

### 8. What the Coder Needs to Implement

1. **`internal/installer/installer.go`** — The `Install`, `Update` functions and all internal helpers. Use `net/http` for the tarball fetch, `archive/tar` and `compress/gzip` from the standard library for extraction, `crypto/sha256` for hashing, `encoding/json` for the manifest.

2. **`internal/installer/installer_test.go`** — Unit tests using `httptest.NewServer` to serve a test tarball. Key cases: fresh install, install when already installed, update with no local changes, update with user-modified file, update with `--force`, new upstream file added, missing manifest.

3. **`cmd/teamwork/cmd/install.go`** — Cobra command following existing patterns. Flags: `--ref`, `--repo`, `--force`.

4. **`cmd/teamwork/cmd/update.go`** — Cobra command following existing patterns. Flags: `--ref`, `--repo`, `--force`.

5. **Update `docs/cli.md`** — Add `teamwork install` and `teamwork update` sections documenting flags, behavior, and exit codes.

6. **Add `.teamwork/framework-version.txt` and `.teamwork/framework-manifest.json` to `.gitignore`** — These are local state, not project configuration. Each developer's installed version may differ during development.

## Consequences

- **Positive:** Users can bootstrap a full Teamwork project with a single command (`teamwork install`). Updates are safe — user modifications are detected and preserved. No git dependency for conflict detection.
- **Positive:** The tarball approach uses a single HTTP request with no authentication required, avoiding rate limit concerns and `gh` CLI dependency.
- **Positive:** The manifest provides a clear, inspectable record of what was installed and whether files have drifted from their installed state.
- **Negative:** The manifest adds two metadata files to `.teamwork/`. These must be kept in sync with actual file state — if a user manually copies a framework file without going through `install`/`update`, the manifest will not reflect it.
- **Negative:** v1 does not merge or diff conflicting files. Users must manually reconcile skipped files by comparing their version with the upstream version. This is acceptable for the initial implementation but may warrant a `--diff` flag in a future iteration.
- **Neutral:** The `init` command is not removed but becomes redundant for users who use `install`. This avoids a breaking change while the commands coexist.

## Alternatives Considered

| Alternative | Why It Was Rejected |
|---|---|
| GitHub Contents API (one request per file) | ~40+ requests per install, risks hitting the 60 req/hr unauthenticated rate limit. Does not scale as the framework adds files. |
| Require `gh` CLI for fetching | Adds an installation prerequisite. `gh` requires authentication setup even for public repos in some configurations. The tarball endpoint needs neither. |
| Git-based conflict detection (`git diff` against installed SHA) | Requires the project to use git. Fails for non-git projects and for users who haven't committed framework files. The manifest approach is self-contained. |
| Always prompt on update for every file | Adds unnecessary friction for the common case where users haven't modified framework files. The manifest lets us skip unchanged files silently. |
| Embed framework files in the `teamwork` binary | Would require rebuilding the binary for every framework change. Decoupling the CLI version from the framework content version allows independent release cadences. |
| Store manifest in `.teamwork/config.yaml` | Mixes framework metadata with user project configuration. Separate files keep concerns cleanly separated and allow `.gitignore` rules to differ. |
