#!/usr/bin/env bash
if [ -z "$BASH_VERSION" ]; then exec bash "$0" "$@"; fi
set -euo pipefail
cd "$(dirname "$0")"
if command -v pwsh >/dev/null 2>&1; then PS=pwsh
elif command -v powershell.exe >/dev/null 2>&1; then PS=powershell.exe
else echo "PowerShell not found; install PowerShell 7 or use Windows PowerShell 5.1." >&2; exit 1; fi
command=help
if [ $# -gt 0 ]; then command="$1"; fi
case "$command" in
 help) cat <<'HELP'
DeskPins commands:
  build       Build the WPF application
  test        Run core and WPF regression checks
  verify      Build, test and check documentation
  dev         Build and start development app
  run         Start the built application
  package     Create dev/release packages under dist/
  benchmark   Measure startup and memory in isolated data
  smoke       Verify portable package with isolated data
  check-docs  Check collaboration documents (--strict supported)
HELP
 ;;
 build) "$PS" -NoProfile -ExecutionPolicy Bypass -File scripts/build.ps1 ;;
 test) "$PS" -NoProfile -ExecutionPolicy Bypass -File scripts/build.ps1 -Test ;;
 verify) "$PS" -NoProfile -ExecutionPolicy Bypass -File scripts/build.ps1 -Test; bash check-docs.sh ;;
 dev) "$PS" -NoProfile -ExecutionPolicy Bypass -File scripts/run.ps1 -Development ;;
 run) "$PS" -NoProfile -ExecutionPolicy Bypass -File scripts/run.ps1 ;;
 package) "$PS" -NoProfile -ExecutionPolicy Bypass -File scripts/ensure-closed.ps1; "$PS" -NoProfile -ExecutionPolicy Bypass -File scripts/package.ps1 ;;
 benchmark) "$PS" -NoProfile -ExecutionPolicy Bypass -File scripts/benchmark.ps1 ;;
 smoke) "$PS" -NoProfile -ExecutionPolicy Bypass -File scripts/smoke-test.ps1 ;;
 check-docs) bash check-docs.sh "$@" ;;
 *) echo "Unknown command: $command" >&2; exit 2 ;;
esac
