#!/usr/bin/env bash
# Real-SQLite compatibility matrix for the C# suite.
#
# For each SQLite version below (plus the newest amalgamation published on
# sqlite.org, resolved at runtime) this script downloads the amalgamation,
# compiles it into a shared library, and runs the full C# test suite against
# that native build via the SQLITEHOST_NATIVE_SQLITE dynamic-provider
# override (see csharp/SqliteHost.Tests/Adapter/NativeSqliteOverride.cs).
#
# Exit status: non-zero if ANY version fails. 3.9.0 and 3.9.2 are below the
# documented floor (docs/compatibility.md); on those engines the
# runtime-driven suites skip with a reason (SampleHostFloor) while
# FloorGateTests assert the sqlite-version-too-low gate itself plus a
# lowered-floor end-to-end run, so below-floor rows are expected green too.
set -u -o pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
CACHE_DIR="$SCRIPT_DIR/.cache"
SOLUTION="$PROJECT_ROOT/csharp/SqliteHost.sln"
FLOOR_VERSION="3.19.3"
FLOOR_VERSION_NUMBER=3019003   # sqlite3_libversion_number encoding of the floor

# The four runtime-driven conformance tests live in the shippable
# SqliteHost.Conformance base class, whose only skip hook
# (SkipEntireSuiteReason) disables the WHOLE suite — including the
# adapter-level sections that measurably pass below the floor. Below-floor
# cells therefore exclude exactly those four methods instead, so the
# adapter-level conformance coverage keeps running; every other
# runtime-driven test skips itself with a reason via SampleHostFloor.
BELOW_FLOOR_FILTER='FullyQualifiedName!~UnknownBinding_FailsMissingBinding'
BELOW_FLOOR_FILTER+='&FullyQualifiedName!~ExtraBinding_FailsUnusedBinding'
BELOW_FLOOR_FILTER+='&FullyQualifiedName!~ErrorMidStep_AbortsTheStep'
BELOW_FLOOR_FILTER+='&FullyQualifiedName!~ScalarFunction_NullForRequiredArg'

mkdir -p "$CACHE_DIR"

if ! command -v dotnet >/dev/null 2>&1; then
  export PATH="/opt/dotnet:$PATH"
fi
export DOTNET_CLI_TELEMETRY_OPTOUT=1

# --- version list: "dotted:year:zipnumber" -------------------------------
VERSIONS=(
  "3.9.0:2015:3090000"
  "3.9.2:2015:3090200"
  "3.19.3:2017:3190300"
  "3.28.0:2019:3280000"
)

# Resolve the newest amalgamation from sqlite.org/download.html; fall back
# to the last one observed when the page is unreachable.
FALLBACK_LATEST="3.53.3:2026:3530300"
latest_entry="$FALLBACK_LATEST"
latest_path="$(curl -fsS --max-time 30 https://sqlite.org/download.html 2>/dev/null \
  | grep -oE '20[0-9]{2}/sqlite-amalgamation-[0-9]{7}\.zip' | head -1 || true)"
if [ -n "$latest_path" ]; then
  year="${latest_path%%/*}"
  num="$(echo "$latest_path" | grep -oE '[0-9]{7}')"
  # Encoding MNNPP00 -> M.NN.PP (e.g. 3530300 -> 3.53.3)
  maj="${num:0:1}"
  min="$((10#${num:1:2}))"
  pat="$((10#${num:3:2}))"
  latest_entry="${maj}.${min}.${pat}:${year}:${num}"
else
  echo "WARN: could not resolve latest version from sqlite.org; using pinned fallback ${FALLBACK_LATEST%%:*}" >&2
fi
# Avoid duplicating a pinned version.
case " ${VERSIONS[*]} " in
  *" $latest_entry "*) ;;
  *) VERSIONS+=("$latest_entry") ;;
esac

# --- build one native library --------------------------------------------
# prints the .so path on success, returns non-zero on failure
build_native() {
  local ver="$1" year="$2" num="$3"
  local so="$CACHE_DIR/libsqlite3-$ver.so"
  if [ -f "$so" ]; then echo "$so"; return 0; fi

  local zip="$CACHE_DIR/sqlite-amalgamation-$num.zip"
  local src_dir="$CACHE_DIR/sqlite-amalgamation-$num"
  if [ ! -f "$zip" ]; then
    echo "  downloading sqlite $ver (https://sqlite.org/$year/sqlite-amalgamation-$num.zip)" >&2
    curl -fsS --max-time 300 -o "$zip.tmp" "https://sqlite.org/$year/sqlite-amalgamation-$num.zip" \
      || { rm -f "$zip.tmp"; return 1; }
    mv "$zip.tmp" "$zip"
  fi
  rm -rf "$src_dir"
  unzip -q -o "$zip" -d "$CACHE_DIR" || return 1
  echo "  compiling libsqlite3-$ver.so" >&2
  gcc -shared -fPIC -O2 -o "$so" "$src_dir/sqlite3.c" -lpthread -ldl -lm || return 1
  echo "$so"
}

# --- run ------------------------------------------------------------------
declare -a RESULT_VERSION RESULT_STATUS RESULT_DETAIL
overall_failure=0

version_ge() { # version_ge A B: is A >= B?
  [ "$(printf '%s\n%s\n' "$1" "$2" | sort -V | head -1)" = "$2" ]
}

for entry in "${VERSIONS[@]}"; do
  ver="${entry%%:*}"; rest="${entry#*:}"
  year="${rest%%:*}"; num="${rest#*:}"
  echo ""
  echo "=== SQLite $ver ==="

  so="$(build_native "$ver" "$year" "$num")"
  if [ -z "$so" ] || [ ! -f "$so" ]; then
    RESULT_VERSION+=("$ver"); RESULT_STATUS+=("BUILD-FAIL"); RESULT_DETAIL+=("download/compile failed")
    overall_failure=1
    continue
  fi

  # Numeric version identity (sqlite3_libversion_number encoding):
  # major*1000000 + minor*1000 + patch, derived deterministically from the
  # dotted version string.
  IFS=. read -r vmaj vmin vpat <<<"$ver"
  vernum=$((vmaj * 1000000 + vmin * 1000 + vpat))

  extra_args=()
  if [ "$vernum" -lt "$FLOOR_VERSION_NUMBER" ]; then
    extra_args+=(--filter "$BELOW_FLOOR_FILTER")
  fi

  log="$CACHE_DIR/test-$ver.log"
  if SQLITEHOST_NATIVE_SQLITE="$so" \
     SQLITEHOST_EXPECTED_SQLITE_VERSION="$ver" \
     SQLITEHOST_EXPECTED_SQLITE_VERSION_NUMBER="$vernum" \
     dotnet test "$SOLUTION" --nologo -v q "${extra_args[@]}" >"$log" 2>&1; then
    status="PASS"
  else
    status="FAIL"
    overall_failure=1
  fi
  summary="$(grep -E 'Failed:.*Passed:.*Total:' "$log" | tail -1 \
    | sed -E 's/.*(Failed:[ ]*[0-9]+),[ ]*(Passed:[ ]*[0-9]+),[ ]*(Skipped:[ ]*[0-9]+),[ ]*(Total:[ ]*[0-9]+).*/\1, \2, \3, \4/' \
    | tr -s ' ')"
  echo "  $status — ${summary:-no test summary (see $log)}"
  RESULT_VERSION+=("$ver"); RESULT_STATUS+=("$status"); RESULT_DETAIL+=("${summary:-see $log}")
done

# --- summary table ---------------------------------------------------------
echo ""
echo "================ SQLite compatibility matrix ================"
printf '%-10s %-12s %-8s %s\n' "version" "status" "floor" "detail"
for i in "${!RESULT_VERSION[@]}"; do
  v="${RESULT_VERSION[$i]}"
  floor="no"
  version_ge "$v" "$FLOOR_VERSION" && floor="yes"
  printf '%-10s %-12s %-8s %s\n' "$v" "${RESULT_STATUS[$i]}" "$floor" "${RESULT_DETAIL[$i]}"
done
echo "============================================================="

if [ "$overall_failure" -ne 0 ]; then
  echo "MATRIX FAILURE: a version row did not pass (below-floor rows are expected green too)." >&2
  exit 1
fi
echo "All versions passed (>= $FLOOR_VERSION runs the full suite; below-floor rows skip the runtime-driven suites and prove the gate via FloorGateTests)."
