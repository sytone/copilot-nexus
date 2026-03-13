#Requires -Version 7.0

<#
.SYNOPSIS
    Enables repository-managed git hooks for Copilot Nexus.

.DESCRIPTION
    Configures git to use the versioned .githooks/ directory so commit/push
    workflow guards (including pre-push cleanliness checks) are enforced.
#>

$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot
$hooksPath = Join-Path $repoRoot '.githooks'

if (-not (Test-Path $hooksPath)) {
    throw "Hooks directory not found: $hooksPath"
}

Push-Location $repoRoot
try {
    git config core.hooksPath .githooks
}
finally {
    Pop-Location
}

Write-Host "Configured git hooks path to .githooks for repo: $repoRoot" -ForegroundColor Green
Write-Host "Verify with: git config --get core.hooksPath" -ForegroundColor DarkGray
