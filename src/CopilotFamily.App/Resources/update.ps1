# Copilot Family — Updater Script
# Launched by the app before exit. Waits for the process to close, copies
# staged files to the install directory, clears staging, and relaunches.
#
# Usage: update.ps1 -AppPid <PID> -InstallPath <path> -StagingPath <path> -AppExe <path>

param(
    [Parameter(Mandatory)][int]$AppPid,
    [Parameter(Mandatory)][string]$InstallPath,
    [Parameter(Mandatory)][string]$StagingPath,
    [Parameter(Mandatory)][string]$AppExe
)

$LogFile = Join-Path (Split-Path $InstallPath -Parent) "logs" "update.log"

function Write-Log($msg) {
    $ts = Get-Date -Format "yyyy-MM-dd HH:mm:ss.fff"
    $line = "[$ts] $msg"
    Write-Host $line
    Add-Content -Path $LogFile -Value $line -ErrorAction SilentlyContinue
}

Write-Log "Updater started. Waiting for PID $AppPid to exit..."

# Wait for the app to exit (up to 30 seconds)
$waited = 0
while ($waited -lt 30) {
    try {
        $proc = Get-Process -Id $AppPid -ErrorAction SilentlyContinue
        if ($null -eq $proc) {
            Write-Log "App process exited."
            break
        }
    } catch {
        Write-Log "App process exited (not found)."
        break
    }
    Start-Sleep -Seconds 1
    $waited++
}

if ($waited -ge 30) {
    Write-Log "ERROR: App did not exit within 30 seconds. Aborting update."
    exit 1
}

# Small extra delay for file handles to release
Start-Sleep -Seconds 1

# Check if staging has files
$stagingFiles = Get-ChildItem -Path $StagingPath -Recurse -File -ErrorAction SilentlyContinue
if ($null -eq $stagingFiles -or $stagingFiles.Count -eq 0) {
    Write-Log "WARNING: Staging folder is empty. Skipping copy."
} else {
    Write-Log "Copying $($stagingFiles.Count) files from staging to dist..."

    # Copy with retry for locked files
    $maxRetries = 10
    $success = $false

    for ($attempt = 1; $attempt -le $maxRetries; $attempt++) {
        try {
            Copy-Item -Path (Join-Path $StagingPath "*") -Destination $InstallPath -Recurse -Force -ErrorAction Stop
            $success = $true
            Write-Log "Copy succeeded on attempt $attempt."
            break
        } catch {
            Write-Log "Copy attempt $attempt failed: $_"
            if ($attempt -lt $maxRetries) {
                Start-Sleep -Seconds 1
            }
        }
    }

    if (-not $success) {
        Write-Log "ERROR: Failed to copy staging files after $maxRetries attempts."
        exit 2
    }

    # Clear staging
    try {
        Get-ChildItem -Path $StagingPath -Recurse | Remove-Item -Recurse -Force -ErrorAction Stop
        Write-Log "Staging folder cleared."
    } catch {
        Write-Log "WARNING: Could not fully clear staging: $_"
    }
}

# Relaunch the app
Write-Log "Launching $AppExe..."
Start-Process -FilePath $AppExe
Write-Log "Updater complete."
exit 0
