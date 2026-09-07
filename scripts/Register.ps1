#requires -RunAsAdministrator
param([switch]$Unregister)
$ErrorActionPreference = 'Stop'
$server = Join-Path $PSScriptRoot '..\src\GeminiFlatPanel.Server\bin\Release\net48\ASCOM.CCDASTRO.GeminiFlatPanel.exe'
if (!(Test-Path -LiteralPath $server)) { throw 'Build the solution in Release configuration first.' }
if (Get-Process -Name 'ASCOM.CCDASTRO.GeminiFlatPanel' -ErrorAction SilentlyContinue) { throw 'Close all Gemini clients and setup windows first.' }
$argument = if ($Unregister) { '/unregister' } else { '/register' }
$result = Start-Process -FilePath $server -ArgumentList $argument -Wait -PassThru -WindowStyle Hidden
if ($result.ExitCode -ne 0) { throw "Registration command failed: $($result.ExitCode)" }
Write-Host 'Registration command completed. Verify the Gemini entries in the ASCOM chooser.'
