<#
Enables or removes Windows Error Reporting LocalDumps for the Godot executables, under HKCU only (no admin rights needed).
A crash of godot*.exe (the access violation at server exit, K4-19) then leaves a full dump in -Dir for the CI artifact.
Usage: pwsh tools/ci/wer_localdumps.ps1 -Action enable -Dir wer-dumps   /   -Action disable
#>
param(
    [Parameter(Mandatory)][ValidateSet('enable', 'disable')][string]$Action,
    [string]$Dir = 'wer-dumps'
)

$ErrorActionPreference = 'Stop'
$root = 'HKCU:\Software\Microsoft\Windows\Windows Error Reporting\LocalDumps'
$names = @('godot.windows.editor.double.x86_64.mono.exe', 'godot.windows.editor.double.x86_64.mono.console.exe', 'Horyzont.exe', 'HoryzontServer.exe')

foreach ($name in $names) {
    $key = Join-Path $root $name
    if ($Action -eq 'enable') {
        $full = (New-Item -ItemType Directory -Force -Path $Dir).FullName
        New-Item -Path $key -Force | Out-Null
        New-ItemProperty -Path $key -Name DumpFolder -Value $full -PropertyType ExpandString -Force | Out-Null
        New-ItemProperty -Path $key -Name DumpType -Value 2 -PropertyType DWord -Force | Out-Null
        New-ItemProperty -Path $key -Name DumpCount -Value 5 -PropertyType DWord -Force | Out-Null
    }
    elseif (Test-Path $key) {
        # The key has values and no subkeys.
        Remove-Item -Path $key -Force
    }
}

Write-Output "WER LocalDumps $Action for $($names.Count) executables"
