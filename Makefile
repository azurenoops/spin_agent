# Teamwork — Central Command Interface
# ======================================
# This Makefile is the single entry point for all project operations.
# AI agents and human developers use the same targets.
#
# Usage: make <target>
#   Run `make help` (or just `make`) to see available targets.

.DEFAULT_GOAL := help

.PHONY: help setup lint test build check plan review clean build-cli install-cli test-cli docker-build docker-run

help: ## Show this help message
	@echo "Teamwork — available targets:"
	@echo ""
	@grep -E '^[a-zA-Z_-]+:.*?## .*$$' $(MAKEFILE_LIST) | \
		awk 'BEGIN {FS = ":.*?## "}; {printf "  \033[36m%-12s\033[0m %s\n", $$1, $$2}'
	@echo ""

setup: ## One-time dev environment setup
	@bash scripts/setup.sh

lint: ## Run linters
	@bash scripts/lint.sh

test: ## Run tests
	@bash scripts/test.sh

build: ## Build the project
	@bash scripts/build.sh

check: lint test build ## Run lint + test + build in sequence

plan: ## Invoke planning agent (usage: make plan GOAL="description")
	@bash scripts/plan.sh "$(GOAL)"

review: ## Invoke review agent (usage: make review REF="pr-number-or-branch")
	@bash scripts/review.sh "$(REF)"

clean: ## Remove build artifacts
	@echo "Cleaning build artifacts..."
	@# TODO: Add project-specific clean commands, e.g.:
	@#   rm -rf dist/ build/ node_modules/.cache coverage/
	@echo "Nothing to clean yet — add project-specific paths to the clean target."

# --- Orchestration CLI ---

build-cli: ## Build the teamwork CLI binary
	@echo "Building teamwork CLI..."
	@go build -o bin/teamwork ./cmd/teamwork
	@echo "Built: bin/teamwork"

install-cli: ## Install the teamwork CLI to GOPATH/bin
	@echo "Installing teamwork CLI..."
	@go install ./cmd/teamwork
	@echo "Installed: teamwork"

test-cli: ## Run Go tests for the CLI
	@go test ./internal/... ./cmd/...

# --- Docker ---

docker-build: ## Build the teamwork Docker image
	@docker build -t teamwork .

docker-run: ## Run teamwork in Docker (usage: make docker-run CMD="status")
	@docker run --rm -u "$(shell id -u):$(shell id -g)" -v "$(PWD):/project" teamwork $(CMD)
