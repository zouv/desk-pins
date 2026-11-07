param([switch]$Test)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
Push-Location $projectRoot
try {
    & dotnet build DeskPins.sln -c Release --nologo
    if($LASTEXITCODE -ne 0){throw 'Build failed.'}
    if($Test){& 'tests/DeskPins.Tests/bin/Release/net48/DeskPins.Tests.exe' --ui;if($LASTEXITCODE -ne 0){throw 'Tests failed.'}}
    $destination=Join-Path $projectRoot '.runtime/build/dev'
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    foreach($name in @('DeskPins.exe','DeskPins.exe.config','DeskPins.Core.dll')){Copy-Item -LiteralPath (Join-Path $projectRoot ('src/DeskPins.App/bin/Release/net48/'+$name)) -Destination $destination -Force}
    Write-Host ('Ready: '+(Join-Path $destination 'DeskPins.exe'))
} finally {Pop-Location}
