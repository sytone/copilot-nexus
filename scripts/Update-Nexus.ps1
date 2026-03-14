#Requires -Version 7.0

<#
.SYNOPSIS
    Builds Copilot Nexus and publishes versioned component payloads.

.DESCRIPTION
    This script follows the shim-first deployment flow:
    1) Build the solution with `nexus build` (unless -SkipBuild is used)
    2) Publish a new versioned payload with `nexus publish`
    3) Reconcile the `nexus` alias to the installed CLI shim path
    4) Stop Nexus service before publish when service lifecycle is affected
    5) If legacy install artifacts are detected, stop legacy processes and clean old artifacts
    6) Start Nexus service via installed shim after publish when service lifecycle is affected

.PARAMETER Configuration
    Build configuration: Debug or Release. Defaults to Release.

.PARAMETER Component
    Component to publish: nexus, app, cli, or both. Defaults to both.

.PARAMETER SkipBuild
    Skip build and only run publish.

.PARAMETER RestartService
    Force service stop/start for app/cli-only publishes. For nexus/both publishes, service stop/start is automatic.

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

function Invoke-SourceCliOrThrow {
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

function Invoke-InstalledCliOrThrow {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,
        [Parameter(Mandatory = $true)]
        [string]$FailureMessage
    )

    if (-not (Test-Path -LiteralPath $nexusCliExe)) {
        throw "Installed CLI shim not found: $nexusCliExe"
    }

    & $nexusCliExe @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FailureMessage (exit code $LASTEXITCODE)"
    }
}

function Ensure-NexusAlias {
    param(
        [Parameter(Mandatory = $true)]
        [string]$AliasTarget
    )

    if (-not (Test-Path -LiteralPath $AliasTarget)) {
        throw "Cannot set nexus alias. Target does not exist: $AliasTarget"
    }

    $resolvedTarget = (Resolve-Path -LiteralPath $AliasTarget).Path
    $alias = Get-Alias -Name nexus -ErrorAction SilentlyContinue
    $resolvedCurrent = $null

    if ($null -ne $alias) {
        try {
            $resolvedCurrent = (Resolve-Path -LiteralPath $alias.Definition -ErrorAction Stop).Path
        }
        catch {
            $resolvedCurrent = $alias.Definition
        }
    }

    if ($null -eq $alias) {
        Set-Alias -Name nexus -Value $AliasTarget -Scope Global
        Write-Host "Set nexus alias to installed shim: $AliasTarget" -ForegroundColor Green
        return
    }

    if (-not [string]::Equals($resolvedCurrent, $resolvedTarget, [System.StringComparison]::OrdinalIgnoreCase)) {
        Set-Alias -Name nexus -Value $AliasTarget -Scope Global
        Write-Host "Updated nexus alias to installed shim: $AliasTarget" -ForegroundColor Green
        return
    }

    Write-Host "nexus alias already points to installed shim: $AliasTarget" -ForegroundColor DarkGray
}

function Get-LegacyInstallArtifacts {
    param(
        [Parameter(Mandatory = $true)]
        [string]$InstallRoot
    )

    $artifacts = New-Object System.Collections.Generic.List[string]
    $legacyFolders = @(
        (Join-Path $InstallRoot 'cli'),
        (Join-Path $InstallRoot 'service'),
        (Join-Path $InstallRoot 'winapp'),
        (Join-Path $InstallRoot 'nexus')
    )

    foreach ($folder in $legacyFolders) {
        if (Test-Path -LiteralPath $folder -PathType Container) {
            $artifacts.Add((Resolve-Path -LiteralPath $folder).Path)
        }
    }

    $legacyRootFiles = Get-ChildItem -Path $InstallRoot -File -Filter 'CopilotNexus.*' -ErrorAction SilentlyContinue
    foreach ($file in $legacyRootFiles) {
        if ($file.Name -ne 'nexus.lock') {
            $artifacts.Add($file.FullName)
        }
    }

    return $artifacts | Sort-Object -Unique
}

function Stop-LegacyProcesses {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$LegacyArtifactPaths
    )

    $legacyRoots = @()
    foreach ($artifact in $LegacyArtifactPaths) {
        if (Test-Path -LiteralPath $artifact -PathType Container) {
            $legacyRoots += (Resolve-Path -LiteralPath $artifact).Path.TrimEnd('\')
        }
        else {
            $legacyRoots += (Split-Path -Path $artifact -Parent)
        }
    }

    $legacyRoots = $legacyRoots | Sort-Object -Unique
    if ($legacyRoots.Count -eq 0) {
        return
    }

    foreach ($process in Get-Process -ErrorAction SilentlyContinue) {
        $processPath = $null
        try {
            $processPath = $process.Path
        }
        catch {
            continue
        }

        if ([string]::IsNullOrWhiteSpace($processPath)) {
            continue
        }

        foreach ($root in $legacyRoots) {
            if ($processPath.StartsWith($root, [System.StringComparison]::OrdinalIgnoreCase)) {
                Write-Host "Stopping legacy process $($process.ProcessName) (PID $($process.Id)) from $processPath" -ForegroundColor Yellow
                Stop-Process -Id $process.Id -Force
                break
            }
        }
    }
}

function Remove-LegacyArtifacts {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$LegacyArtifactPaths
    )

    foreach ($artifact in $LegacyArtifactPaths) {
        if (-not (Test-Path -LiteralPath $artifact)) {
            continue
        }

        Write-Host "Removing legacy artifact: $artifact" -ForegroundColor Yellow
        Remove-Item -LiteralPath $artifact -Recurse -Force
    }
}

function Try-StopService {
    if (Test-Path -LiteralPath $nexusCliExe) {
        & $nexusCliExe stop | Out-Null
    }
    else {
        & dotnet run --project $cliProject -- stop | Out-Null
    }
}

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $scriptRoot
$cliProject = Join-Path $projectRoot 'src/CopilotNexus.Cli/CopilotNexus.Cli.csproj'
$installRoot = Join-Path $env:LOCALAPPDATA 'CopilotNexus'
$nexusCliExe = Join-Path $installRoot 'app\cli\CopilotNexus.Cli.exe'
$legacyArtifacts = @()
$legacyCleanupRequired = $false

Write-Host 'Copilot Nexus Publish Script' -ForegroundColor Cyan
Write-Host "Project root: $projectRoot" -ForegroundColor DarkGray

if (-not (Test-Path (Join-Path $projectRoot 'CopilotNexus.slnx'))) {
    throw "Solution file not found under project root: $projectRoot"
}

if (-not (Test-Path $cliProject)) {
    throw "CLI project not found: $cliProject"
}

$legacyArtifacts = Get-LegacyInstallArtifacts -InstallRoot $installRoot
$legacyCleanupRequired = $legacyArtifacts.Count -gt 0

if ($legacyCleanupRequired) {
    Write-Host "`nLegacy install artifacts detected. Entering cleanup flow." -ForegroundColor Yellow
    $legacyArtifacts | ForEach-Object { Write-Host "  - $_" -ForegroundColor DarkGray }
}
else {
    Write-Host "`nNo legacy install artifacts detected. Running standard publish flow." -ForegroundColor DarkGray
}

Push-Location $projectRoot
try {
    if (-not $SkipBuild) {
        Write-Host "`nBuilding solution ($Configuration)..." -ForegroundColor Yellow
        Invoke-SourceCliOrThrow -Arguments @('build', '-c', $Configuration) -FailureMessage 'Build failed'
        Write-Host 'Build succeeded' -ForegroundColor Green
    }
    else {
        Write-Host "`nSkipping build as requested." -ForegroundColor Yellow
    }

    $serviceLifecycleRequired = ($Component -eq 'nexus' -or $Component -eq 'both') -or $RestartService

    if ($serviceLifecycleRequired -or $legacyCleanupRequired) {
        Write-Host "`nStopping Nexus service before publish..." -ForegroundColor Yellow
        Try-StopService
    }

    if ($legacyCleanupRequired) {
        Write-Host "Stopping legacy processes before cleanup..." -ForegroundColor Yellow
        Stop-LegacyProcesses -LegacyArtifactPaths $legacyArtifacts
    }

    Write-Host "`nPublishing component: $Component" -ForegroundColor Yellow
    Invoke-SourceCliOrThrow -Arguments @('publish', '--component', $Component) -FailureMessage 'Publish failed'

    if ($legacyCleanupRequired) {
        Write-Host "`nCleaning legacy install artifacts..." -ForegroundColor Yellow
        Remove-LegacyArtifacts -LegacyArtifactPaths $legacyArtifacts
    }

    Ensure-NexusAlias -AliasTarget $nexusCliExe

    if ($serviceLifecycleRequired -or $legacyCleanupRequired) {
        Write-Host "`nStarting Nexus service via installed shim..." -ForegroundColor Yellow
        Invoke-InstalledCliOrThrow -Arguments @('start') -FailureMessage 'Service start via shim failed'
        Write-Host 'Service started via shim' -ForegroundColor Green
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
