#!/usr/bin/env bash
# Real-SQLite compatibility matrix for the C# suite.
#
# For each SQLite version below (plus the newest amalgamation published on
# sqlite.org, resolved at runtime) this script downloads the amalgamation,
# compiles it into a shared library, and runs the full C# test suite against
# that native build via the SQLITEHOST_NATIVE_SQLITE dynamic-provider
# override (see csharp/SqliteHost.Tests/Adapter/NativeSqliteOverride.cs).
#
# Usage: run-matrix.sh [version|latest ...]   (no arguments = every version)
# CI names one version per matrix leg so a failure names the engine.
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

# --- skip budget ----------------------------------------------------------
# A green leg proves nothing until you know how much of the suite it ran.
# Every cell skips a large, DESIGNED fraction (README "What the numbers
# mean"): the two adapters that bundle their own SQLite skip whenever the
# override is active, and below the floor every runtime-driven test skips
# itself through the version gate. That is 22% of the suite at the floor
# and 44% below it — and until these ceilings existed, an inverted skip
# predicate could have taken it to 100% and still reported PASS.
#
# The ceilings are the measured skip counts plus room for ordinary test
# growth; the passed floors are the same in reverse. They are deliberately
# far below what any inverted predicate produces (an at-floor leg that
# started skipping everything runtime-driven lands near the below-floor
# number, ~330). Recompute after a change that adds adapter-parameterized
# tests: run one leg, read `Skipped:`/`Passed:`, round up/down with slack,
# and update the README table with the same numbers.
#
# Measured 2026-09-07 on the fix/coverage-hunt3 tree (Total 768):
#   3.19.3   Passed 596  Skipped 172        3.28.0  Passed 598  Skipped 170
#   3.9.0    Passed 419  Skipped 333  (Total 752 = 768 - 16 filtered)
MAX_SKIPPED_AT_FLOOR=220
MIN_PASSED_AT_FLOOR=520
MAX_SKIPPED_BELOW_FLOOR=400
MIN_PASSED_BELOW_FLOOR=360

mkdir -p "$CACHE_DIR"

if ! command -v dotnet >/dev/null 2>&1; then
  export PATH="/opt/dotnet:$PATH"
fi
export DOTNET_CLI_TELEMETRY_OPTOUT=1

# --- version list: "dotted:year:zipnumber:sha256" -------------------------
# The four pinned rows carry the SHA-256 of the amalgamation zip: this
# script downloads C source over the network and then compiles and EXECUTES
# it, so the bytes are pinned, not just the URL. The hashes were recorded
# from sqlite.org and are trust-on-first-use — a mismatch means the archive
# changed under a URL that is supposed to be immutable, which is a stop, not
# a retry.
VERSIONS=(
  "3.9.0:2015:3090000:e92e77efb885a1b2a2b9c1813b7c6ebd8ececa39e36428d6ff0845825e97553b"
  "3.9.2:2015:3090200:567139c94375e3808a11f34d81f534d0c257e2c498cddbf4cac283d74b51fe9c"
  "3.19.3:2017:3190300:130185efe772a7392c5cecb4613156aba12f37b335ef91e171c345e197eabdc1"
  "3.28.0:2019:3280000:d02fc4e95cfef672b45052e221617a050b7f2e20103661cda88387349a9b1327"
)

# Resolve the newest amalgamation from sqlite.org/download.html; fall back
# to the last one observed when the page is unreachable. This row is
# non-hermetic by construction — an upstream release changes what it tests —
# so it carries no checksum ("-") and CI runs it as an advisory leg.
FALLBACK_LATEST="3.53.3:2026:3530300:-"
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
  latest_entry="${maj}.${min}.${pat}:${year}:${num}:-"
else
  echo "WARN: could not resolve latest version from sqlite.org; using pinned fallback ${FALLBACK_LATEST%%:*}" >&2
fi
# Avoid duplicating a pinned version (compare the dotted version only: the
# pinned rows carry a checksum the resolved one does not).
latest_version="${latest_entry%%:*}"
already_pinned=0
for entry in "${VERSIONS[@]}"; do
  [ "${entry%%:*}" = "$latest_version" ] && already_pinned=1
done
[ "$already_pinned" -eq 1 ] || VERSIONS+=("$latest_entry")

# --- optional selection: run-matrix.sh [version|latest ...] ---------------
# With no arguments every version runs, which is what a developer wants.
# CI names one version per matrix leg so a failure names the engine, and
# `latest` selects whichever version the resolution above landed on.
if [ "$#" -gt 0 ]; then
  selected=()
  for want in "$@"; do
    [ "$want" = "latest" ] && want="$latest_version"
    for entry in "${VERSIONS[@]}"; do
      [ "${entry%%:*}" = "$want" ] && selected+=("$entry")
    done
  done
  if [ "${#selected[@]}" -eq 0 ]; then
    echo "No known SQLite version matched: $*" >&2
    echo "Known: $(for e in "${VERSIONS[@]}"; do printf '%s ' "${e%%:*}"; done)" >&2
    exit 2
  fi
  VERSIONS=("${selected[@]}")
fi

# --- build one native library --------------------------------------------
# prints the .so path on success, returns non-zero on failure
sha256_of() {
  if command -v sha256sum >/dev/null 2>&1; then sha256sum "$1" | cut -d' ' -f1
  else shasum -a 256 "$1" | cut -d' ' -f1
  fi
}

build_native() {
  local ver="$1" year="$2" num="$3" want_sha="${4:--}"
  local so="$CACHE_DIR/libsqlite3-$ver.so"
  local zip="$CACHE_DIR/sqlite-amalgamation-$num.zip"
  local src_dir="$CACHE_DIR/sqlite-amalgamation-$num"

  if [ ! -f "$zip" ]; then
    echo "  downloading sqlite $ver (https://sqlite.org/$year/sqlite-amalgamation-$num.zip)" >&2
    curl -fsS --max-time 300 -o "$zip.tmp" "https://sqlite.org/$year/sqlite-amalgamation-$num.zip" \
      || { rm -f "$zip.tmp"; return 1; }
    mv "$zip.tmp" "$zip"
  fi
  # Verified before anything is reused or compiled, not only after a
  # download: this is the gate a cached archive has to pass too. The .so
  # short-circuit below sits AFTER it on purpose — putting it first would
  # make the pin unreachable on exactly the runs that skip the download,
  # which is most of them.
  if [ "$want_sha" != "-" ]; then
    local got_sha; got_sha="$(sha256_of "$zip")"
    if [ "$got_sha" != "$want_sha" ]; then
      echo "  SHA-256 MISMATCH for sqlite $ver" >&2
      echo "    expected $want_sha" >&2
      echo "    got      $got_sha" >&2
      # The stale library goes too. Leaving it would let the next run
      # re-download a clean archive, pass the pin, and then short-circuit
      # straight back onto the library built from the rejected bytes.
      rm -f "$zip" "$so"
      return 1
    fi
  else
    echo "  (no checksum pin for $ver — resolved at runtime, see the version list)" >&2
  fi

  # Reuse a library this machine already built. CI never takes this path:
  # engine-matrix.yml caches the verified ARCHIVE and not the .so, so the
  # bytes the test process loads are always compiled from source that just
  # passed the pin above. Locally it is what makes a re-run instant.
  if [ -f "$so" ]; then echo "$so"; return 0; fi

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
  IFS=: read -r ver year num sha <<<"$entry"
  echo ""
  echo "=== SQLite $ver ==="

  so="$(build_native "$ver" "$year" "$num" "$sha")"
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

  # --- skip budget: a leg that ran almost nothing is not a passing leg ----
  if [ "$status" = "PASS" ]; then
    counts_line="$(grep -E 'Failed:.*Passed:.*Total:' "$log" | tail -1)"
    passed="$(printf '%s' "$counts_line" | sed -nE 's/.*Passed:[ ]*([0-9]+).*/\1/p')"
    skipped="$(printf '%s' "$counts_line" | sed -nE 's/.*Skipped:[ ]*([0-9]+).*/\1/p')"
    if [ -z "$passed" ] || [ -z "$skipped" ]; then
      status="NO-COUNTS"
      summary="could not read Passed/Skipped from $log"
      overall_failure=1
    else
      if [ "$vernum" -lt "$FLOOR_VERSION_NUMBER" ]; then
        max_skipped=$MAX_SKIPPED_BELOW_FLOOR; min_passed=$MIN_PASSED_BELOW_FLOOR; band="below-floor"
      else
        max_skipped=$MAX_SKIPPED_AT_FLOOR; min_passed=$MIN_PASSED_AT_FLOOR; band="at-or-above-floor"
      fi
      if [ "$skipped" -gt "$max_skipped" ] || [ "$passed" -lt "$min_passed" ]; then
        status="SKIP-BUDGET"
        summary="$band budget breached: Passed $passed (min $min_passed), Skipped $skipped (max $max_skipped)"
        echo "  SKIP BUDGET BREACHED for sqlite $ver" >&2
        echo "    passed  $passed (minimum $min_passed)" >&2
        echo "    skipped $skipped (maximum $max_skipped)" >&2
        echo "    Either a skip predicate inverted and the leg stopped testing this" >&2
        echo "    engine, or the suite grew legitimately — in which case recompute" >&2
        echo "    the budget at the top of this script AND the table in" >&2
        echo "    tests/compatibility-sqlite/README.md." >&2
        overall_failure=1
      fi
    fi
  fi

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
  echo "MATRIX FAILURE: a version row did not pass its tests or its skip budget" >&2
  echo "(below-floor rows are expected green too)." >&2
  exit 1
fi
echo "All versions passed (>= $FLOOR_VERSION runs the full suite; below-floor rows skip the runtime-driven suites and prove the gate via FloorGateTests)."
