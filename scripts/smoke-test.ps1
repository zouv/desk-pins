$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
[xml]$props=Get-Content -LiteralPath (Join-Path $projectRoot 'Directory.Build.props') -Raw
$name='DeskPins_'+$props.Project.PropertyGroup.Version+'_x64-unpack'
$zip=Join-Path $projectRoot ('dist/release/'+$name+'.zip')
if(-not(Test-Path -LiteralPath $zip)){throw 'Run package first.'}
$testRoot=Join-Path $projectRoot ('.runtime/package-smoke/'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::ExtractToDirectory($zip,$testRoot)
$exe=Join-Path $testRoot 'DeskPins.exe'
$data=Join-Path $testRoot 'data'
$response=Join-Path $testRoot 'response.txt'
$process=Start-Process -FilePath $exe -ArgumentList ('--data-dir "'+$data+'"') -WindowStyle Hidden -PassThru
function Invoke-TestApi([string]$command) {
    $client=Start-Process -FilePath $exe -ArgumentList ('--data-dir "'+$data+'" --api "'+$command+'" --output "'+$response+'"') -WindowStyle Hidden -PassThru
    if(-not $client.WaitForExit(15000) -or $client.ExitCode -ne 0){throw ('API failed: '+$command)}
    return [IO.File]::ReadAllText($response)
}
try {
    Start-Sleep -Seconds 2
    if((Invoke-TestApi 'do ping') -notlike '000*'){throw 'Packaged app did not start.'}
    $file=Join-Path $testRoot 'Imported note.txt'
    [IO.File]::WriteAllText($file,'Portable package import',[Text.UTF8Encoding]::new($false))
    $second=Start-Process -FilePath $exe -ArgumentList ('--data-dir "'+$data+'" "'+$file+'"') -WindowStyle Hidden -PassThru
    if(-not $second.WaitForExit(10000) -or $second.ExitCode -ne 0){throw 'Second instance did not hand off import.'}
    $notes=Invoke-TestApi 'get notes' | ConvertFrom-Json
    if(-not($notes | Where-Object Preview -eq 'Portable package import')){throw 'Import did not reach the existing instance.'}
    $note=$notes | Where-Object Preview -eq 'Portable package import' | Select-Object -First 1
    if((Invoke-TestApi ('get desktop '+$note.Id+' text')) -notlike '*Portable package import*'){throw 'Imported content mismatch.'}
    $process.Refresh(); $initialCpu=$process.TotalProcessorTime.TotalMilliseconds
    Start-Sleep -Seconds 5
    $process.Refresh()
    $result=[PSCustomObject]@{Started=$true;SingleInstanceImport=$true;PrivateMiB=[Math]::Round($process.PrivateMemorySize64/1MB,2);WorkingSetMiB=[Math]::Round($process.WorkingSet64/1MB,2);IdleCpuMsOver5Seconds=[Math]::Round($process.TotalProcessorTime.TotalMilliseconds-$initialCpu,2)}
    $result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $testRoot 'result.json') -Encoding UTF8
    $result | Format-List
} finally {
    if(-not $process.HasExited){try {Invoke-TestApi 'quit' | Out-Null; if(-not $process.WaitForExit(10000)){throw 'Test instance did not exit.'}} catch { if($result){throw} else {Write-Warning 'Cleanup failed; inspect the isolated test instance.'} }}
}
Write-Host 'Portable process smoke test passed. Visual appearance is not part of this check.'
