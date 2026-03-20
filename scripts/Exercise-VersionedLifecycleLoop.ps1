#Requires -Version 7.0

<#
.SYNOPSIS
    Stress-tests Copilot Nexus versioned publish/start/restart behavior via shims.

.DESCRIPTION
    Runs repeated lifecycle cycles in an isolated install root:
    - build
    - install
    - publish
    - start service + app
    - publish again
    - restart service + app
    - publish again
    - restart service + app

    The script fails fast on command errors and scans command/log output for lock/load
    failure signatures after each cycle.

.PARAMETER Configuration
    Build configuration (Debug or Release). Defaults to Release.

.PARAMETER Iterations
    Number of full publish/start/restart cycles to run. Defaults to 2.

.PARAMETER TestRoot
    Isolated Copilot Nexus root folder. Defaults to a temp folder.

.PARAMETER ServiceUrl
    Service URL for the loop run. Defaults to http://localhost:5380.

.PARAMETER SkipBuild
    Skip solution build.

.PARAMETER KeepTestRoot
    Preserve test root contents after completion.
#>

[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [ValidateRange(1, 50)]
    [int]$Iterations = 2,

    [string]$TestRoot = (Join-Path $env:TEMP ("CopilotNexus-loop-" + [Guid]::NewGuid().ToString('N'))),

    [string]$ServiceUrl = 'http://localhost:5380',

    [switch]$SkipBuild,
    [switch]$KeepTestRoot
)

$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $scriptRoot
$solutionPath = Join-Path $projectRoot 'CopilotNexus.slnx'
$cliProjectPath = Join-Path $projectRoot 'src\CopilotNexus.Cli\CopilotNexus.Cli.csproj'
$runLogPath = Join-Path $TestRoot 'loop-run.log'
$cliShimPath = Join-Path $TestRoot 'app\cli\CopilotNexus.Cli.exe'
$serviceRoot = Join-Path $TestRoot 'app\service'
$appRoot = Join-Path $TestRoot 'app\winapp'
$logsPath = Join-Path $TestRoot 'logs'
$rootOverrideVar = 'COPILOT_NEXUS_ROOT'
$lastExitCode = 0
$completedSuccessfully = $false

$issuePatterns = @(
    '(?i)skipping locked( shim)? file',
    '(?i)the process cannot access the file',
    '(?i)access is denied',
    '(?i)failed to initialize',
    '(?i)could not load file or assembly',
    '(?i)no connection could be made because the target machine actively refused it',
    '(?i)nexus failed start validation'
)

function Write-Section {
    param([Parameter(Mandatory = $true)][string]$Text)

    Write-Host "`n=== $Text ===" -ForegroundColor Cyan
    Add-Content -Path $runLogPath -Value ("`n=== {0:u} :: {1} ===" -f [DateTime]::UtcNow, $Text)
}

function Invoke-ExternalCommand {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][string]$StepName,
        [int[]]$AllowedExitCodes = @(0)
    )

    $commandLine = "$FilePath $($Arguments -join ' ')"
    Write-Host "-> $StepName" -ForegroundColor Yellow
    Write-Host "   $commandLine" -ForegroundColor DarkGray
    Add-Content -Path $runLogPath -Value ("[{0:u}] {1}`n{2}" -f [DateTime]::UtcNow, $StepName, $commandLine)

    $outputLines = & $FilePath @Arguments 2>&1
    $exitCode = $LASTEXITCODE
    if ($null -eq $exitCode) { $exitCode = 0 }
    $script:lastExitCode = $exitCode

    if ($outputLines) {
        $outputLines | ForEach-Object { Write-Host $_ }
        Add-Content -Path $runLogPath -Value ($outputLines -join [Environment]::NewLine)
    }

    Add-Content -Path $runLogPath -Value ("[exit={0}]" -f $exitCode)

    if (-not ($AllowedExitCodes -contains $exitCode)) {
        throw "Step '$StepName' failed with exit code $exitCode."
    }
}

function Get-ProcessPathSafe {
    param([Parameter(Mandatory = $true)][System.Diagnostics.Process]$Process)

    try {
        return $Process.Path
    }
    catch {
        return $null
    }
}

function Get-ProcessesUnderPath {
    param(
        [Parameter(Mandatory = $true)][string]$PathPrefix,
        [string[]]$ProcessNames = @()
    )

    $normalizedPrefix = [IO.Path]::GetFullPath($PathPrefix).TrimEnd('\') + '\'
    $all = Get-Process -ErrorAction SilentlyContinue

    return $all | Where-Object {
        if ($ProcessNames.Count -gt 0 -and $ProcessNames -notcontains $_.ProcessName) {
            return $false
        }

        $procPath = Get-ProcessPathSafe -Process $_
        if ([string]::IsNullOrWhiteSpace($procPath)) {
            return $false
        }

        $procPath.StartsWith($normalizedPrefix, [System.StringComparison]::OrdinalIgnoreCase)
    }
}

function Stop-ProcessesByPath {
    param(
        [Parameter(Mandatory = $true)][string]$PathPrefix,
        [string[]]$ProcessNames = @()
    )

    $targets = Get-ProcessesUnderPath -PathPrefix $PathPrefix -ProcessNames $ProcessNames
    foreach ($process in $targets) {
        Write-Host ("Stopping {0} (PID {1})" -f $process.ProcessName, $process.Id) -ForegroundColor Yellow
        try {
            Stop-Process -Id $process.Id -Force -ErrorAction Stop
        }
        catch {
            Write-Host ("Failed to stop PID {0}: {1}" -f $process.Id, $_.Exception.Message) -ForegroundColor Yellow
        }
    }
}

function Wait-ForServiceHealth {
    param(
        [Parameter(Mandatory = $true)][string]$Url,
        [int]$TimeoutSeconds = 30
    )

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        try {
            $response = Invoke-WebRequest -Uri ($Url.TrimEnd('/') + '/health') -UseBasicParsing -TimeoutSec 3
            if ($response.StatusCode -eq 200) {
                return
            }
        }
        catch {
            Start-Sleep -Milliseconds 400
            continue
        }

        Start-Sleep -Milliseconds 400
    }

    throw "Service health check timed out for $Url after ${TimeoutSeconds}s."
}

function Invoke-NexusFromSource {
    param(
        [Parameter(Mandatory = $true)][string[]]$NexusArguments,
        [Parameter(Mandatory = $true)][string]$StepName,
        [int[]]$AllowedExitCodes = @(0)
    )

    $args = @('run', '--project', $cliProjectPath, '--') + $NexusArguments
    Invoke-ExternalCommand -FilePath 'dotnet' -Arguments $args -StepName $StepName -AllowedExitCodes $AllowedExitCodes
}

function Invoke-NexusFromShim {
    param(
        [Parameter(Mandatory = $true)][string[]]$NexusArguments,
        [Parameter(Mandatory = $true)][string]$StepName,
        [int[]]$AllowedExitCodes = @(0)
    )

    if (-not (Test-Path -LiteralPath $cliShimPath)) {
        throw "CLI shim not found: $cliShimPath"
    }

    Invoke-ExternalCommand -FilePath $cliShimPath -Arguments $NexusArguments -StepName $StepName -AllowedExitCodes $AllowedExitCodes
}

function Start-AppViaShim {
    param([Parameter(Mandatory = $true)][string]$Reason)

    Invoke-NexusFromShim -NexusArguments @('winapp', 'start', '--nexus-url', $ServiceUrl) -StepName "Start app ($Reason)"
    Start-Sleep -Milliseconds 1200

    $running = Get-ProcessesUnderPath -PathPrefix $appRoot -ProcessNames @('CopilotNexus.App')
    if ($running.Count -lt 1) {
        throw "App did not remain running after launch for '$Reason'."
    }
}

function Restart-AppViaShim {
    param([Parameter(Mandatory = $true)][string]$Reason)

    Stop-ProcessesByPath -PathPrefix $appRoot -ProcessNames @('CopilotNexus.App', 'CopilotNexus.Updater')
    Start-AppViaShim -Reason $Reason
}

function Restart-ServiceAndApp {
    param([Parameter(Mandatory = $true)][string]$Reason)

    Stop-ProcessesByPath -PathPrefix $appRoot -ProcessNames @('CopilotNexus.App', 'CopilotNexus.Updater')
    Invoke-NexusFromShim -NexusArguments @('restart', '--url', $ServiceUrl) -StepName "Restart service ($Reason)"
    Wait-ForServiceHealth -Url $ServiceUrl -TimeoutSeconds 30
    Start-AppViaShim -Reason $Reason
}

function Assert-NoLifecycleErrors {
    param([Parameter(Mandatory = $true)][string]$Context)

    $matches = New-Object System.Collections.Generic.List[object]

    if (Test-Path -LiteralPath $runLogPath) {
        $runLogMatches = Select-String -Path $runLogPath -Pattern $issuePatterns -AllMatches
        foreach ($match in $runLogMatches) {
            $matches.Add([PSCustomObject]@{
                    File = $runLogPath
                    Line = $match.LineNumber
                    Text = $match.Line.Trim()
                })
        }
    }

    if (Test-Path -LiteralPath $logsPath) {
        $logFiles = Get-ChildItem -Path $logsPath -File -Filter '*.log' -ErrorAction SilentlyContinue
        foreach ($file in $logFiles) {
            $logMatches = Select-String -Path $file.FullName -Pattern $issuePatterns -AllMatches
            foreach ($match in $logMatches) {
                $matches.Add([PSCustomObject]@{
                        File = $file.FullName
                        Line = $match.LineNumber
                        Text = $match.Line.Trim()
                    })
            }
        }
    }

    if ($matches.Count -gt 0) {
        $preview = $matches |
            Select-Object -First 20 |
            ForEach-Object { "{0}:{1} {2}" -f $_.File, $_.Line, $_.Text }
        $message = "Detected lock/load lifecycle issues during ${Context}:`n$($preview -join [Environment]::NewLine)"
        throw $message
    }
}

if (-not (Test-Path -LiteralPath $solutionPath)) {
    throw "Solution file not found: $solutionPath"
}

if (-not (Test-Path -LiteralPath $cliProjectPath)) {
    throw "CLI project file not found: $cliProjectPath"
}

Write-Host 'Copilot Nexus lifecycle loop runner' -ForegroundColor Cyan
Write-Host "Project root: $projectRoot" -ForegroundColor DarkGray
Write-Host "Test root: $TestRoot" -ForegroundColor DarkGray
Write-Host "Service URL: $ServiceUrl" -ForegroundColor DarkGray
Write-Host "Iterations: $Iterations" -ForegroundColor DarkGray

$previousRootOverride = [Environment]::GetEnvironmentVariable($rootOverrideVar, 'Process')

try {
    if (Test-Path -LiteralPath $TestRoot) {
        Stop-ProcessesByPath -PathPrefix $TestRoot
        Remove-Item -LiteralPath $TestRoot -Recurse -Force
    }

    New-Item -Path $TestRoot -ItemType Directory -Force | Out-Null
    New-Item -Path $runLogPath -ItemType File -Force | Out-Null

    [Environment]::SetEnvironmentVariable($rootOverrideVar, $TestRoot, 'Process')

    if (-not $SkipBuild) {
        Write-Section "Build solution ($Configuration)"
        Invoke-ExternalCommand -FilePath 'dotnet' -Arguments @('build', $solutionPath, '-c', $Configuration, '--nologo', '-v', 'q') -StepName 'Build solution'
    }
    else {
        Write-Section 'Build skipped'
    }

    Write-Section 'Install via source CLI'
    Invoke-NexusFromSource -NexusArguments @('install') -StepName 'Install'

    if (-not (Test-Path -LiteralPath $cliShimPath)) {
        throw "Install did not produce CLI shim at $cliShimPath"
    }

    for ($iteration = 1; $iteration -le $Iterations; $iteration++) {
        Write-Section "Iteration $iteration of $Iterations"

        Invoke-NexusFromShim -NexusArguments @('stop') -StepName 'Best-effort stop before iteration' -AllowedExitCodes @(0, 1)
        Stop-ProcessesByPath -PathPrefix $appRoot -ProcessNames @('CopilotNexus.App', 'CopilotNexus.Updater')

        Invoke-NexusFromShim -NexusArguments @('publish', '--component', 'both') -StepName 'Publish #1'
        Invoke-NexusFromShim -NexusArguments @('start', '--url', $ServiceUrl) -StepName 'Start service'
        Wait-ForServiceHealth -Url $ServiceUrl -TimeoutSeconds 30
        Start-AppViaShim -Reason 'after publish #1'

        Invoke-NexusFromShim -NexusArguments @('publish', '--component', 'both') -StepName 'Publish #2'
        Restart-ServiceAndApp -Reason 'after publish #2'

        Invoke-NexusFromShim -NexusArguments @('publish', '--component', 'both') -StepName 'Publish #3'
        Restart-ServiceAndApp -Reason 'after publish #3'

        Assert-NoLifecycleErrors -Context "iteration $iteration"
        Write-Host "Iteration $iteration passed." -ForegroundColor Green
    }

    Write-Section 'Final cleanup'
    Stop-ProcessesByPath -PathPrefix $appRoot -ProcessNames @('CopilotNexus.App', 'CopilotNexus.Updater')
    Invoke-NexusFromShim -NexusArguments @('stop') -StepName 'Stop service' -AllowedExitCodes @(0, 1)

    Write-Host "`nLifecycle loop completed successfully." -ForegroundColor Green
    Write-Host "Run log: $runLogPath" -ForegroundColor DarkGray
    Write-Host "Runtime logs: $logsPath" -ForegroundColor DarkGray
    $completedSuccessfully = $true
}
finally {
    [Environment]::SetEnvironmentVariable($rootOverrideVar, $previousRootOverride, 'Process')

    if (-not $KeepTestRoot -and $completedSuccessfully) {
        if (Test-Path -LiteralPath $TestRoot) {
            Remove-Item -LiteralPath $TestRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}
