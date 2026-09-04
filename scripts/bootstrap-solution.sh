#!/usr/bin/env bash
# Task P0-T01: create the solution and project skeleton.
#
# Run once. Idempotent - it skips anything that already exists.
# Package versions are pinned centrally in Directory.Packages.props; if restore
# fails, adjust the version there rather than in an individual project.
set -euo pipefail
cd "$(dirname "$0")/.."

if [ -f Counterpoint.sln ]; then
  echo "Counterpoint.sln already exists. Nothing to do."
  exit 0
fi

echo "==> Creating solution"
dotnet new sln -n Counterpoint

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
new_lib src/Counterpoint.Domain          Counterpoint.Domain
new_lib src/Counterpoint.Application     Counterpoint.Application
new_lib src/Counterpoint.Infrastructure  Counterpoint.Infrastructure
new_lib src/Counterpoint.Devices         Counterpoint.Devices
new_lib src/Counterpoint.Reporting       Counterpoint.Reporting
new_lib src/Counterpoint.Backup          Counterpoint.Backup

echo "==> Creating test projects"
new_test tests/Counterpoint.Domain.Tests      Counterpoint.Domain.Tests
new_test tests/Counterpoint.Integration.Tests Counterpoint.Integration.Tests
new_test tests/Counterpoint.Device.Tests      Counterpoint.Device.Tests
new_test tests/Counterpoint.Acceptance.Tests  Counterpoint.Acceptance.Tests

echo "==> Wiring project references (see CLAUDE.md for the dependency rules)"
dotnet add src/Counterpoint.Application     reference src/Counterpoint.Domain
dotnet add src/Counterpoint.Infrastructure  reference src/Counterpoint.Application src/Counterpoint.Domain
dotnet add src/Counterpoint.Devices         reference src/Counterpoint.Application src/Counterpoint.Domain
dotnet add src/Counterpoint.Reporting       reference src/Counterpoint.Application src/Counterpoint.Domain
dotnet add src/Counterpoint.Backup          reference src/Counterpoint.Application src/Counterpoint.Domain

for t in Domain.Tests Integration.Tests Device.Tests Acceptance.Tests; do
  dotnet add "tests/Counterpoint.$t" reference src/Counterpoint.Domain src/Counterpoint.Application
done
dotnet add tests/Counterpoint.Integration.Tests reference src/Counterpoint.Infrastructure
dotnet add tests/Counterpoint.Device.Tests      reference src/Counterpoint.Devices

cat <<'NOTE'

==> Not created by this script, on purpose:

    src/Counterpoint.Ui   - Avalonia project. Create with the Avalonia templates:
                             dotnet new install Avalonia.Templates
                             dotnet new avalonia.mvvm -o src/Counterpoint.Ui -n Counterpoint.Ui
                             dotnet sln add src/Counterpoint.Ui/Counterpoint.Ui.csproj
                             dotnet add src/Counterpoint.Ui reference src/Counterpoint.Application src/Counterpoint.Domain
                           Counterpoint.Ui must NOT reference Infrastructure, Devices,
                           Reporting or Backup. The composition root wires those by interface.

    tools/SeedGenerator  - console app, created in P1-T16.

==> Next: write tests/Counterpoint.Domain.Tests/ArchitectureTests.cs
    (project boundaries + no double/float in Domain/Application/Infrastructure),
    then run: bash scripts/verify.sh

NOTE
echo "Done."
