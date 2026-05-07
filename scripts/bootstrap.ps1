<#
.SYNOPSIS
    ATO Copilot — Developer Machine Bootstrap (Windows / PowerShell)

.DESCRIPTION
    Idempotent. Safe to re-run. Verifies prerequisites, installs what is
    missing via winget where possible, and restores all package
    dependencies for the .NET solution and Node.js sub-projects.

.PARAMETER Check
    Check prerequisites only — do not install anything.

.PARAMETER SkipAzure
    Skip the Azure CLI install / check.

.EXAMPLE
    .\scripts\bootstrap.ps1
    .\scripts\bootstrap.ps1 -Check
#>

[CmdletBinding()]
param(
    [switch]$Check,
    [switch]$SkipAzure
)

$ErrorActionPreference = 'Stop'
$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
Set-Location $RepoRoot

# ── Output helpers ─────────────────────────────────────────────
function Write-Header($msg) { Write-Host ""; Write-Host "== $msg ==" -ForegroundColor Cyan }
function Write-Info  ($msg) { Write-Host "i  $msg" -ForegroundColor Blue }
function Write-Ok    ($msg) { Write-Host "OK $msg" -ForegroundColor Green }
function Write-Warn  ($msg) { Write-Host "!! $msg" -ForegroundColor Yellow }
function Write-Err   ($msg) { Write-Host "XX $msg" -ForegroundColor Red }

function Test-Command($name) { [bool](Get-Command $name -ErrorAction SilentlyContinue) }

function Install-WithWinget {
    param([string]$Id, [string]$Friendly)
    if ($Check) { Write-Warn "$Friendly missing (skipping install — -Check mode)"; return }
    if (-not (Test-Command winget)) {
        Write-Warn "winget not available — install $Friendly manually."
        return
    }
    Write-Info "Installing $Friendly via winget…"
    winget install --id $Id --silent --accept-package-agreements --accept-source-agreements
}

Write-Info "Repo root: $RepoRoot"

# ── Required tools ─────────────────────────────────────────────
Write-Header "Checking prerequisites"

$missing = $false

# Git
if (Test-Command git) {
    Write-Ok ("git: " + ((git --version) -split ' ')[2])
} else {
    Write-Err "git not found"
    Install-WithWinget -Id 'Git.Git' -Friendly 'Git'
    if (-not (Test-Command git)) { $missing = $true }
}

# .NET 9 SDK
if (Test-Command dotnet) {
    $dotnetVer = (dotnet --version) 2>$null
    if ($dotnetVer -like '9.*') {
        Write-Ok ".NET SDK: $dotnetVer"
    } else {
        Write-Warn ".NET SDK present but version is $dotnetVer — project requires 9.x"
        Install-WithWinget -Id 'Microsoft.DotNet.SDK.9' -Friendly '.NET 9 SDK'
    }
} else {
    Write-Err ".NET SDK not found"
    Install-WithWinget -Id 'Microsoft.DotNet.SDK.9' -Friendly '.NET 9 SDK'
    if (-not (Test-Command dotnet)) { $missing = $true }
}

# Node 20+
if (Test-Command node) {
    $nodeVer = (node --version) -replace '^v',''
    $nodeMajor = [int]($nodeVer -split '\.')[0]
    if ($nodeMajor -ge 20) {
        Write-Ok "node: v$nodeVer"
    } else {
        Write-Warn "node v$nodeVer < 20.x"
        Install-WithWinget -Id 'OpenJS.NodeJS.LTS' -Friendly 'Node.js LTS'
    }
} else {
    Write-Err "node not found"
    Install-WithWinget -Id 'OpenJS.NodeJS.LTS' -Friendly 'Node.js LTS'
    if (-not (Test-Command node)) { $missing = $true }
}

# Docker (recommended)
if (Test-Command docker) {
    Write-Ok ("docker: " + ((docker --version) -split ' ')[2].TrimEnd(','))
} else {
    Write-Warn "docker not found — required for docker-compose.mcp.yml workflow"
    Write-Info "  Install Docker Desktop: https://www.docker.com/products/docker-desktop"
}

# Azure CLI
if (-not $SkipAzure) {
    if (Test-Command az) {
        Write-Ok "az (Azure CLI) installed"
    } else {
        Write-Warn "az (Azure CLI) not found"
        Install-WithWinget -Id 'Microsoft.AzureCLI' -Friendly 'Azure CLI'
    }
}

# Python 3 (for mkdocs and STIG/CCI generation scripts)
if (Test-Command python) {
    Write-Ok ("python: " + (python --version 2>&1))
} else {
    Write-Warn "python not found (needed for docs site + STIG/CCI scripts)"
    Install-WithWinget -Id 'Python.Python.3.11' -Friendly 'Python 3.11'
}

# dotnet-ef global tool
if (Test-Command dotnet) {
    $efInstalled = (dotnet tool list -g 2>$null | Select-String -Pattern '^dotnet-ef')
    if ($efInstalled) {
        Write-Ok "dotnet-ef installed globally"
    } else {
        if (-not $Check) {
            Write-Info "Installing dotnet-ef as a global tool…"
            try { dotnet tool install --global dotnet-ef --version 9.0.0 } catch { Write-Warn "dotnet-ef install failed" }
        } else {
            Write-Warn "dotnet-ef not installed (will install on full bootstrap)"
        }
    }
}

if ($missing -and -not $Check) {
    Write-Err "One or more required prerequisites could not be installed automatically."
    Write-Err "Resolve the items above and re-run .\scripts\bootstrap.ps1."
    exit 1
}

if ($Check) {
    Write-Header "Check complete"
    exit 0
}

# ── .env file ──────────────────────────────────────────────────
Write-Header "Environment configuration"
$envPath = Join-Path $RepoRoot '.env'
$envExample = Join-Path $RepoRoot '.env.example'
if (-not (Test-Path $envPath)) {
    if (Test-Path $envExample) {
        Copy-Item $envExample $envPath
        Write-Ok "Created .env from .env.example"
        Write-Warn "Edit .env to provide your Azure / OpenAI credentials before running the app"
    } else {
        Write-Warn ".env.example not found — skipping .env creation"
    }
} else {
    Write-Ok ".env already exists (not overwriting)"
}

# ── Restore .NET solution ──────────────────────────────────────
Write-Header "Restoring .NET solution"
if (Test-Command dotnet) {
    dotnet restore Ato.Copilot.sln
    Write-Ok "dotnet restore complete"
} else {
    Write-Warn "dotnet not on PATH — open a new shell after install and re-run"
}

# ── Restore Node sub-projects ──────────────────────────────────
function Restore-NodeProject($dir) {
    $lock = Join-Path $dir 'package-lock.json'
    $pkg  = Join-Path $dir 'package.json'
    if (Test-Path $lock) {
        Write-Info "npm ci in $dir"
        Push-Location $dir
        try { npm ci --no-audit --no-fund } finally { Pop-Location }
        Write-Ok "$dir installed"
    } elseif (Test-Path $pkg) {
        Write-Info "npm install in $dir (no lockfile)"
        Push-Location $dir
        try { npm install --no-audit --no-fund } finally { Pop-Location }
        Write-Ok "$dir installed"
    }
}

if (Test-Command npm) {
    Write-Header "Restoring Node sub-projects"
    Restore-NodeProject (Join-Path $RepoRoot 'extensions\vscode')
    Restore-NodeProject (Join-Path $RepoRoot 'extensions\m365')
    Restore-NodeProject (Join-Path $RepoRoot 'src\Ato.Copilot.Chat\ClientApp')
    Restore-NodeProject (Join-Path $RepoRoot 'src\Ato.Copilot.Dashboard')
} else {
    Write-Warn "npm not on PATH — skipping Node restores"
}

# ── Done ───────────────────────────────────────────────────────
Write-Header "Bootstrap complete"
Write-Ok "Next steps:"
Write-Host "    1. Edit .env with your Azure / OpenAI credentials"
Write-Host "    2. dotnet build Ato.Copilot.sln"
Write-Host "    3. dotnet test  Ato.Copilot.sln"
Write-Host "    4. docker compose -f docker-compose.mcp.yml up --build   (full stack)"
Write-Host ""
Write-Host "    See README.md and docs/dev/contributing.md for details."
