[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$ProcessName = 'DeskPins',
    [switch]$Force
)
$ErrorActionPreference = 'Stop'

function Get-Targets {
    @(Get-Process -Name $ProcessName -ErrorAction SilentlyContinue)
}

$targets = Get-Targets
if ($targets.Count -eq 0) {
    Write-Host "[info] $ProcessName is not running."
    exit 0
}

Write-Host "[info] $ProcessName is running:"
foreach ($target in $targets) {
    $path = ''
    try { $path = $target.Path } catch { }
    if ($path) { Write-Host "  PID $($target.Id) - $path" }
    else { Write-Host "  PID $($target.Id)" }
}

if (-not $Force) {
    $answer = Read-Host "Close $ProcessName and continue? Unsaved edits may be lost. [y/N]"
    if ($answer -notmatch '^(?i:y|yes)$') {
        Write-Host '[cancelled] Left running.'
        exit 1
    }
}
else {
    Write-Host '[warn] Force-closing; edits from the last save interval may be lost.'
}

$stopped = $false
foreach ($target in $targets) {
    if ($PSCmdlet.ShouldProcess("$ProcessName PID $($target.Id)", 'Stop-Process -Force')) {
        try {
            Stop-Process -Id $target.Id -Force -ErrorAction Stop
            $stopped = $true
        }
        catch {
            if (Get-Process -Id $target.Id -ErrorAction SilentlyContinue) {
                throw "Cannot close $ProcessName PID $($target.Id): $($_.Exception.Message)"
            }
            $stopped = $true
        }
    }
}
if (-not $stopped) { exit 0 }

$deadline = (Get-Date).AddSeconds(10)
while ((Get-Targets).Count -gt 0 -and (Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 200
}
if ((Get-Targets).Count -gt 0) {
    throw "$ProcessName is still running after 10 seconds."
}

Write-Host "[ ok ] $ProcessName closed."
exit 0
