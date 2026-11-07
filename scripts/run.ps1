param([switch]$Development)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
if($Development){& (Join-Path $PSScriptRoot 'build.ps1')}
$exe=Join-Path $projectRoot '.runtime/build/dev/DeskPins.exe'
if(-not(Test-Path -LiteralPath $exe)){throw 'Run build first.'}
Start-Process -FilePath $exe -WindowStyle Hidden
