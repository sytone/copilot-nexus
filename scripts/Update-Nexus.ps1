#Requires -Version 7.0

<#
.SYNOPSIS
    Builds Copilot Nexus and publishes versioned component payloads.

.DESCRIPTION
    This script follows the shim-first deployment flow:
    1) Build the solution with `nexus build` (unless -SkipBuild is used)
    2) Publish a new versioned payload with `nexus publish`
    3) Optionally restart Nexus service so the shim launches the newest version

.PARAMETER Configuration
    Build configuration: Debug or Release. Defaults to Release.

.PARAMETER Component
    Component to publish: nexus, app, cli, or both. Defaults to both.

.PARAMETER SkipBuild
    Skip build and only run publish.

.PARAMETER RestartService
    Restart Nexus service after publish so it runs the newest version immediately.

.EXAMPLE
    .\Update-Nexus.ps1

.EXAMPLE
    .\Update-Nexus.ps1 -Component app

.EXAMPLE
    .\Update-Nexus.ps1 -Component nexus -RestartService
#>

param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [ValidateSet('nexus', 'app', 'cli', 'both')]
    [string]$Component = 'both',

    [switch]$SkipBuild,
    [switch]$RestartService
)

$ErrorActionPreference = 'Stop'

function Invoke-NexusOrThrow {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,
        [Parameter(Mandatory = $true)]
        [string]$FailureMessage
    )

    & dotnet run --project $cliProject -- @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FailureMessage (exit code $LASTEXITCODE)"
    }
}

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $scriptRoot
$cliProject = Join-Path $projectRoot 'src/CopilotNexus.Cli/CopilotNexus.Cli.csproj'

Write-Host 'Copilot Nexus Publish Script' -ForegroundColor Cyan
Write-Host "Project root: $projectRoot" -ForegroundColor DarkGray

if (-not (Test-Path (Join-Path $projectRoot 'CopilotNexus.slnx'))) {
    throw "Solution file not found under project root: $projectRoot"
}

if (-not (Test-Path $cliProject)) {
    throw "CLI project not found: $cliProject"
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

    Write-Host "`nPublishing component: $Component" -ForegroundColor Yellow
    Invoke-NexusOrThrow -Arguments @('publish', '--component', $Component) -FailureMessage 'Publish failed'

    if ($RestartService -and ($Component -eq 'nexus' -or $Component -eq 'both')) {
        Write-Host "`nRestarting Nexus service to load latest version..." -ForegroundColor Yellow
        & dotnet run --project $cliProject -- stop | Out-Null
        Invoke-NexusOrThrow -Arguments @('start') -FailureMessage 'Service restart failed'
        Write-Host 'Service restarted' -ForegroundColor Green
    }
}
finally {
    Pop-Location
}

Write-Host "`nPublish complete." -ForegroundColor Green
Write-Host 'Published payloads are available immediately through shims.' -ForegroundColor DarkGray
Write-Host 'Next steps:' -ForegroundColor Cyan
if ($Component -eq 'nexus' -or $Component -eq 'both') {
    Write-Host '  nexus stop' -ForegroundColor DarkGray
    Write-Host '  nexus start' -ForegroundColor DarkGray
}
if ($Component -eq 'app' -or $Component -eq 'both') {
    Write-Host '  nexus winapp start' -ForegroundColor DarkGray
}
