#!/usr/bin/env bash
# SqliteHost full verification matrix — the single entry point CI would
# call. Runs every language suite, then the cross-language goldens.
set -euo pipefail

cd "$(dirname "$0")/../.."
ROOT="$(pwd)"

if command -v dotnet >/dev/null 2>&1; then
  DOTNET=dotnet
elif [ -x /opt/dotnet/dotnet ]; then
  DOTNET=/opt/dotnet/dotnet
else
  echo "error: dotnet SDK not found" >&2
  exit 1
fi

echo "==> C# (dotnet test)"
(cd "$ROOT/csharp" && "$DOTNET" test --nologo)

echo "==> Java (mvn test)"
(cd "$ROOT/java" && mvn -q test)

echo "==> npm workspace (pnpm -r test)"
(cd "$ROOT" && pnpm install --silent && pnpm -r run test)

echo "==> Cross-language goldens"
node "$ROOT/tests/cross-language-golden/run.mjs"

echo "ALL SUITES GREEN"
