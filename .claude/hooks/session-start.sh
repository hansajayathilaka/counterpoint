#!/usr/bin/env bash
# SessionStart hook for Claude Code on the web.
#
# The dev container gets the .NET SDK, node and commitlint/husky wiring via
# devcontainer features and .devcontainer/post-create.sh. A Claude Code web
# session boots from a plain sandbox with none of that, so this hook brings
# it up to the same baseline: the pinned .NET SDK (global.json), npm deps so
# husky's commit-msg hook enforces Conventional Commits, and the system deps
# scripts/tests rely on (sqlite3 CLI, PDF fonts).
#
# Safe to run multiple times (every check is skip-if-already-present).
set -euo pipefail

if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

cd "$CLAUDE_PROJECT_DIR"

echo "==> Counterpoint session-start setup"

# --- .NET SDK, pinned by global.json ------------------------------------
DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
export DOTNET_ROOT
export PATH="$DOTNET_ROOT:$DOTNET_ROOT/tools:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1

if ! command -v dotnet >/dev/null 2>&1; then
  DOTNET_VERSION=$(sed -n 's/.*"version"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' global.json | head -1)
  DOTNET_VERSION="${DOTNET_VERSION:-10.0.100}"
  echo "==> Installing .NET SDK $DOTNET_VERSION (this can take a minute)"
  curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
  bash /tmp/dotnet-install.sh --version "$DOTNET_VERSION" --install-dir "$DOTNET_ROOT" --no-path
  rm -f /tmp/dotnet-install.sh
fi

echo "==> dotnet $(dotnet --version 2>/dev/null || echo 'not available')"

# Persist for the rest of the session (every subsequent shell / tool call).
{
  echo "export DOTNET_ROOT=\"$DOTNET_ROOT\""
  echo "export PATH=\"$DOTNET_ROOT:$DOTNET_ROOT/tools:\$PATH\""
  echo "export DOTNET_CLI_TELEMETRY_OPTOUT=1"
  echo "export DOTNET_NOLOGO=1"
  echo "export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1"
} >> "$CLAUDE_ENV_FILE"

dotnet tool install --global dotnet-ef --version 10.* >/dev/null 2>&1 \
  || dotnet tool update --global dotnet-ef >/dev/null 2>&1 \
  || true

# --- npm deps: commitlint + husky enforce Conventional Commits ----------
if [ -f package.json ] && [ ! -d node_modules ]; then
  echo "==> Installing npm dependencies (commitlint, husky, semantic-release)"
  npm install --no-audit --no-fund
fi

# `npm install` runs the `prepare` script (husky) which wires up
# .husky/commit-msg as core.hooksPath; confirm it actually landed.
if [ -f package.json ] && ! git config --get core.hooksPath >/dev/null 2>&1; then
  npx --no -- husky >/dev/null 2>&1 || true
fi

# --- System deps used by scripts and tests -------------------------------
if ! command -v sqlite3 >/dev/null 2>&1; then
  echo "==> Installing sqlite3 CLI"
  apt-get update -qq && apt-get install -y -qq sqlite3 libsqlite3-dev >/dev/null
fi

if [ ! -d /usr/share/fonts/truetype/dejavu ]; then
  echo "==> Installing fonts for PDF rendering (QuestPDF)"
  apt-get install -y -qq fontconfig fonts-dejavu-core >/dev/null
fi

mkdir -p artifacts/data artifacts/backups artifacts/receipts artifacts/logs

# --- Restore the solution, once it exists (P0-T01) ------------------------
if [ -f Counterpoint.sln ]; then
  echo "==> Restoring NuGet packages"
  dotnet restore || echo "!! restore failed - check Directory.Packages.props versions"
else
  echo "==> No solution yet. Run: bash scripts/bootstrap-solution.sh   (this is task P0-T01)"
fi

echo "==> Session-start setup complete"
