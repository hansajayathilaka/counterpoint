#!/usr/bin/env bash
# Runs once when the dev container (or Claude Code web sandbox) is created.
# Keep it idempotent and keep it quiet on success.
set -euo pipefail

echo "==> Hardware Shop POS dev environment setup"

cd "$(dirname "$0")/.."

mkdir -p artifacts/data artifacts/backups artifacts/receipts artifacts/logs

# SQLCipher native dependency. SQLitePCLRaw.bundle_e_sqlcipher ships its own
# native binary, so this is only needed for the sqlite3 CLI used by scripts.
if ! command -v sqlite3 >/dev/null 2>&1; then
  echo "==> Installing sqlite3 CLI"
  sudo apt-get update -qq && sudo apt-get install -y -qq sqlite3 libsqlite3-dev >/dev/null
fi

# Fonts: QuestPDF renders PDFs headlessly and needs at least one real font family.
if [ ! -d /usr/share/fonts/truetype/dejavu ]; then
  echo "==> Installing fonts for PDF rendering"
  sudo apt-get install -y -qq fontconfig fonts-dejavu-core >/dev/null
fi

if command -v dotnet >/dev/null 2>&1; then
  echo "==> dotnet $(dotnet --version)"
  dotnet tool install --global dotnet-ef --version 10.* 2>/dev/null || dotnet tool update --global dotnet-ef 2>/dev/null || true

  if [ -f HardwarePos.sln ]; then
    echo "==> Restoring packages"
    dotnet restore || echo "!! restore failed - check Directory.Packages.props versions"
  else
    echo "==> No solution yet. Run: bash scripts/bootstrap-solution.sh   (this is task P0-T01)"
  fi
else
  echo "!! dotnet not found on PATH"
fi

echo "==> Ready. Start with:  /task-status   then   /next-task"
