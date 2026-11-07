#!/usr/bin/env bash
if [ -z "$BASH_VERSION" ]; then exec bash "$0" "$@"; fi
set -euo pipefail
cd "$(dirname "$0")"
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
  package     Create portable x64 package
  benchmark   Measure startup and memory in isolated data
  smoke       Verify portable package with isolated data
  check-docs  Check collaboration documents
HELP
 ;;
 build) powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/build.ps1 ;;
 test) powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/build.ps1 -Test ;;
 verify) powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/build.ps1 -Test; bash check-docs.sh ;;
 dev) powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/run.ps1 -Development ;;
 run) powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/run.ps1 ;;
 package) powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/package.ps1 ;;
 benchmark) powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/benchmark.ps1 ;;
 smoke) powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/smoke-test.ps1 ;;
 check-docs) bash check-docs.sh ;;
 *) echo "Unknown command: $command" >&2; exit 2 ;;
esac
