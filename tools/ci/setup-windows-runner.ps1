#Requires -Version 7.0
<#
.SYNOPSIS
  Installs a self-hosted GitHub Actions runner (Windows x64) for the horyzont repo.

.DESCRIPTION
  Run by the repo owner on the Windows machine. Needs the GitHub CLI (`gh`) logged in with
  admin rights on the repo. The registration token is never printed.
  No Windows service is installed (it would run as NETWORK SERVICE without the user's toolchain);
  use -StartAtLogon for a per-user Scheduled Task, or start run.cmd manually.

.PARAMETER Sha256
  Expected SHA-256 of actions-runner-win-x64-<Version>.zip, taken from the release notes page
  of actions/runner for that version. Mandatory unless -SkipHashCheck.
#>
[CmdletBinding()]
param(
    [string]$RunnerDir = 'C:\actions-runner\horyzont',
    [string]$RunnerName = "$env:COMPUTERNAME-horyzont",
    [string]$Repo = 'Dismonder/horyzont',
    [string]$Version = '2.338.0',
    # SHA-256 of actions-runner-win-x64-2.338.0.zip (GitHub release asset digest); pass a new one with -Version.
    [string]$Sha256 = 'f48e0750a21812bca5f82de5f7f5aeae71abee647fab5a582f1742d07eba455f',
    [switch]$SkipHashCheck,
    [switch]$StartAtLogon
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-PathFile {
    param([string]$Dir)
    $gitDirs = @('C:\Program Files\Git\bin', 'C:\Program Files\Git\usr\bin')
    foreach ($d in $gitDirs) {
        if (-not (Test-Path -LiteralPath $d)) { throw "Git for Windows directory not found: $d" }
    }
    $machine = [Environment]::GetEnvironmentVariable('Path', 'Machine')
    $user = [Environment]::GetEnvironmentVariable('Path', 'User')
    $rest = ("$machine;$user" -split ';') | Where-Object { $_ -and ($gitDirs -notcontains $_.TrimEnd('\')) }
    $content = (($gitDirs + $rest) -join ';')
    # The runner reads .path as a plain single-line UTF-8 file (no BOM).
    [System.IO.File]::WriteAllText((Join-Path $Dir '.path'), $content, [System.Text.UTF8Encoding]::new($false))
    Write-Host "Refreshed $(Join-Path $Dir '.path') (Git Bash first, so 'shell: bash' is not WSL)."
}

if (-not $SkipHashCheck -and [string]::IsNullOrWhiteSpace($Sha256)) {
    throw "Provide -Sha256 (SHA-256 of actions-runner-win-x64-$Version.zip from the actions/runner v$Version release notes) or pass -SkipHashCheck."
}
if ($SkipHashCheck) {
    Write-Warning 'Skipping SHA-256 verification of the runner archive. Not recommended.'
}

New-Item -ItemType Directory -Force -Path $RunnerDir | Out-Null

if (Test-Path -LiteralPath (Join-Path $RunnerDir '.runner')) {
    Write-Host "Runner already configured in $RunnerDir; skipping download and registration."
}
else {
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw 'GitHub CLI (gh) not found in PATH.' }

    $zipName = "actions-runner-win-x64-$Version.zip"
    $zipPath = Join-Path $RunnerDir $zipName
    $url = "https://github.com/actions/runner/releases/download/v$Version/$zipName"

    Write-Host "Downloading $url"
    Invoke-WebRequest -Uri $url -OutFile $zipPath -UseBasicParsing

    if (-not $SkipHashCheck) {
        $actual = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
        if ($actual -ne $Sha256.Trim().ToUpperInvariant() -and $actual -ne $Sha256.Trim()) {
            Remove-Item -LiteralPath $zipPath -Force
            throw "SHA-256 mismatch for $zipName. Expected $Sha256, got $actual. Archive deleted."
        }
        Write-Host 'SHA-256 OK.'
    }

    Write-Host "Extracting to $RunnerDir"
    Expand-Archive -LiteralPath $zipPath -DestinationPath $RunnerDir -Force
    Remove-Item -LiteralPath $zipPath -Force

    # .path must exist before config so the runner picks it up from the start.
    Write-PathFile -Dir $RunnerDir

    $token = (& gh api -X POST "repos/$Repo/actions/runners/registration-token" --jq .token)
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($token)) {
        $token = $null
        throw "Could not get a registration token for $Repo (is 'gh' logged in with admin rights?)."
    }
    try {
        Push-Location $RunnerDir
        & .\config.cmd --unattended --url "https://github.com/$Repo" --token $token --name $RunnerName --labels horyzont --work _work --replace
        if ($LASTEXITCODE -ne 0) { throw "config.cmd failed with exit code $LASTEXITCODE." }
    }
    finally {
        Pop-Location
        $token = $null
        Remove-Variable -Name token -ErrorAction SilentlyContinue
    }
    Write-Host "Runner '$RunnerName' registered with label 'horyzont'."
}

Write-PathFile -Dir $RunnerDir

# The runner resolves 'shell: bash' against its own process PATH, not .path, and the user PATH
# lists the WSL alias (WindowsApps\bash.exe) first. This launcher puts Git Bash first for the runner.
$runCmd = Join-Path $RunnerDir 'run-horyzont.cmd'
$launcher = "@echo off`r`nset `"PATH=C:\Program Files\Git\bin;%PATH%`"`r`ncall `"%~dp0run.cmd`" %*`r`n"
[System.IO.File]::WriteAllText($runCmd, $launcher, [System.Text.Encoding]::ASCII)
if ($StartAtLogon) {
    $taskName = 'horyzont-actions-runner'
    $action = New-ScheduledTaskAction -Execute $runCmd -WorkingDirectory $RunnerDir
    $trigger = New-ScheduledTaskTrigger -AtLogOn -User "$env:USERDOMAIN\$env:USERNAME"
    $principal = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" -LogonType Interactive -RunLevel Limited
    $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero) -StartWhenAvailable
    Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force | Out-Null
    Write-Host "Scheduled task '$taskName' registered (runs at your logon). Start now: Start-ScheduledTask -TaskName $taskName"
}
else {
    Write-Host "Start the runner manually: & '$runCmd'   (or re-run with -StartAtLogon)"
}
