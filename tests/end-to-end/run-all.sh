#!/usr/bin/env bash
# SqliteHost full verification matrix — the local entry point that runs
# every language suite, then the cross-language goldens. CI splits the
# same work across per-language jobs (see docs/testing.md).
set -euo pipefail

cd "$(dirname "$0")/../.."
ROOT="$(pwd)"

# The playground's browser tests run against an already-installed
# Chromium (PLAYWRIGHT_BROWSERS_PATH, or `pnpm exec playwright install
# chromium`). The pnpm install below must not have Playwright's
# postinstall go download the rest of its browser set; if the expected
# Chromium is missing, the browser step further down is where that must
# fail, loudly.
export PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=1

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

echo "==> C# slim-build smoke (SQLITEHOST_SLIM compiles)"
(cd "$ROOT/csharp" && "$DOTNET" build SqliteHost.Runtime/SqliteHost.Runtime.csproj \
  -c Release --nologo -v q -p:SqliteHostSlim=true)

# The behavioural half of the vendoring build, and the ci.yml job the
# compile above is NOT a substitute for: the tests pinning the stripped
# strict checks compile out under the same #if, so what runs here is the
# functional core. A gate accidentally stripped along with the strict
# checks shows up here and nowhere else.
echo "==> C# under SQLITEHOST_SLIM (dotnet test)"
(cd "$ROOT" && "$DOTNET" test csharp/SqliteHost.Tests/ --nologo -p:SqliteHostSlim=true)

echo "==> Java (mvn test)"
(cd "$ROOT/java" && mvn -q test)

echo "==> npm workspace (pnpm -r test)"
(cd "$ROOT" && pnpm install --silent && pnpm -r run test)

echo "==> Cross-language goldens"
node "$ROOT/tests/cross-language-golden/run.mjs"

echo "==> Script-delivery goldens (TS signer bytes verify under .NET)"
node "$ROOT/tests/delivery-golden/run.mjs"

# The runners above prove each validator reports what a case expects; these
# two prove the corpus itself is sound (no orphan in either direction, one
# fault per invalid case, a fixture for every pinned code) and that the
# checker can still fail. Both gate in ci.yml's goldens job.
echo "==> Fixture corpus check"
node "$ROOT/scripts/check-fixture-corpus.mjs"

echo "==> Fixture corpus checker self-test"
node "$ROOT/scripts/check-fixture-corpus.mjs" --self-test

echo "==> Unity package sync check"
node "$ROOT/unity/sync.mjs" --check

echo "==> Unity vendor-trim (each profile trims + compiles alone)"
node "$ROOT/tests/vendor-trim/run.mjs"

# Costs 30 ms and needs nothing. release.yml runs the tag-checking form
# (check-versions.mjs v1.2.3) only once a release is already being cut, so
# a missed manifest found there means re-cutting it; ci.yml gates the
# argument-free form on every pull request.
echo "==> Version lockstep across every release-please manifest"
node "$ROOT/scripts/check-versions.mjs"

# The npm suite above proves the playground's pipeline under Node; this
# proves the shipped page in a real browser (rendering, event wiring,
# debounce, tab switching). build:web first because the tests load
# web-dist/, which is not committed.
echo "==> Playground in a real browser (Playwright, Chromium)"
(cd "$ROOT" && pnpm --dir typescript/playground run build:web && \
  pnpm --dir typescript/playground run test:e2e)

echo "ALL SUITES GREEN"
echo "(real-SQLite version matrix runs separately: tests/compatibility-sqlite/run-matrix.sh)"
