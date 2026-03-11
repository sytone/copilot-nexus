#Requires -Version 7.0

<#
.SYNOPSIS
    Uninstalls Copilot Nexus from the current user's local app data folder.

.DESCRIPTION
    Stops running Copilot Nexus processes (service and desktop app), cleans up
    lock artifacts, and removes %LOCALAPPDATA%\CopilotNexus.

.PARAMETER KeepData
    Keeps the install directory and only stops running processes.

.EXAMPLE
    .\Uninstall-Nexus.ps1

.EXAMPLE
    .\Uninstall-Nexus.ps1 -KeepData
#>

[CmdletBinding()]
param(
    [switch]$KeepData
)

$ErrorActionPreference = 'Stop'

$installRoot = Join-Path $env:LOCALAPPDATA 'CopilotNexus'
$lockFile = Join-Path $installRoot 'nexus.lock'

function Stop-ProcessSafe {
    param(
        [Parameter(Mandatory = $true)]
        [System.Diagnostics.Process]$Process,
        [string]$Label = 'process'
    )

    try {
        if (-not $Process.HasExited) {
            Stop-Process -Id $Process.Id -Force -ErrorAction Stop
            Write-Host "Stopped $Label (PID $($Process.Id))." -ForegroundColor Green
        }
    }
    catch {
        Write-Host "Failed to stop $Label (PID $($Process.Id)): $($_.Exception.Message)" -ForegroundColor Yellow
    }
}

function Stop-ByName {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$ProcessNames
    )

    foreach ($name in $ProcessNames) {
        $procs = Get-Process -Name $name -ErrorAction SilentlyContinue
        if (-not $procs) {
            continue
        }

        foreach ($proc in $procs) {
            Stop-ProcessSafe -Process $proc -Label $name
        }
    }
}

Write-Host 'Copilot Nexus Uninstaller' -ForegroundColor Cyan
Write-Host "Install root: $installRoot" -ForegroundColor DarkGray

if (-not (Test-Path $installRoot)) {
    Write-Host 'Nothing to uninstall. Install directory was not found.' -ForegroundColor Yellow
    return
}

# Attempt graceful service stop if nexus command is available.
if (Get-Command nexus -ErrorAction SilentlyContinue) {
    try {
        & nexus stop | Out-Null
        if ($LASTEXITCODE -eq 0) {
            Write-Host 'Requested Nexus service stop via CLI.' -ForegroundColor Green
        }
    }
    catch {
        Write-Host 'nexus stop failed; falling back to process termination.' -ForegroundColor Yellow
    }
}

# Stop service by lock file PID if present.
if (Test-Path $lockFile) {
    try {
        $pidText = (Get-Content -Path $lockFile -Raw).Trim()
        $pid = 0
        if ([int]::TryParse($pidText, [ref]$pid)) {
            $proc = Get-Process -Id $pid -ErrorAction SilentlyContinue
            if ($proc) {
                Stop-ProcessSafe -Process $proc -Label 'CopilotNexus.Service (lock file PID)'
            }
        }
    }
    catch {
        Write-Host "Unable to process lock file '$lockFile': $($_.Exception.Message)" -ForegroundColor Yellow
    }
}

# Force stop known Nexus processes.
Stop-ByName -ProcessNames @('CopilotNexus.Service', 'CopilotNexus.App', 'CopilotNexus.Updater')

# Best-effort short wait for process exits to settle.
Start-Sleep -Milliseconds 500

if (Test-Path $lockFile) {
    Remove-Item -Path $lockFile -Force -ErrorAction SilentlyContinue
}

if ($KeepData) {
    Write-Host 'Stopped running processes. Install directory preserved due to -KeepData.' -ForegroundColor Yellow
    return
}

Write-Host 'Removing install directory...' -ForegroundColor Yellow
Remove-Item -Path $installRoot -Recurse -Force

Write-Host 'Uninstall complete.' -ForegroundColor Green
