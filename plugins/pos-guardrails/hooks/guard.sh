#!/usr/bin/env bash
# Portable version of .claude/hooks/guard-invariants.sh, shipped as a plugin so it
# can be reused across related repositories.
set -uo pipefail
INPUT=$(cat)
FILE=$(printf '%s' "$INPUT" | grep -o '"file_path"[[:space:]]*:[[:space:]]*"[^"]*"' | head -1 | sed 's/.*"file_path"[[:space:]]*:[[:space:]]*"//; s/"$//')
[ -z "${FILE:-}" ] && exit 0

case "$FILE" in
  *Migrations/*.cs)
    if [ -f "$FILE" ] && git ls-files --error-unmatch "$FILE" >/dev/null 2>&1; then
      echo "BLOCKED: $FILE is a committed migration. Migrations are forward-only - add a new one." >&2
      exit 2
    fi
    ;;
esac
exit 0
