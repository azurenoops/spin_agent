---
applyTo: "**/*.go"
---
# Go Coding Guidelines

- Follow standard Go conventions (`gofmt`, `go vet`, `golangci-lint`).
- Use `error` returns, not panics, for expected failure cases.
- Name packages with short, lowercase, single-word names.
- Prefer table-driven tests using `t.Run()` subtests.
- Use `context.Context` as the first parameter for functions that do I/O or may be cancelled.
- Keep functions short and focused. If a function exceeds ~50 lines, consider splitting it.
- Use meaningful variable names. Avoid single-letter names except for loop indices and short-lived locals.
- Handle errors explicitly — do not use `_` to discard errors unless there is a documented reason.
- Use `go doc` style comments for exported symbols.
- Run `go test ./...` to verify changes before committing.
