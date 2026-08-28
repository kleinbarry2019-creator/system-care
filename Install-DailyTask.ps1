[CmdletBinding()]
param(
    [ValidatePattern('^([01]\d|2[0-3]):[0-5]\d$')]
    [string]$Time = '03:15'
)

$ErrorActionPreference = 'Stop'
$executable = Join-Path $PSScriptRoot 'SystemCare.exe'
if (-not (Test-Path -LiteralPath $executable)) {
    throw "SystemCare.exe not found at $executable. Publish/build the project first."
}
& $executable --install-task --time $Time
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Output "Daily task installed. Configure DailyTime in %LOCALAPPDATA%\GamingSystemCare\config.json if needed."
