#!/usr/bin/env bash
# Task P0-T01: create the solution and project skeleton.
#
# Run once. Idempotent - it skips anything that already exists.
# Package versions are pinned centrally in Directory.Packages.props; if restore
# fails, adjust the version there rather than in an individual project.
set -euo pipefail
cd "$(dirname "$0")/.."

if [ -f HardwarePos.sln ]; then
  echo "HardwarePos.sln already exists. Nothing to do."
  exit 0
fi

echo "==> Creating solution"
dotnet new sln -n HardwarePos

new_lib() {
  local path="$1" name="$2"
  if [ ! -d "$path" ]; then
    dotnet new classlib -o "$path" -n "$name" --framework net10.0
    rm -f "$path/Class1.cs"
  fi
  dotnet sln add "$path/$name.csproj"
}

new_test() {
  local path="$1" name="$2"
  if [ ! -d "$path" ]; then
    dotnet new xunit -o "$path" -n "$name" --framework net10.0
    rm -f "$path/UnitTest1.cs"
  fi
  dotnet sln add "$path/$name.csproj"
}

echo "==> Creating source projects"
new_lib src/HardwarePos.Domain          HardwarePos.Domain
new_lib src/HardwarePos.Application     HardwarePos.Application
new_lib src/HardwarePos.Infrastructure  HardwarePos.Infrastructure
new_lib src/HardwarePos.Devices         HardwarePos.Devices
new_lib src/HardwarePos.Reporting       HardwarePos.Reporting
new_lib src/HardwarePos.Backup          HardwarePos.Backup

echo "==> Creating test projects"
new_test tests/HardwarePos.Domain.Tests      HardwarePos.Domain.Tests
new_test tests/HardwarePos.Integration.Tests HardwarePos.Integration.Tests
new_test tests/HardwarePos.Device.Tests      HardwarePos.Device.Tests
new_test tests/HardwarePos.Acceptance.Tests  HardwarePos.Acceptance.Tests

echo "==> Wiring project references (see CLAUDE.md for the dependency rules)"
dotnet add src/HardwarePos.Application     reference src/HardwarePos.Domain
dotnet add src/HardwarePos.Infrastructure  reference src/HardwarePos.Application src/HardwarePos.Domain
dotnet add src/HardwarePos.Devices         reference src/HardwarePos.Application src/HardwarePos.Domain
dotnet add src/HardwarePos.Reporting       reference src/HardwarePos.Application src/HardwarePos.Domain
dotnet add src/HardwarePos.Backup          reference src/HardwarePos.Application src/HardwarePos.Domain

for t in Domain.Tests Integration.Tests Device.Tests Acceptance.Tests; do
  dotnet add "tests/HardwarePos.$t" reference src/HardwarePos.Domain src/HardwarePos.Application
done
dotnet add tests/HardwarePos.Integration.Tests reference src/HardwarePos.Infrastructure
dotnet add tests/HardwarePos.Device.Tests      reference src/HardwarePos.Devices

cat <<'NOTE'

==> Not created by this script, on purpose:

    src/HardwarePos.Ui   - Avalonia project. Create with the Avalonia templates:
                             dotnet new install Avalonia.Templates
                             dotnet new avalonia.mvvm -o src/HardwarePos.Ui -n HardwarePos.Ui
                             dotnet sln add src/HardwarePos.Ui/HardwarePos.Ui.csproj
                             dotnet add src/HardwarePos.Ui reference src/HardwarePos.Application src/HardwarePos.Domain
                           HardwarePos.Ui must NOT reference Infrastructure, Devices,
                           Reporting or Backup. The composition root wires those by interface.

    tools/SeedGenerator  - console app, created in P1-T16.

==> Next: write tests/HardwarePos.Domain.Tests/ArchitectureTests.cs
    (project boundaries + no double/float in Domain/Application/Infrastructure),
    then run: bash scripts/verify.sh

NOTE
echo "Done."
