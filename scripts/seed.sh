#!/usr/bin/env bash
# Generates the performance-test dataset: 20,000 SKUs and 100,000 historical
# bill lines. AC-18 and every performance budget are measured against this,
# never against an empty database.
#
# --output is a data-directory root (PosDataDirectory's convention), not a bare
# file: the encrypted database ends up at <output>/db/counterpoint.db, exactly
# where a real till's data directory would put it, so the same root can be
# pointed straight at `--verify` (the hash-chain check) or at the sales screen
# for a manual look, with no separate export/import step.
set -euo pipefail
cd "$(dirname "$0")/.."

SKUS="${1:-20000}"
LINES="${2:-100000}"

mkdir -p artifacts/data
echo "Seeding $SKUS SKUs and $LINES bill lines..."
dotnet run --project tools/SeedGenerator -c Release -- \
  --skus "$SKUS" --lines "$LINES" --output artifacts/data/pos-perf
echo "Done: artifacts/data/pos-perf/db/counterpoint.db"
