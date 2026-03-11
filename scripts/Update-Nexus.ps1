#Requires -Version 7.0

<#
.SYNOPSIS
    Builds Copilot Nexus and publishes staged updates.

.DESCRIPTION
    This script follows docs/installation-and-operations.md update flow:
    1) Build the solution with `nexus build`
    2) Publish updates to staging with `nexus publish`

    After this script completes, run `nexus update` (or `nexus update --component ...`)
    to apply staged updates from %LOCALAPPDATA%\CopilotNexus\staging.

.PARAMETER Configuration
    Build configuration: Debug or Release. Defaults to Release.

.PARAMETER Component
    Component to publish: nexus, app, or both. Defaults to both.

.PARAMETER SkipBuild
    Skip build and only publish to staging.

.EXAMPLE
    .\Update-Nexus.ps1

.EXAMPLE
    .\Update-Nexus.ps1 -Component app

.EXAMPLE
    .\Update-Nexus.ps1 -Configuration Debug -Component nexus
#>

param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [ValidateSet('nexus', 'app', 'both')]
    [string]$Component = 'both',

    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'

function Invoke-NexusOrThrow {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,
        [Parameter(Mandatory = $true)]
        [string]$FailureMessage
    )

    & nexus @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FailureMessage (exit code $LASTEXITCODE)"
    }
}

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $scriptRoot
$defaultNexusExe = Join-Path $env:LOCALAPPDATA 'CopilotNexus\cli\CopilotNexus.Cli.exe'

Write-Host 'Copilot Nexus Update Publisher' -ForegroundColor Cyan
Write-Host "Project root: $projectRoot" -ForegroundColor DarkGray

if (-not (Test-Path (Join-Path $projectRoot 'CopilotNexus.slnx'))) {
    throw "Solution file not found under project root: $projectRoot"
}

if (Test-Path $defaultNexusExe) {
    # Always prefer the installed CLI path so stale user aliases don't point to old locations.
    Set-Alias nexus $defaultNexusExe
    Write-Host "Using installed nexus CLI: $defaultNexusExe" -ForegroundColor Yellow
}
elseif (-not (Get-Command nexus -ErrorAction SilentlyContinue)) {
    throw "'nexus' command not found and default install was not found at: $defaultNexusExe"
}

Push-Location $projectRoot
try {
    if (-not $SkipBuild) {
        Write-Host "`nBuilding solution ($Configuration)..." -ForegroundColor Yellow
        Invoke-NexusOrThrow -Arguments @('build', '-c', $Configuration) -FailureMessage 'Build failed'
        Write-Host 'Build succeeded' -ForegroundColor Green
    }
    else {
        Write-Host "`nSkipping build as requested." -ForegroundColor Yellow
    }

    Write-Host "`nPublishing staged update for component: $Component" -ForegroundColor Yellow
    Invoke-NexusOrThrow -Arguments @('publish', '--component', $Component) -FailureMessage 'Publish failed'
}
finally {
    Pop-Location
}

Write-Host "`nStaged update ready." -ForegroundColor Green
Write-Host 'Next step:' -ForegroundColor Cyan
Write-Host "  nexus update --component $Component" -ForegroundColor DarkGray
