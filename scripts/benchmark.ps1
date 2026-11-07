param([ValidateRange(1,10)][int]$Runs=3)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
$exe=Join-Path $projectRoot '.runtime/build/dev/DeskPins.exe'
if(-not(Test-Path -LiteralPath $exe)){throw 'Run build first.'}
$benchmarkRoot=Join-Path $projectRoot ('.runtime/benchmark/'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $benchmarkRoot -Force | Out-Null
$results=@()
foreach($noteCount in @(1,10)) {
    for($i=0;$i -lt $Runs;$i++) {
        $data=Join-Path $benchmarkRoot ('notes-'+$noteCount)
        $output=Join-Path $benchmarkRoot ('result-'+$noteCount+'-'+$i+'.json')
        $arguments='--benchmark --notes '+$noteCount+' --data-dir "'+$data+'" --output "'+$output+'"'
        $process=Start-Process -FilePath $exe -ArgumentList $arguments -WindowStyle Hidden -PassThru
        if(-not $process.WaitForExit(30000)){throw 'Benchmark exceeded 30 seconds; inspect its test instance.'}
        if($process.ExitCode -ne 0 -or -not(Test-Path -LiteralPath $output)){throw 'Benchmark failed.'}
        $result=Get-Content -LiteralPath $output -Raw | ConvertFrom-Json
        $results += [PSCustomObject]@{ Notes=$result.VisibleNotes; Run=$i+1; ReadyMs=$result.ReadyMilliseconds; PrivateMiB=[Math]::Round($result.PrivateBytes/1MB,2); WorkingSetMiB=[Math]::Round($result.WorkingSet/1MB,2) }
    }
}
$results | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $benchmarkRoot 'summary.json') -Encoding UTF8
$results | Format-Table
Write-Host 'Measures entry to dispatcher idle; file/CLR caches are warm. It does not measure a confirmed first paint.'
