#!/usr/bin/env bash
# Deterministic state operations for the /autopilot orchestration command.
#
# The orchestrating agent is not allowed to edit files, so every state
# transition goes through this script. That keeps the ledger honest: a task
# cannot be marked done by an agent that merely believes it is done.
#
# Usage:
#   autopilot.sh ready [n]              list the next n ready tasks
#   autopilot.sh info <TASK>            print one task's row
#   autopilot.sh status <TASK>          print just the status
#   autopilot.sh mark <TASK> <status> [note]
#   autopilot.sh log <TASK> <event> <detail...>
#   autopilot.sh session-start [n]
#   autopilot.sh session-end <summary...>
#   autopilot.sh check                  sanity check the ledger
set -uo pipefail
cd "$(dirname "$0")/.."

LEDGER=".claude/state/PROGRESS.md"
DEPS=".claude/state/deps.tsv"
LOG=".claude/state/autopilot-log.md"
VALID_STATUS="todo in-progress done blocked deferred"

die() { echo "ERROR: $*" >&2; exit 1; }
[ -f "$LEDGER" ] || die "$LEDGER not found"
[ -f "$DEPS" ]   || die "$DEPS not found"

now() { date -u +"%Y-%m-%dT%H:%M:%SZ"; }
today() { date -u +"%Y-%m-%d"; }

# status of a task from the ledger (3rd pipe column)
task_status() {
  awk -F'|' -v id="$1" '
    { gsub(/^[ \t]+|[ \t]+$/, "", $2); gsub(/^[ \t]+|[ \t]+$/, "", $4) }
    $2 == id { print $4; found=1; exit }
    END { if (!found) print "unknown" }
  ' "$LEDGER"
}

deps_row() {
  grep -P "^$1\t" "$DEPS" 2>/dev/null || grep "^$1	" "$DEPS" 2>/dev/null
}

cmd_ready() {
  local want="${1:-3}" count=0
  while IFS=$'\t' read -r id deps est mode title; do
    case "$id" in \#*|"") continue ;; esac
    local st; st=$(task_status "$id")
    [ "$st" = "todo" ] || continue

    local blocked=0 blockers=""
    if [ "$deps" != "-" ]; then
      local d
      for d in ${deps//,/ }; do
        local ds; ds=$(task_status "$d")
        if [ "$ds" != "done" ]; then blocked=1; blockers="$blockers $d($ds)"; fi
      done
    fi
    [ "$blocked" -eq 1 ] && continue

    printf '%s\t%s\t%s\t%s\n' "$id" "$mode" "$est" "$title"
    count=$((count+1))
    [ "$count" -ge "$want" ] && break
  done < "$DEPS"
  [ "$count" -eq 0 ] && echo "NONE READY"
  return 0
}

cmd_info() {
  local id="$1"
  local row; row=$(deps_row "$id") || die "unknown task $id"
  IFS=$'\t' read -r tid deps est mode title <<< "$row"
  echo "id:      $tid"
  echo "title:   $title"
  echo "status:  $(task_status "$tid")"
  echo "mode:    $mode"
  echo "est:     ${est}d"
  echo "deps:    $deps"
  if [ "$deps" != "-" ]; then
    local d
    for d in ${deps//,/ }; do echo "  - $d: $(task_status "$d")"; done
  fi
}

cmd_mark() {
  local id="$1" status="$2" note="${3:-}"
  echo "$VALID_STATUS" | grep -qw "$status" || die "invalid status '$status' (use: $VALID_STATUS)"
  deps_row "$id" >/dev/null || die "unknown task $id"

  # Only one task may be in-progress at a time.
  if [ "$status" = "in-progress" ]; then
    local open; open=$(grep -c '| in-progress |' "$LEDGER" || true)
    if [ "$open" -gt 0 ] && ! grep -q "| $id .*| in-progress |" "$LEDGER"; then
      die "another task is already in-progress. Finish or reset it first."
    fi
  fi

  local date_col=""
  [ "$status" = "done" ] && date_col="$(today)"

  local tmp; tmp=$(mktemp)
  awk -F'|' -v OFS='|' -v id="$id" -v st="$status" -v dt="$date_col" -v nt="$note" '
    {
      key=$2; gsub(/^[ \t]+|[ \t]+$/, "", key)
      if (NF >= 6 && key == id) {
        $4 = " " st " "
        if (dt != "") $5 = " " dt " "
        if (nt != "") $6 = " " nt " "
        print; next
      }
      print
    }' "$LEDGER" > "$tmp" && mv "$tmp" "$LEDGER"

  grep -q "| $id .*| $status |" "$LEDGER" || die "failed to update $id in the ledger"
  echo "$id -> $status${note:+ ($note)}"
}

cmd_log() {
  local id="$1" event="$2"; shift 2
  local detail="$*"
  [ -f "$LOG" ] || cat > "$LOG" <<'HDR'
# Autopilot run log

Append-only record of unattended runs. Written by `scripts/autopilot.sh`, never
by hand. Read this to find out what a run actually did, in what order, and why
it stopped.

HDR
  printf -- '- `%s` **%s** %s — %s\n' "$(now)" "$id" "$event" "$detail" >> "$LOG"
}

cmd_session_start() {
  local n="${1:-2}"
  [ -f "$LOG" ] || cmd_log "-" "init" "log created"
  {
    printf '\n## Run %s\n\n' "$(now)"
    printf -- '- branch: `%s`\n' "$(git rev-parse --abbrev-ref HEAD 2>/dev/null || echo n/a)"
    printf -- '- head: `%s`\n' "$(git rev-parse --short HEAD 2>/dev/null || echo n/a)"
    printf -- '- budget: %s task(s)\n\n' "$n"
  } >> "$LOG"
  echo "session started (budget: $n)"
}

cmd_session_end() {
  printf -- '\n**Run ended %s** — %s\n' "$(now)" "$*" >> "$LOG"
  echo "session ended"
}

cmd_check() {
  local problems=0
  local open; open=$(grep -c '| in-progress |' "$LEDGER" || true)
  if [ "$open" -gt 1 ]; then
    echo "FAIL: $open tasks marked in-progress; only one is allowed"; problems=1
  fi
  # every task in deps.tsv must exist in the ledger
  while IFS=$'\t' read -r id _ _ _ _; do
    case "$id" in \#*|"") continue ;; esac
    [ "$(task_status "$id")" = "unknown" ] && { echo "FAIL: $id in deps.tsv but not in the ledger"; problems=1; }
  done < "$DEPS"
  # a done task whose dependency is not done is a broken ordering
  while IFS=$'\t' read -r id deps _ _ _; do
    case "$id" in \#*|"") continue ;; esac
    [ "$(task_status "$id")" = "done" ] || continue
    [ "$deps" = "-" ] && continue
    local d
    for d in ${deps//,/ }; do
      [ "$(task_status "$d")" != "done" ] && { echo "WARN: $id is done but dependency $d is $(task_status "$d")"; }
    done
  done < "$DEPS"
  [ "$problems" -eq 0 ] && echo "ledger OK"
  return "$problems"
}

case "${1:-}" in
  ready)         shift; cmd_ready "$@" ;;
  info)          shift; [ $# -ge 1 ] || die "usage: info <TASK>"; cmd_info "$@" ;;
  status)        shift; [ $# -ge 1 ] || die "usage: status <TASK>"; task_status "$1" ;;
  mark)          shift; [ $# -ge 2 ] || die "usage: mark <TASK> <status> [note]"; cmd_mark "$@" ;;
  log)           shift; [ $# -ge 2 ] || die "usage: log <TASK> <event> <detail>"; cmd_log "$@" ;;
  session-start) shift; cmd_session_start "$@" ;;
  session-end)   shift; cmd_session_end "$@" ;;
  check)         cmd_check ;;
  *) cat <<USAGE
usage: scripts/autopilot.sh <command>

  ready [n]                    next n tasks whose dependencies are all done
  info <TASK>                  task detail and dependency status
  status <TASK>                status only
  mark <TASK> <status> [note]  todo | in-progress | done | blocked | deferred
  log <TASK> <event> <detail>  append to the run log
  session-start [n]            open a run block in the log
  session-end <summary>        close it
  check                        sanity check the ledger
USAGE
     exit 1 ;;
esac
