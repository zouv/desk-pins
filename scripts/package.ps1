param(
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $projectRoot 'DeskPins.sln'
$appProject = Join-Path $projectRoot 'src/DeskPins.App/DeskPins.App.csproj'
$appOutput = Join-Path $projectRoot 'src/DeskPins.App/bin'
$distDir = Join-Path $projectRoot 'dist'
$stagingRoot = Join-Path $projectRoot '.runtime/staging'
. (Join-Path $PSScriptRoot 'PackageTransaction.ps1')

$version = (& dotnet msbuild $appProject -getProperty:Version -nologo 2>$null | Select-Object -Last 1)
if ($null -ne $version) { $version = $version.Trim() }
if ([string]::IsNullOrEmpty($version) -or $version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Cannot read <Version> from Directory.Build.props via $appProject."
}

$stagingDir = Assert-PackagePath (Join-Path $stagingRoot ('package-' + [Guid]::NewGuid().ToString('N'))) $projectRoot
$finalDevDir = Join-Path $distDir 'dev'
$finalReleaseDir = Join-Path $distDir 'release'
$stageDevDir = Join-Path $stagingDir 'dev'
$stageReleaseDir = Join-Path $stagingDir 'release'
$devFolder = "DeskPins_${version}_x64-unpack-debug"
$devSource = Join-Path $stagingDir "source/$devFolder"
$devLogs = Join-Path $devSource 'logs'
$releaseSource = Join-Path $stageReleaseDir 'unpack'
$devZip = Join-Path $stageDevDir "$devFolder.zip"
$releaseZip = Join-Path $stageReleaseDir "DeskPins_${version}_x64-unpack.zip"

New-Item -ItemType Directory -Force -Path $devLogs, $releaseSource, $stageDevDir | Out-Null

function Get-Sha256([string]$Path) {
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Invoke-Build([string]$Configuration, [string]$Log) {
    if ($SkipBuild) { return }
    & dotnet build $solution -c $Configuration --nologo 2>&1 | Out-File -LiteralPath $Log -Encoding utf8
    if ($LASTEXITCODE -ne 0) {
        Get-Content -LiteralPath $Log -Tail 40
        throw "$Configuration build failed. Log: $Log"
    }
}

function Copy-AppFiles([string]$Configuration, [string]$Destination, [bool]$IncludeSymbols) {
    $source = Join-Path $appOutput "$Configuration/net48"
    $names = @('DeskPins.exe', 'DeskPins.exe.config', 'DeskPins.Core.dll')
    if ($IncludeSymbols) { $names += @('DeskPins.pdb', 'DeskPins.Core.pdb') }
    foreach ($name in $names) {
        $file = Join-Path $source $name
        if (-not (Test-Path -LiteralPath $file)) { throw "Missing build output: $file" }
        if ($name -eq 'DeskPins.exe' -or $name -eq 'DeskPins.Core.dll') {
            $builtVersion = [Reflection.AssemblyName]::GetAssemblyName($file).Version.ToString(3)
            if ($builtVersion -ne $version) { throw "Stale build version: $file ($builtVersion != $version)" }
        }
        Copy-Item -LiteralPath $file -Destination (Join-Path $Destination $name) -Force
    }
}

function Copy-ProjectContent([string]$Destination) {
    Copy-Item -LiteralPath (Join-Path $projectRoot 'assets') -Destination (Join-Path $Destination 'assets') -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $projectRoot 'docs') -Destination (Join-Path $Destination 'docs') -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination (Join-Path $Destination 'README.md') -Force
    foreach ($document in @('AGENTS.md', 'ARCHITECTURE.md', 'ROADMAP.md', 'CHANGELOG.md', 'HANDOFF.md')) {
        Copy-Item -LiteralPath (Join-Path $projectRoot $document) -Destination (Join-Path $Destination $document) -Force
    }
}

function Write-BuildInfo([string]$Path, [string]$Mode) {
    $git = 'unknown'
    try {
        $git = (& git -C $projectRoot rev-parse --short HEAD 2>$null | Select-Object -First 1)
        if ([string]::IsNullOrWhiteSpace($git)) { $git = 'unknown' }
    }
    catch { }
    $sdk = (& dotnet --version 2>$null | Select-Object -First 1)
    @(
        "DeskPins $version ($Mode, x64)"
        "Built: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
        "Git: $git"
        "Dotnet: $sdk"
        "OS: $([Environment]::OSVersion.VersionString)"
    ) | Set-Content -LiteralPath $Path -Encoding UTF8
}

function Write-ReadmeText([string]$Path, [string]$Mode) {
    $lines = if ($Mode -eq 'Debug') {
        @(
            "DeskPins $version (Debug, x64)"
            "================================"
            ""
            "Debug build for development and troubleshooting."
            "Requires Windows 10 / 11 x64 and .NET Framework 4.8."
            ""
            "Includes PDBs, build logs and build-info.txt."
            "User data: %LOCALAPPDATA%\DeskPins by default; use --data-dir <path> for an isolated folder."
            "See README.md and the docs folder for usage."
        )
    }
    else {
        @(
            "DeskPins $version (x64)"
            "======================="
            ""
            "Portable release: double-click DeskPins.exe. No installer or admin rights needed."
            "Requires Windows 10 / 11 x64 and .NET Framework 4.8 (included with Windows 10/11)."
            ""
            "Move or copy the WHOLE folder; do not move the exe by itself."
            "User data: %LOCALAPPDATA%\DeskPins by default; use --data-dir <path> for a portable data folder."
            "See README.md and the docs folder for usage."
        )
    }
    $lines | Set-Content -LiteralPath $Path -Encoding UTF8
}

function New-Zip([string]$Source, [string]$ZipPath, [string]$Prefix) {
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    if (Test-Path -LiteralPath $ZipPath) { Remove-Item -LiteralPath $ZipPath -Force }
    $archive = [IO.Compression.ZipFile]::Open($ZipPath, [IO.Compression.ZipArchiveMode]::Create)
    try {
        $base = [IO.Path]::GetFullPath($Source).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
        foreach ($file in Get-ChildItem -LiteralPath $Source -Recurse -File) {
            $relative = $file.FullName.Substring($base.Length).Replace('\', '/')
            $entry = if ([string]::IsNullOrEmpty($Prefix)) { $relative } else { "$Prefix/$relative" }
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $entry) | Out-Null
        }
    }
    finally { $archive.Dispose() }
}

function Write-Manifest([string]$Directory, [string]$ManifestPath) {
    $base = [IO.Path]::GetFullPath($Directory).TrimEnd('\') + '\'
    $manifestFull = [IO.Path]::GetFullPath($ManifestPath)
    $files = @(Get-ChildItem -LiteralPath $Directory -Recurse -File |
        Where-Object { -not [string]::Equals($_.FullName, $manifestFull, [StringComparison]::OrdinalIgnoreCase) } |
        Sort-Object FullName)
    $entries = @(foreach ($file in $files) {
        [ordered]@{
            path = $file.FullName.Substring($base.Length).Replace('\', '/')
            size = $file.Length
            sha256 = Get-Sha256 $file.FullName
        }
    })
    $manifest = [ordered]@{
        format = 1
        product = 'DeskPins'
        version = $version
        mode = 'Release'
        platform = 'win32-x64'
        createdAt = (Get-Date).ToUniversalTime().ToString('o')
        files = $entries
    }
    $json = $manifest | ConvertTo-Json -Depth 5
    [IO.File]::WriteAllText($ManifestPath, $json + [Environment]::NewLine, (New-Object Text.UTF8Encoding($false)))
}

$packageLock = $null
try {
    New-Item -ItemType Directory -Force -Path $stagingRoot | Out-Null
    $packageLock = [IO.File]::Open((Join-Path $stagingRoot 'package.lock'), 'OpenOrCreate', 'ReadWrite', 'None')

    Write-Host "Package | Version = $version"
    Write-Host "  dev     : $(Join-Path $finalDevDir (Split-Path -Leaf $devZip))"
    Write-Host "  unpack  : $(Join-Path $finalReleaseDir 'unpack')"
    Write-Host "  release : $(Join-Path $finalReleaseDir (Split-Path -Leaf $releaseZip))"

    Write-Host '[1/6] Debug build...'
    Invoke-Build 'Debug' (Join-Path $devLogs 'debug-build.log')
    Copy-AppFiles 'Debug' $devSource $true
    Copy-ProjectContent $devSource
    Write-BuildInfo (Join-Path $devSource 'build-info.txt') 'Debug'
    Write-ReadmeText (Join-Path $devSource 'README.txt') 'Debug'

    Write-Host '[2/6] Release build...'
    Invoke-Build 'Release' (Join-Path $devLogs 'release-build.log')
    Copy-AppFiles 'Release' $releaseSource $false
    Copy-ProjectContent $releaseSource
    Write-ReadmeText (Join-Path $releaseSource 'README.txt') 'Release'

    Write-Host '[3/6] Tests...'
    if (-not $SkipBuild) {
        $tests = Join-Path $projectRoot 'tests/DeskPins.Tests/bin/Release/net48/DeskPins.Tests.exe'
        if (-not (Test-Path -LiteralPath $tests)) { throw "Missing test executable: $tests" }
        & $tests --ui 2>&1 | Tee-Object -FilePath (Join-Path $devLogs 'tests.log')
        if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
    }

    Write-Host '[4/6] Manifest...'
    Write-Manifest $releaseSource (Join-Path $releaseSource 'unpack-manifest.json')

    Write-Host '[5/6] Zip...'
    New-Zip $devSource $devZip $devFolder
    New-Zip $releaseSource $releaseZip ''
    Test-PackageZip $devSource $devZip $devFolder
    Test-PackageZip $releaseSource $releaseZip ''

    Write-Host '[6/6] Publish...'
    New-Item -ItemType Directory -Force -Path $distDir | Out-Null
    $releaseHash = Get-Sha256 $releaseZip
    $devHash = Get-Sha256 $devZip
    @(
        "$releaseHash *$(Split-Path -Leaf $releaseZip)"
        "$devHash *$(Split-Path -Leaf $devZip)"
    ) | Set-Content -LiteralPath (Join-Path $stageReleaseDir 'SHA256SUMS.txt') -Encoding ASCII

    Publish-PackageSet $projectRoot $stagingDir $distDir
    Remove-Item -LiteralPath $stagingDir -Recurse -Force

    Write-Host ''
    Write-Host "[SUCCESS] Package complete (version $version)."
    Write-Host "  dev     : $(Join-Path $finalDevDir (Split-Path -Leaf $devZip))"
    Write-Host "  unpack  : $(Join-Path $finalReleaseDir 'unpack')"
    Write-Host "  release : $(Join-Path $finalReleaseDir (Split-Path -Leaf $releaseZip))"
    Write-Host "  sha256  : $(Join-Path $finalReleaseDir 'SHA256SUMS.txt')"
}
catch {
    Write-Host "Package staging retained for inspection: $stagingDir" -ForegroundColor Yellow
    throw
}
finally {
    if ($packageLock) { $packageLock.Dispose() }
}
