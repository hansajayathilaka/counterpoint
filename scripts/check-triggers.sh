#!/usr/bin/env bash
# Append-only trigger survival (CLAUDE.md invariant 5, docs/01_DATA_MODEL.md §8).
#
# EF Core's SQLite provider rebuilds a table (create-copy-drop-rename) for almost any alter, and a
# rebuild silently drops that table's triggers. A migration that looks like it only added a column
# can therefore leave the bill ledger editable with nothing to show for it.
#
# The check itself lives in the integration tests, against a real encrypted database with the whole
# migration chain applied - it compares sqlite_schema to the manifest in
# src/Counterpoint.Infrastructure/Data/AppendOnlyTables.cs. This script is the entry point
# scripts/verify.sh calls, so the check has a name of its own in the verification output.
set -uo pipefail
cd "$(dirname "$0")/.."

# Must match whatever configuration the caller already built (scripts/verify.sh builds Debug;
# CI's build-and-test job builds Release) - --no-build fails outright against the other one.
CONFIGURATION="${CONFIGURATION:-Debug}"

dotnet test tests/Counterpoint.Integration.Tests/Counterpoint.Integration.Tests.csproj \
  --no-build -c "$CONFIGURATION" \
  --filter "FullyQualifiedName~AppendOnlyTriggerTests"
