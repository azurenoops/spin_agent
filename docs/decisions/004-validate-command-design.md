# ADR-004: Validate Command Design

**Status:** proposed

**Date:** 2025-07-18

## Context

The `teamwork` CLI manages `.teamwork/` protocol files (config, state, handoffs, memory) that are read and written by both agents and humans. Corrupt or incomplete files cause silent failures downstream — a malformed state file can crash `teamwork status`, and a missing config field can cause `teamwork start` to produce invalid workflows. There is no way to check the health of `.teamwork/` as a whole.

We need a `teamwork validate` command that checks structural integrity of all protocol files and reports problems clearly, so users can run it after `init`, before committing, or in CI.

## Decision

We will implement `teamwork validate` as a new cobra command in `cmd/teamwork/cmd/validate.go` with a supporting `internal/validate` package that contains all validation logic.

### Output format

- **Default:** Human-readable, one line per check result. Passing checks print `✓ <description>`. Failing checks print `✗ <description>: <reason>`. A summary line at the end: `N passed, M failed`.
- **`--json` flag:** Structured JSON output (array of check results with `path`, `check`, `passed`, `message` fields) for CI and tooling consumption.
- **`--quiet` flag:** Suppress passing checks; only show failures and the summary. Combine with `--json` for machine-parseable CI output.

### Exit codes

- `0` — All checks pass.
- `1` — One or more validation checks failed.
- `2` — Validate itself could not run (e.g., `.teamwork/` directory does not exist, I/O error reading directory listing). This is distinct from "checks failed" so CI can distinguish between "unhealthy project" and "broken tool invocation."

### Validation checks (ordered by priority)

1. **Config exists** — `.teamwork/config.yaml` must exist and be readable.
2. **Config parses** — File must parse as valid YAML.
3. **Config required fields** — `project.name` non-empty, `project.repo` non-empty, `roles.core` non-empty slice.
4. **State files parse** — Each `*.yaml` in `.teamwork/state/` (recursive) must parse as valid YAML.
5. **State required fields** — Each state file must have: `id` (non-empty string), `type` (non-empty string), `status` (one of `active`, `blocked`, `completed`, `failed`, `cancelled`), `current_step` (integer ≥ 0), `created_at` (non-empty string).
6. **Handoff files non-empty** — Each `*.md` in `.teamwork/handoffs/` (recursive) must exist and have size > 0. No content parsing in v1.
7. **Memory files parse** — Each `*.yaml` in `.teamwork/memory/` must parse as valid YAML if file size > 0. Empty files (created by `init`) are valid.

Checks 1–3 are fatal: if config is missing or unparseable, remaining checks still run (validate is not fail-fast — it reports all problems in one pass).

### Package structure

Create `internal/validate/validate.go` with:

```
type Result struct {
    Path    string `json:"path"`
    Check   string `json:"check"`
    Passed  bool   `json:"passed"`
    Message string `json:"message,omitempty"`
}

func Run(dir string) ([]Result, error)
```

`Run` returns all check results (both passing and failing). It returns an error only for category-2 failures (cannot run at all). Individual check failures are captured in `Result` entries with `Passed: false`.

The command file `cmd/teamwork/cmd/validate.go` handles flag parsing, calls `validate.Run()`, formats output, and sets the exit code.

### Reuse of existing packages

- Use `config.Load()` for checks 1–3. If `Load()` returns an error, report it. If it succeeds, inspect the returned `*Config` struct for required-field checks.
- Use `state.LoadAll()` for check 4, but since `LoadAll` stops on first error, the validate package should implement its own walk that continues past errors to report all problems.
- Do **not** use `memory.LoadCategory()` directly — it silently returns empty structs for missing files. The validate package should do its own `ReadFile` + `yaml.Unmarshal` to distinguish "file missing" from "file empty" from "file invalid."
- Handoff validation is file-stat only (size > 0), no package dependency needed.

### What the Coder needs to implement

1. **`internal/validate/validate.go`** — The `Run(dir string) ([]Result, error)` function plus a `Result` struct. Walk `.teamwork/` subdirectories, apply each check, collect results. Use `gopkg.in/yaml.v3` for YAML parsing (already a dependency). Use `state.StatusActive` etc. constants for status validation. Keep the valid-status set as a `map[string]bool` built from the state package constants.

2. **`internal/validate/validate_test.go`** — Unit tests using `t.TempDir()`. Create valid and invalid `.teamwork/` structures, call `Run()`, assert expected results. Key cases: missing config, malformed YAML, missing required fields, valid state with each status, empty memory file, non-empty invalid memory file, empty handoff file.

3. **`cmd/teamwork/cmd/validate.go`** — Cobra command following the existing pattern: `var validateCmd`, `func init() { rootCmd.AddCommand(validateCmd) }`, `func runValidate(...)`. Add `--json` and `--quiet` bool flags. Format output, set `os.Exit(1)` for failures or `os.Exit(2)` for run errors.

4. **Update `docs/cli.md`** — Add `teamwork validate` section between `init` and `start`, documenting flags and exit codes.

### Error message format

Validation messages should include the file path relative to the project root and a specific description:

```
✗ .teamwork/config.yaml: missing required field "project.name"
✗ .teamwork/state/feature/42.yaml: invalid status "running" (expected one of: active, blocked, completed, failed, cancelled)
✓ .teamwork/memory/patterns.yaml: valid YAML
```

## Consequences

- **Positive:** Users and CI can verify `.teamwork/` integrity in one command. Errors are surfaced before they cause runtime failures in `start`, `status`, or `next`. JSON output enables tooling integration.
- **Positive:** Separate `internal/validate` package keeps validation logic testable and decoupled from cobra.
- **Negative:** The validate package duplicates some file-walking logic that exists in `state.LoadAll()` and `memory.LoadCategory()`. This is intentional — validate needs continue-on-error semantics that those functions don't provide.
- **Neutral:** The `--json` flag establishes a structured output precedent. Other commands may want this later, but we are not building a shared output-formatting layer now. That can be extracted if/when a second command needs it.

## Alternatives Considered

| Alternative | Why It Was Rejected |
|---|---|
| Validate inside `config.Load()` / `state.Load()` as a `Validate()` method on each struct | Spreads validation across packages, makes it hard to get a unified report. Also conflates "load" errors with "invalid content" errors. Validate needs holistic, continue-on-error behavior. |
| Fail-fast on first error | Users would need to run validate repeatedly to find all problems. One-pass reporting is more useful, especially in CI. |
| Only human-readable output (no `--json`) | CI pipelines and editors benefit from structured output. Low implementation cost to support both. |
| Exit code 1 for both "checks failed" and "cannot run" | Makes it impossible for CI to distinguish between "your project has issues" and "the tool is misconfigured." Two distinct codes are standard practice (cf. `go vet`, `shellcheck`). |
