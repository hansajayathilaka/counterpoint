#!/usr/bin/env bash
# PreToolUse hook. Blocks edits that would break structural invariants.
# Exit 2 = block the tool call and show stderr to Claude.
set -uo pipefail

INPUT=$(cat)
FILE=$(printf '%s' "$INPUT" | grep -o '"file_path"[[:space:]]*:[[:space:]]*"[^"]*"' | head -1 | sed 's/.*"file_path"[[:space:]]*:[[:space:]]*"//; s/"$//')

[ -z "${FILE:-}" ] && exit 0

# 1. Applied migrations are immutable. Edit forward with a new migration.
case "$FILE" in
  *Migrations/*.cs|*Migrations/*.Designer.cs)
    if [ -f "$FILE" ] && git -C "$CLAUDE_PROJECT_DIR" ls-files --error-unmatch "$FILE" >/dev/null 2>&1; then
      echo "BLOCKED: $FILE is a committed EF migration." >&2
      echo "Migrations are forward-only. Create a new migration instead of editing an applied one." >&2
      echo "See CLAUDE.md invariant 4 and docs/00_ENGINEERING_GUIDE.md section 6." >&2
      exit 2
    fi
    ;;
esac

# 2. The SRS is the client's document, not ours.
case "$FILE" in
  *docs/Hardware_Shop_POS_Requirements.md)
    echo "BLOCKED: the SRS is the client's signed document and is read-only here." >&2
    echo "Raise a change request in docs/adr/ instead." >&2
    exit 2
    ;;
esac

exit 0
