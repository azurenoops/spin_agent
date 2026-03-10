# Coding Conventions and Standards

This document defines language-agnostic conventions for the project. Agents and humans should follow these consistently. When in doubt, follow the convention — don't invent a new pattern.

## Git

### Branch Naming

Use prefixed branch names with lowercase kebab-case:

- `feature/<short-description>` — new functionality
- `bugfix/<short-description>` — fixing broken behavior
- `refactor/<short-description>` — restructuring without behavior change
- `docs/<short-description>` — documentation-only changes
- `chore/<short-description>` — tooling, dependencies, CI

### Commit Messages

Follow [Conventional Commits](https://www.conventionalcommits.org/):

```
<type>(<scope>): <short summary>

<optional body — explain what and why, not how>
```

**Types:** `feat`, `fix`, `docs`, `refactor`, `test`, `chore`, `ci`

- Keep the summary under 72 characters
- Use imperative mood: "add feature" not "added feature"
- Reference issues when applicable: `fix(auth): handle expired tokens (#42)`

### Pull Requests

- One logical change per PR
- Title follows the same format as commit messages
- Description must include: what changed, why, and how to verify
- Link related issues or ADRs

## Code

### File Naming

- Use lowercase with hyphens or underscores per language convention (e.g., `user-service.ts`, `user_service.py`)
- Test files mirror source files: `user-service.test.ts` or `test_user_service.py`
- No spaces in filenames, ever

### Directory Structure

- Group by feature or domain, not by file type
- Keep nesting shallow — three levels deep maximum for source code
- Shared utilities go in a `shared/` or `common/` directory
- Configuration files live at the project root

### Documentation

- Every public function or module should have a brief doc comment
- READMEs are required at the project root and in major subdirectories
- Use inline comments only to explain **why**, not **what**

## Testing

### Test File Naming

- Co-locate tests with source or place in a parallel `tests/` directory
- Name test files to match their source: `<source-file>.test.<ext>` or `test_<source-file>.<ext>`

### Test Types

| Type | Scope | Speed | When to Write |
|---|---|---|---|
| Unit | Single function or class | Fast | Always |
| Integration | Multiple components together | Medium | When components interact |
| E2E | Full user workflow | Slow | For critical paths |

### Coverage

- Aim for meaningful coverage, not a percentage target
- All new code should include tests
- Bug fixes must include a regression test

## Dependencies

### Adding Dependencies

- Justify new dependencies — prefer standard library when reasonable
- Pin versions or use lockfiles
- Document why a dependency was chosen if it's non-obvious

### Updating Dependencies

- Update dependencies in dedicated commits or PRs, not mixed with feature work
- Run the full test suite after updates
- Review changelogs for breaking changes before updating major versions

### Lockfiles

- Always commit lockfiles (`package-lock.json`, `poetry.lock`, `go.sum`, etc.)
- Never manually edit lockfiles — regenerate them with the package manager

## Acceptance Criteria

### Format

Use checkbox format in issue descriptions so progress is trackable:

```
- [ ] Criterion
```

Each criterion must be **testable and verifiable** — if you can't write a test or manually confirm it, rewrite it until you can.

### Behavioral Criteria

For criteria that describe system behavior, use GIVEN/WHEN/THEN:

```
- [ ] GIVEN <precondition> WHEN <action> THEN <expected result>
```

### Example

```
- [ ] Users can log in with email and password
- [ ] GIVEN an invalid password WHEN the user submits login THEN a 401 error is returned
- [ ] Login endpoint responds in under 200ms at p95
```

### Guidelines

- Write criteria before implementation begins, not after
- Avoid vague language — "works correctly" or "handles errors" is not testable
- Include performance criteria when relevant (response time, throughput)
- The Tester uses these criteria as the definitive checklist for validation
