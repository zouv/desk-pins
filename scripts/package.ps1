$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'build.ps1') -Test
[xml]$props=Get-Content -LiteralPath (Join-Path $projectRoot 'Directory.Build.props') -Raw
$version=$props.Project.PropertyGroup.Version
$package=Join-Path $projectRoot ('dist/DeskPins-'+$version+'-x64')
New-Item -ItemType Directory -Path $package -Force | Out-Null
foreach($name in @('DeskPins.exe','DeskPins.exe.config','DeskPins.Core.dll')){Copy-Item -LiteralPath (Join-Path $projectRoot ('.runtime/build/dev/'+$name)) -Destination $package -Force}
$assetTarget=Join-Path $package 'assets'
New-Item -ItemType Directory -Path $assetTarget -Force | Out-Null
Get-ChildItem -LiteralPath (Join-Path $projectRoot 'assets') | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $assetTarget -Recurse -Force }
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $package -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs') -Destination $package -Recurse -Force
foreach($document in @('AGENTS.md','ARCHITECTURE.md','ROADMAP.md','CHANGELOG.md','HANDOFF.md')){Copy-Item -LiteralPath (Join-Path $projectRoot $document) -Destination $package -Force}
$zip=Join-Path $projectRoot ('dist/DeskPins-'+$version+'-x64.zip')
Compress-Archive -LiteralPath $package -DestinationPath $zip -Force
$hashStream=[IO.File]::OpenRead($zip)
$hasher=[Security.Cryptography.SHA256]::Create()
try { $digest=[BitConverter]::ToString($hasher.ComputeHash($hashStream)).Replace('-','') } finally { $hashStream.Dispose(); $hasher.Dispose() }
[IO.File]::WriteAllText((Join-Path $projectRoot 'dist/SHA256SUMS.txt'),$digest+'  '+[IO.Path]::GetFileName($zip)+[Environment]::NewLine,[Text.Encoding]::ASCII)
Write-Host ('Package: '+$zip)
