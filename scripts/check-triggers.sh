#!/usr/bin/env bash
# Applies the full migration chain to a scratch database and asserts that every
# append-only table still has both triggers.
#
# This exists because EF Core's SQLite provider rebuilds tables for some alters,
# which silently drops triggers. Without this check, append-only protection can
# disappear without a single test failing.
set -uo pipefail
cd "$(dirname "$0")/.."

DB="artifacts/data/trigger-check.db"
mkdir -p artifacts/data
rm -f "$DB"

TABLES="sale sale_line payment sale_return sale_return_line stock_movement shift cash_movement audit_log"

echo "Applying migration chain to $DB"
POS_DB_PATH="$DB" POS_DEV_MODE=1 dotnet ef database update \
  --project src/Counterpoint.Infrastructure \
  --startup-project src/Counterpoint.Ui >/dev/null 2>&1 || {
    echo "FAIL: migrations did not apply"; exit 1; }

MISSING=0
for t in $TABLES; do
  if ! sqlite3 "$DB" "SELECT name FROM sqlite_master WHERE type='table' AND name='$t';" | grep -q .; then
    continue  # table not created yet at this point in the build
  fi
  COUNT=$(sqlite3 "$DB" "SELECT COUNT(*) FROM sqlite_master WHERE type='trigger' AND tbl_name='$t';")
  if [ "$COUNT" -lt 2 ]; then
    echo "FAIL: table '$t' has $COUNT trigger(s), expected at least 2 (no-update, no-delete)"
    MISSING=1
  fi
done

INTEGRITY=$(sqlite3 "$DB" "PRAGMA integrity_check;")
if [ "$INTEGRITY" != "ok" ]; then
  echo "FAIL: integrity_check returned: $INTEGRITY"
  MISSING=1
fi

rm -f "$DB"
[ "$MISSING" -eq 0 ] && echo "OK: append-only triggers intact, integrity_check ok"
exit "$MISSING"
