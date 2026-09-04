#!/usr/bin/env bash
# Stop hook. Nudges the progress ledger back into sync if a task looks finished
# but PROGRESS.md was not updated.
set -uo pipefail

LEDGER="$CLAUDE_PROJECT_DIR/.claude/state/PROGRESS.md"
[ -f "$LEDGER" ] || exit 0

IN_PROGRESS=$(grep -c '| in-progress |' "$LEDGER" 2>/dev/null || echo 0)
if [ "$IN_PROGRESS" -gt 1 ]; then
  echo "More than one task is marked in-progress in .claude/state/PROGRESS.md. Work one task at a time (CLAUDE.md working agreement)." >&2
  exit 2
fi
exit 0
