#!/usr/bin/env bash
# Generates the performance-test dataset: 20,000 SKUs and 100,000 historical
# bill lines. AC-18 and every performance budget are measured against this,
# never against an empty database.
set -euo pipefail
cd "$(dirname "$0")/.."

SKUS="${1:-20000}"
LINES="${2:-100000}"

mkdir -p artifacts/data
echo "Seeding $SKUS SKUs and $LINES bill lines..."
dotnet run --project tools/SeedGenerator -c Release -- \
  --skus "$SKUS" --lines "$LINES" --output artifacts/data/pos-perf.db
echo "Done: artifacts/data/pos-perf.db"
