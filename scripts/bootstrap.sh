#!/usr/bin/env bash
# ──────────────────────────────────────────────────────────────
# ATO Copilot — Developer Machine Bootstrap (macOS / Linux)
# ──────────────────────────────────────────────────────────────
# Idempotent. Safe to re-run. Verifies prerequisites, installs
# what is missing (where possible), and restores all package
# dependencies for the .NET solution and Node.js sub-projects.
#
# Usage:
#   ./scripts/bootstrap.sh              # full bootstrap
#   ./scripts/bootstrap.sh --check      # check only, no install
#   ./scripts/bootstrap.sh --skip-azure # skip Azure CLI install
# ──────────────────────────────────────────────────────────────

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

CHECK_ONLY=0
SKIP_AZURE=0
for arg in "$@"; do
    case "$arg" in
        --check)      CHECK_ONLY=1 ;;
        --skip-azure) SKIP_AZURE=1 ;;
        -h|--help)
            grep '^#' "$0" | sed 's/^# \{0,1\}//'
            exit 0
            ;;
    esac
done

# ── Output helpers ─────────────────────────────────────────────
if [ -t 1 ]; then
    BOLD=$'\033[1m'; GREEN=$'\033[32m'; YELLOW=$'\033[33m'
    RED=$'\033[31m'; BLUE=$'\033[34m'; RESET=$'\033[0m'
else
    BOLD=''; GREEN=''; YELLOW=''; RED=''; BLUE=''; RESET=''
fi
log()    { printf "%s\n" "$*"; }
info()   { printf "${BLUE}ℹ${RESET}  %s\n" "$*"; }
ok()     { printf "${GREEN}✓${RESET}  %s\n" "$*"; }
warn()   { printf "${YELLOW}⚠${RESET}  %s\n" "$*"; }
err()    { printf "${RED}✗${RESET}  %s\n" "$*" >&2; }
header() { printf "\n${BOLD}== %s ==${RESET}\n" "$*"; }

# ── Platform detection ─────────────────────────────────────────
OS="$(uname -s)"
case "$OS" in
    Darwin*) PLATFORM="macos" ;;
    Linux*)
        if [ -f /etc/os-release ]; then
            . /etc/os-release
            PLATFORM="linux-${ID:-unknown}"
        else
            PLATFORM="linux"
        fi
        ;;
    *)       PLATFORM="unknown" ;;
esac
info "Platform: $PLATFORM"
info "Repo root: $REPO_ROOT"

# ── Package manager helpers ────────────────────────────────────
have() { command -v "$1" >/dev/null 2>&1; }

install_with_brew() {
    if ! have brew; then
        warn "Homebrew not found. Install it from https://brew.sh and re-run."
        return 1
    fi
    brew install "$@"
}

install_with_apt() {
    if ! have apt-get; then return 1; fi
    sudo apt-get update -y
    sudo apt-get install -y "$@"
}

run_install() {
    # $1 = friendly name, remaining args = packages (brew uses same names by default)
    local name="$1"; shift
    if [ "$CHECK_ONLY" -eq 1 ]; then
        warn "$name missing (skipping install — --check mode)"
        return 0
    fi
    case "$PLATFORM" in
        macos)        install_with_brew "$@" ;;
        linux-ubuntu|linux-debian) install_with_apt "$@" ;;
        *)            warn "Auto-install not supported on $PLATFORM. Install $name manually."; return 1 ;;
    esac
}

# ── Required tools ─────────────────────────────────────────────
header "Checking prerequisites"

MISSING=0

# Git
if have git; then ok "git: $(git --version | awk '{print $3}')"
else err "git not found"; MISSING=1; fi

# .NET 9 SDK
if have dotnet; then
    DOTNET_VER="$(dotnet --version 2>/dev/null || echo unknown)"
    if [[ "$DOTNET_VER" == 9.* ]]; then
        ok ".NET SDK: $DOTNET_VER"
    else
        warn ".NET SDK present but version is $DOTNET_VER — project requires 9.x"
        info "  Install .NET 9 SDK: https://dotnet.microsoft.com/download/dotnet/9.0"
        run_install "dotnet-sdk-9.0" dotnet-sdk-9.0 || MISSING=1
    fi
else
    err ".NET SDK not found"
    if [ "$PLATFORM" = "macos" ]; then
        run_install "dotnet@9" dotnet@9 || MISSING=1
    else
        run_install "dotnet-sdk-9.0" dotnet-sdk-9.0 || MISSING=1
    fi
fi

# Node 20 + npm (for VS Code/M365 extensions and React SPAs)
if have node; then
    NODE_MAJOR="$(node --version | sed 's/^v//' | cut -d. -f1)"
    if [ "$NODE_MAJOR" -ge 20 ] 2>/dev/null; then
        ok "node: $(node --version)"
    else
        warn "node $(node --version) < 20.x"
        run_install "node@20" node@20 || MISSING=1
    fi
else
    err "node not found"
    run_install "node@20" node@20 || MISSING=1
fi

# Docker (recommended for full deployment)
if have docker; then ok "docker: $(docker --version | awk '{print $3}' | tr -d ',')"
else
    warn "docker not found — required for docker-compose.mcp.yml workflow"
    info "  Install Docker Desktop: https://www.docker.com/products/docker-desktop"
fi

# Azure CLI
if [ "$SKIP_AZURE" -eq 0 ]; then
    if have az; then ok "az: $(az --version 2>/dev/null | head -1 | awk '{print $2}')"
    else
        warn "az (Azure CLI) not found"
        case "$PLATFORM" in
            macos) run_install "azure-cli" azure-cli || true ;;
            linux-ubuntu|linux-debian)
                if [ "$CHECK_ONLY" -eq 0 ]; then
                    curl -sL https://aka.ms/InstallAzureCLIDeb | sudo bash || warn "Azure CLI install failed"
                fi
                ;;
            *) info "  Install Azure CLI: https://learn.microsoft.com/cli/azure/install-azure-cli" ;;
        esac
    fi
fi

# Python 3 (for mkdocs and STIG/CCI generation scripts)
if have python3; then ok "python3: $(python3 --version | awk '{print $2}')"
else
    warn "python3 not found (needed for docs site + STIG/CCI scripts)"
    run_install "python@3.11" python@3.11 || true
fi

# EF Core tools (dotnet ef)
if have dotnet; then
    if dotnet tool list -g 2>/dev/null | grep -q '^dotnet-ef'; then
        ok "dotnet-ef installed globally"
    else
        if [ "$CHECK_ONLY" -eq 0 ]; then
            info "Installing dotnet-ef as a global tool…"
            dotnet tool install --global dotnet-ef --version 9.0.0 || warn "dotnet-ef install failed"
        else
            warn "dotnet-ef not installed (will install on full bootstrap)"
        fi
    fi
fi

if [ "$MISSING" -ne 0 ] && [ "$CHECK_ONLY" -eq 0 ]; then
    err "One or more required prerequisites could not be installed automatically."
    err "Resolve the items above and re-run ./scripts/bootstrap.sh."
    exit 1
fi

if [ "$CHECK_ONLY" -eq 1 ]; then
    header "Check complete"
    exit 0
fi

# ── .env file ──────────────────────────────────────────────────
header "Environment configuration"
if [ ! -f "$REPO_ROOT/.env" ]; then
    if [ -f "$REPO_ROOT/.env.example" ]; then
        cp "$REPO_ROOT/.env.example" "$REPO_ROOT/.env"
        ok "Created .env from .env.example"
        warn "Edit .env to provide your Azure/OpenAI credentials before running the app"
    else
        warn ".env.example not found — skipping .env creation"
    fi
else
    ok ".env already exists (not overwriting)"
fi

# ── Restore .NET dependencies ──────────────────────────────────
header "Restoring .NET solution"
if have dotnet; then
    dotnet restore Ato.Copilot.sln
    ok "dotnet restore complete"
else
    warn "dotnet not on PATH — skipping restore (open a new shell after install)"
fi

# ── Restore Node sub-projects ──────────────────────────────────
restore_node_project() {
    local dir="$1"
    if [ -f "$dir/package-lock.json" ]; then
        info "npm ci in $dir"
        ( cd "$dir" && npm ci --no-audit --no-fund )
        ok "$dir installed"
    elif [ -f "$dir/package.json" ]; then
        info "npm install in $dir (no lockfile)"
        ( cd "$dir" && npm install --no-audit --no-fund )
        ok "$dir installed"
    fi
}

if have npm; then
    header "Restoring Node sub-projects"
    restore_node_project "$REPO_ROOT/extensions/vscode"
    restore_node_project "$REPO_ROOT/extensions/m365"
    restore_node_project "$REPO_ROOT/src/Ato.Copilot.Chat/ClientApp"
    restore_node_project "$REPO_ROOT/src/Ato.Copilot.Dashboard"
else
    warn "npm not on PATH — skipping Node restores"
fi

# ── Done ───────────────────────────────────────────────────────
header "Bootstrap complete"
ok "Next steps:"
log "    1. Edit .env with your Azure / OpenAI credentials"
log "    2. dotnet build Ato.Copilot.sln"
log "    3. dotnet test  Ato.Copilot.sln"
log "    4. docker compose -f docker-compose.mcp.yml up --build   (full stack)"
log ""
log "    See README.md and docs/dev/contributing.md for details."
