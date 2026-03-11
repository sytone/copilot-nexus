#Requires -Version 7.0

<#
.SYNOPSIS
    Installs Copilot Nexus and configures the PowerShell nexus alias.

.DESCRIPTION
    This script follows docs/installation-and-operations.md:
    1) Optional solution build
    2) Run `dotnet run --project src/CopilotNexus.Cli -- install`
    3) Create a `nexus` alias for the current session
    4) Optionally persist the alias to $PROFILE with -AddToProfile

.PARAMETER Configuration
    Build configuration: Debug or Release. Defaults to Release.

.PARAMETER NoBuild
    Skip the build step and run install only.

.PARAMETER AddToProfile
    Persist the alias in $PROFILE in addition to setting it for the current session.

.EXAMPLE
    .\Install-Nexus.ps1

.EXAMPLE
    .\Install-Nexus.ps1 -Configuration Debug

.EXAMPLE
    .\Install-Nexus.ps1 -NoBuild -AddToProfile
#>

param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$NoBuild,
    [switch]$AddToProfile
)

$ErrorActionPreference = 'Stop'

function Invoke-DotNetOrThrow {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,
        [Parameter(Mandatory = $true)]
        [string]$FailureMessage
    )

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FailureMessage (exit code $LASTEXITCODE)"
    }
}

function Ensure-PersistentNexusAlias {
    param(
        [Parameter(Mandatory = $true)]
        [string]$AliasTarget
    )

    $profileDir = Split-Path -Parent $PROFILE
    if (-not (Test-Path $profileDir)) {
        New-Item -Path $profileDir -ItemType Directory -Force | Out-Null
    }

    if (-not (Test-Path $PROFILE)) {
        New-Item -Path $PROFILE -ItemType File -Force | Out-Null
    }

    $profileContent = Get-Content -Path $PROFILE -Raw
    $aliasLine = "Set-Alias nexus '$AliasTarget'"

    # Replace any existing nexus alias line to avoid duplicate/conflicting definitions.
    if ($profileContent -match '(?m)^\s*Set-Alias\s+nexus\b.*$') {
        $updatedProfile = [regex]::Replace(
            $profileContent,
            '(?m)^\s*Set-Alias\s+nexus\b.*$',
            $aliasLine,
            1
        )
        Set-Content -Path $PROFILE -Value $updatedProfile
    }
    else {
        if ($profileContent -and -not $profileContent.EndsWith([Environment]::NewLine)) {
            Add-Content -Path $PROFILE -Value [Environment]::NewLine
        }

        Add-Content -Path $PROFILE -Value "# Copilot Nexus CLI alias"
        Add-Content -Path $PROFILE -Value $aliasLine
    }
}

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $scriptRoot
$slnFile = Join-Path $projectRoot 'CopilotNexus.slnx'
$cliProject = Join-Path $projectRoot 'src/CopilotNexus.Cli/CopilotNexus.Cli.csproj'
$nexusCliExe = Join-Path $env:LOCALAPPDATA 'CopilotNexus\cli\CopilotNexus.Cli.exe'

Write-Host 'Copilot Nexus Installer' -ForegroundColor Cyan
Write-Host "Project root: $projectRoot" -ForegroundColor DarkGray

if (-not (Test-Path $cliProject)) {
    throw "CLI project not found: $cliProject"
}

if (-not $NoBuild) {
    Write-Host "`nBuilding solution ($Configuration)..." -ForegroundColor Yellow

    if (-not (Test-Path $slnFile)) {
        throw "Solution file not found: $slnFile"
    }

    Invoke-DotNetOrThrow -Arguments @('build', $slnFile, '-c', $Configuration, '--nologo', '-v', 'q') -FailureMessage 'Build failed'
    Write-Host 'Build succeeded' -ForegroundColor Green
}

Write-Host "`nRunning nexus install..." -ForegroundColor Yellow
Invoke-DotNetOrThrow -Arguments @('run', '--project', $cliProject, '--', 'install') -FailureMessage 'nexus install failed'

if (-not (Test-Path $nexusCliExe)) {
    throw "Installed CLI executable not found: $nexusCliExe"
}

# Make alias available right away in this session.
Set-Alias nexus $nexusCliExe

if ($AddToProfile) {
    Ensure-PersistentNexusAlias -AliasTarget $nexusCliExe
    Write-Host "Alias persisted to profile: $PROFILE" -ForegroundColor Green
}
else {
    Write-Host 'Alias set for current session only. Use -AddToProfile to persist it.' -ForegroundColor Yellow
}

Write-Host "`nVerifying installed CLI..." -ForegroundColor Yellow
& $nexusCliExe --help | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "Installed CLI failed to execute (exit code $LASTEXITCODE)"
}

Write-Host "`nInstallation complete." -ForegroundColor Green
Write-Host 'Next steps:' -ForegroundColor Cyan
Write-Host '  nexus start' -ForegroundColor DarkGray
Write-Host '  nexus status' -ForegroundColor DarkGray
Write-Host '  nexus winapp start' -ForegroundColor DarkGray
