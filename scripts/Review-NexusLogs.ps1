#Requires -Version 7.0

<#
.SYNOPSIS
    Reviews Copilot Nexus logs since the last recorded checkpoint.

.DESCRIPTION
    Scans app/service/CLI logs under %LOCALAPPDATA%\CopilotNexus\logs and reports
    warning/error entries newer than the timestamp stored in:
    %USERPROFILE%\.copilot\copilot-nexus\log-review-state.json

    By default, the script updates the state file with a new lastReviewedUtc value
    after each run.

.PARAMETER LogsPath
    Log directory to scan. Defaults to %LOCALAPPDATA%\CopilotNexus\logs.

.PARAMETER StatePath
    State file containing lastReviewedUtc. Defaults to:
    %USERPROFILE%\.copilot\copilot-nexus\log-review-state.json

.PARAMETER MaxIssues
    Maximum issue rows to print.

.PARAMETER NoStateUpdate
    Do not write a new lastReviewedUtc checkpoint.

.PARAMETER FailOnIssues
    Exit with code 2 when warnings/errors are found.
#>

param(
    [string]$LogsPath = (Join-Path $env:LOCALAPPDATA 'CopilotNexus\logs'),
    [string]$StatePath = (Join-Path $env:USERPROFILE '.copilot\copilot-nexus\log-review-state.json'),
    [int]$MaxIssues = 100,
    [switch]$NoStateUpdate,
    [switch]$FailOnIssues
)

$ErrorActionPreference = 'Stop'

function Convert-ToUtcOrDefault {
    param(
        [string]$Value,
        [datetime]$Default
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return $Default
    }

    $dto = [DateTimeOffset]::MinValue
    if ([DateTimeOffset]::TryParse(
            $Value,
            [System.Globalization.CultureInfo]::InvariantCulture,
            [System.Globalization.DateTimeStyles]::AssumeUniversal,
            [ref]$dto)) {
        return $dto.UtcDateTime
    }

    return $Default
}

function Read-ReviewState {
    param([string]$Path)

    $defaultState = [pscustomobject]@{
        lastReviewedUtc = '1970-01-01T00:00:00Z'
    }

    if (-not (Test-Path $Path)) {
        return $defaultState
    }

    try {
        return Get-Content -Path $Path -Raw | ConvertFrom-Json
    }
    catch {
        Write-Warning "Failed to parse state file at $Path. Resetting checkpoint."
        return $defaultState
    }
}

function Try-ParseLineTimestampUtc {
    param(
        [string]$Line,
        [ref]$TimestampUtc
    )

    $TimestampUtc.Value = $null
    if ([string]::IsNullOrWhiteSpace($Line)) {
        return $false
    }

    $match = [regex]::Match(
        $Line,
        '^(?<ts>\d{4}-\d{2}-\d{2}[ T]\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:\s?(?:Z|[+\-]\d{2}:\d{2}))?)')

    if (-not $match.Success) {
        return $false
    }

    $dto = [DateTimeOffset]::MinValue
    if (-not [DateTimeOffset]::TryParse(
            $match.Groups['ts'].Value,
            [System.Globalization.CultureInfo]::InvariantCulture,
            [System.Globalization.DateTimeStyles]::AssumeUniversal,
            [ref]$dto)) {
        return $false
    }

    $TimestampUtc.Value = $dto.UtcDateTime
    return $true
}

function Get-LineSeverity {
    param([string]$Line)

    $levelMatch = [regex]::Match(
        $Line,
        '^\d{4}-\d{2}-\d{2}[ T]\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:\s?(?:Z|[+\-]\d{2}:\d{2}))?\s+\[(?<lvl>[A-Z]{3})\]')

    if ($levelMatch.Success) {
        $level = $levelMatch.Groups['lvl'].Value
        if ($level -eq 'ERR' -or $level -eq 'FTL') {
            return 'Error'
        }

        if ($level -eq 'WRN') {
            return 'Warning'
        }

        return $null
    }

    if ($Line -match '(?i)\b(error|exception|failed|fatal|timeout|unhandled)\b') {
        return 'Error'
    }

    if ($Line -match '(?i)\b(warning|warn)\b') {
        return 'Warning'
    }

    return $null
}

$componentPatterns = [ordered]@{
    App     = 'copilot-nexus-*.log'
    Service = 'nexus-*.log'
    Cli     = 'cli-*.log'
}

$state = Read-ReviewState -Path $StatePath
$reviewStartUtc = Convert-ToUtcOrDefault -Value $state.lastReviewedUtc -Default ([datetime]'1970-01-01T00:00:00Z')
$nowUtc = [DateTime]::UtcNow

Write-Host "Copilot Nexus log review" -ForegroundColor Cyan
Write-Host "Logs path: $LogsPath" -ForegroundColor DarkGray
Write-Host "Review start (UTC): $($reviewStartUtc.ToString('o'))" -ForegroundColor DarkGray

if (-not (Test-Path $LogsPath)) {
    Write-Warning "Log directory does not exist: $LogsPath"
}

$entries = New-Object System.Collections.Generic.List[object]

foreach ($entry in $componentPatterns.GetEnumerator()) {
    $component = $entry.Key
    $pattern = $entry.Value

    $files = @(
        Get-ChildItem -Path $LogsPath -Filter $pattern -File -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTimeUtc
    )

    foreach ($file in $files) {
        $fileHasUpdates = $file.LastWriteTimeUtc -gt $reviewStartUtc
        if (-not $fileHasUpdates) {
            continue
        }

        $lineNumber = 0
        foreach ($line in Get-Content -Path $file.FullName -ErrorAction SilentlyContinue) {
            $lineNumber++

            $lineTimestampUtc = $null
            $hasLineTimestamp = Try-ParseLineTimestampUtc -Line $line -TimestampUtc ([ref]$lineTimestampUtc)
            if (-not $hasLineTimestamp) {
                continue
            }

            if ($lineTimestampUtc -le $reviewStartUtc) {
                continue
            }

            $entries.Add([pscustomobject]@{
                    Component    = $component
                    File         = $file.Name
                    LineNumber   = $lineNumber
                    TimestampUtc = $lineTimestampUtc
                    Line         = $line
                })
        }
    }
}

$issues = foreach ($entry in $entries) {
    $severity = Get-LineSeverity -Line $entry.Line

    if ($null -eq $severity) {
        continue
    }

    [pscustomobject]@{
        Component    = $entry.Component
        Severity     = $severity
        TimestampUtc = $entry.TimestampUtc
        File         = $entry.File
        LineNumber   = $entry.LineNumber
        Message      = if ($entry.Line.Length -gt 180) { $entry.Line.Substring(0, 180) + '...' } else { $entry.Line }
    }
}

$errorCount = @($issues | Where-Object Severity -eq 'Error').Count
$warningCount = @($issues | Where-Object Severity -eq 'Warning').Count

Write-Host "`nScanned entries: $($entries.Count)" -ForegroundColor DarkGray
Write-Host "Detected issues: $($issues.Count) (errors: $errorCount, warnings: $warningCount)" -ForegroundColor DarkGray

if ($issues.Count -gt 0) {
    Write-Host "`nIssues since last review (showing up to $MaxIssues):" -ForegroundColor Yellow
    $issues |
        Select-Object -First $MaxIssues |
        Format-Table -AutoSize Component, Severity, TimestampUtc, File, LineNumber, Message
}
else {
    Write-Host "`nNo warning/error entries found since the last checkpoint." -ForegroundColor Green
}

if (-not $NoStateUpdate) {
    $stateDir = Split-Path -Parent $StatePath
    if (-not (Test-Path $stateDir)) {
        New-Item -Path $stateDir -ItemType Directory -Force | Out-Null
    }

    $newState = [ordered]@{
        lastReviewedUtc = $nowUtc.ToString('o')
        previousReviewedUtc = $reviewStartUtc.ToString('o')
        logsPath = $LogsPath
        scannedEntries = $entries.Count
        detectedIssues = $issues.Count
        detectedErrors = $errorCount
        detectedWarnings = $warningCount
    }

    $newState | ConvertTo-Json | Set-Content -Path $StatePath -Encoding UTF8
    Write-Host "Updated review state: $StatePath" -ForegroundColor Green
}
else {
    Write-Host "State file was not updated (-NoStateUpdate)." -ForegroundColor Yellow
}

if ($FailOnIssues -and $issues.Count -gt 0) {
    exit 2
}
