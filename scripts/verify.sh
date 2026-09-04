#!/usr/bin/env bash
# Full verification. This is what "done" means for a task.
# Every step prints PASS or FAIL. Exit code is non-zero if any step fails.
set -uo pipefail
cd "$(dirname "$0")/.."

FAILED=0
step() {
  printf '\n=== %s ===\n' "$1"
}
result() {
  if [ "$1" -eq 0 ]; then echo "PASS: $2"; else echo "FAIL: $2"; FAILED=1; fi
}

if [ ! -f HardwarePos.sln ]; then
  echo "No solution yet. Run: bash scripts/bootstrap-solution.sh  (task P0-T01)"
  exit 1
fi

step "Restore"
dotnet restore >/dev/null 2>&1
result $? "package restore"

step "Build (warnings are errors)"
dotnet build --no-restore -c Debug
result $? "build"

step "Format check"
dotnet format --verify-no-changes --no-restore 2>/dev/null
result $? "code formatting"

step "Tests"
dotnet test --no-build -c Debug --logger "console;verbosity=normal"
result $? "test suite"

step "Architecture tests"
dotnet test --no-build --filter "FullyQualifiedName~ArchitectureTests" 2>/dev/null
result $? "project boundaries and no-float rules"

step "Trigger survival"
if [ -f scripts/check-triggers.sh ]; then
  bash scripts/check-triggers.sh
  result $? "append-only triggers survive the migration chain"
else
  echo "SKIP: scripts/check-triggers.sh not present yet (added in P0-T04)"
fi

step "Working tree"
git status --short

echo
if [ "$FAILED" -eq 0 ]; then
  echo "ALL CHECKS PASSED"
else
  echo "VERIFICATION FAILED - see FAIL lines above"
fi
exit "$FAILED"
