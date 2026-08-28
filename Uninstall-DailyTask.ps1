[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$executable = Join-Path $PSScriptRoot 'SystemCare.exe'
if (Test-Path -LiteralPath $executable) {
    & $executable --uninstall-task
    exit $LASTEXITCODE
}
& schtasks.exe /Delete /TN 'GamingSystemCare Daily' /F
exit $LASTEXITCODE
