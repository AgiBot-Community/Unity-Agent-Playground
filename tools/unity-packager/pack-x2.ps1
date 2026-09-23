param(
    [Parameter(Mandatory = $true)]
    [string]$Source,
    [Parameter(Mandatory = $true)]
    [string]$Output
)

$ErrorActionPreference = 'Stop'
$icon = Join-Path $PSScriptRoot 'assets\agibot-x2.ico'
& python (Join-Path $PSScriptRoot 'pack.py') --source $Source --player 'UnityEnvironment.exe' --output $Output --icon $icon
if ($LASTEXITCODE -ne 0) { throw 'X2 portable packaging failed.' }
