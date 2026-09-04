#!/usr/bin/env bash
# PostToolUse hook. Warns (does not block) when a change looks like it violates
# the money or stock invariants. Exit 2 surfaces the message to Claude so it can
# self-correct before moving on.
set -uo pipefail

INPUT=$(cat)
FILE=$(printf '%s' "$INPUT" | grep -o '"file_path"[[:space:]]*:[[:space:]]*"[^"]*"' | head -1 | sed 's/.*"file_path"[[:space:]]*:[[:space:]]*"//; s/"$//')

[ -z "${FILE:-}" ] && exit 0
[ ! -f "$FILE" ] && exit 0
case "$FILE" in *.cs) ;; *) exit 0 ;; esac

PROBLEMS=""

case "$FILE" in
  */HardwarePos.Domain/*|*/HardwarePos.Application/*|*/HardwarePos.Infrastructure/*)
    if grep -nE '\b(double|float)\b' "$FILE" | grep -vE '^\s*[0-9]+:\s*//' | grep -q .; then
      PROBLEMS="${PROBLEMS}- 'double' or 'float' appears in $FILE. Money and quantity use the Money/Quantity value objects over decimal (CLAUDE.md invariant 1).\n"
    fi
    ;;
esac

if grep -q 'stock_balance' "$FILE" 2>/dev/null; then
  case "$FILE" in
    */StockLedger.cs|*/StockBalanceConfiguration.cs|*Tests*|*Migrations*) ;;
    *)
      PROBLEMS="${PROBLEMS}- $FILE references stock_balance directly. All stock changes go through StockLedger.PostAsync (CLAUDE.md invariant 3).\n"
      ;;
  esac
fi

if grep -qE 'AUTOINCREMENT|MAX\(\s*bill_no' "$FILE" 2>/dev/null; then
  PROBLEMS="${PROBLEMS}- $FILE looks like it derives a document number from AUTOINCREMENT or MAX(). Use number_sequence (CLAUDE.md invariant 4).\n"
fi

if grep -qE 'synchronous\s*=\s*(NORMAL|OFF)' "$FILE" 2>/dev/null; then
  PROBLEMS="${PROBLEMS}- $FILE weakens PRAGMA synchronous. It must stay FULL for NFR-R2 durability (CLAUDE.md invariant 9).\n"
fi

if [ -n "$PROBLEMS" ]; then
  printf 'Invariant check flagged this edit:\n%b' "$PROBLEMS" >&2
  exit 2
fi
exit 0
