<#
.SYNOPSIS
    Runs the Antigravity Comprehensive Automated Test Suite for Photo Aevum Studio.
.DESCRIPTION
    Executes automated checks across:
    - Deployment Synchronization (MonsterASP live responsiveness & GitHub commit hash matching)
    - Database Validation (Live SQL Server read/write & 33 core tables schema verification)
    - Secret & Storage Integration (Infisical configuration pull & Cloudflare R2 mock image roundtrip)
    - Strict Teardown (Automated rollback of mock database rows & permanent R2 artifact obliteration)
#>

[CmdletBinding()]
param(
    [switch]$VerboseOutput
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$WorkspaceRoot = Split-Path -Parent $ScriptDir

Write-Host "`n=================================================================" -ForegroundColor Cyan
Write-Host "  Launching Antigravity Comprehensive Automated Test Suite" -ForegroundColor Cyan
Write-Host "  Workspace: $WorkspaceRoot" -ForegroundColor DarkGray
Write-Host "=================================================================`n" -ForegroundColor Cyan

$PythonExe = (Get-Command python -ErrorAction SilentlyContinue).Source
if (-not $PythonExe) {
    Write-Error "Python executable not found in PATH."
    exit 1
}

$TestRunnerScript = Join-Path $ScriptDir "run_antigravity_tests.py"

& $PythonExe $TestRunnerScript
$ExitCode = $LASTEXITCODE

if ($ExitCode -eq 0) {
    Write-Host "`nAll Antigravity automated checks passed successfully with 100% data hygiene.`n" -ForegroundColor Green
} else {
    Write-Host "`nAntigravity test suite encountered failures (Exit Code: $ExitCode).`n" -ForegroundColor Red
}

exit $ExitCode
