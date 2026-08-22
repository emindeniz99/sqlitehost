#!/usr/bin/env bash
# Assembles unity-project/Assets/Sources for one IL2CPP matrix row
# (docs/guides/il2cpp-size-protocol.md §4).
#
# Was a fenced code block in docs/reports/il2cpp-size-report.md §6; it is a
# real script now so CI can run the matrix. Two changes from the appendix
# version: the project is the committed one under unity-project/ instead of
# a scratch dir outside the repo, and rows 9-11 (the 5-method profiles the
# protocol needs for the per-method slope) are implemented.
#
# Usage: bash tests/app-size-bench/prepare-row.sh <row 0..11>
set -euo pipefail

ROW="$1"
BENCH="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$(cd "$BENCH/../.." && pwd)"
CS="$REPO/csharp"
PROJ="${BENCH_PROJECT:-$BENCH/unity-project}"
SRC="$PROJ/Assets/Sources"

if [ ! -d "$BENCH/out/unity-src" ]; then
  echo "tests/app-size-bench/out is missing — run: node tests/app-size-bench/generate.mjs" >&2
  exit 2
fi

rm -rf "$SRC" "$PROJ/Assets/Sources.meta" "$PROJ/Assets/Main.unity" "$PROJ/Assets/Main.unity.meta"
mkdir -p "$SRC"

vendor_runtime() {
  mkdir -p "$SRC/Abstractions" "$SRC/Runtime"
  cp "$CS/SqliteHost.Abstractions/"*.cs "$SRC/Abstractions/"
  cp "$CS/SqliteHost.Runtime/"*.cs "$SRC/Runtime/"
}

runner_bench() { cat > "$SRC/Runner.cs" <<'R'
using UnityEngine;
public sealed class Runner : MonoBehaviour
{
    void Start() { Debug.Log(BenchEntry.Run(7)); }
}
R
}
runner_game() { cat > "$SRC/Runner.cs" <<'R'
using UnityEngine;
public sealed class Runner : MonoBehaviour
{
    void Start() { Debug.Log(DummyGame.GameWork.RunAll(7)); }
}
R
}
runner_probe() { cat > "$SRC/Runner.cs" <<'R'
using UnityEngine;
public sealed class Runner : MonoBehaviour
{
    void Start() { Debug.Log(Program.Run()); }
}
R
}

vendor_probe() { # $1 = gvm|nogvm — identical transform for both (drop Main, expose Run)
  sed -e 's/static class Program/public static class Program/' \
      -e 's/static void Main()/public static int Run()/' \
      -e 's/Console.WriteLine(n);/return n;/' \
      "$BENCH/probes/$1/Program.cs" > "$SRC/Program.cs"
}

case "$ROW" in
  0) cp "$BENCH/out/unity-src/classic/GameWork.cs" "$SRC/"; runner_game ;;
  1) vendor_runtime; cp "$BENCH/out/unity-src/classic/"*.cs "$SRC/"; runner_bench ;;
  2) vendor_runtime; cp "$BENCH/out/unity-src/compact/"*.cs "$SRC/"; runner_bench ;;
  3) vendor_runtime; cp "$BENCH/out/unity-src/compact/"*.cs "$SRC/"; runner_bench ;;
  4) vendor_runtime; cp "$BENCH/out/unity-src/ultra/"*.cs "$SRC/"; runner_bench ;;
  5) vendor_runtime; cp "$BENCH/out/unity-src/ultra/"*.cs "$SRC/"; runner_bench ;;
  6) vendor_runtime; cp "$BENCH/out/unity-src/compact/"*.cs "$SRC/"
     cp "$BENCH/out/gen/compact-fields/HostMethodDtos.g.cs" "$SRC/HostMethodDtos.g.cs"; runner_bench ;;
  7) vendor_probe gvm; runner_probe ;;
  8) vendor_probe nogvm; runner_probe ;;
  9) vendor_runtime; cp "$BENCH/out/unity-src/classic-5/"*.cs "$SRC/"; runner_bench ;;
  10) vendor_runtime; cp "$BENCH/out/unity-src/compact-5/"*.cs "$SRC/"; runner_bench ;;
  11) vendor_runtime; cp "$BENCH/out/unity-src/ultra-5/"*.cs "$SRC/"; runner_bench ;;
  *) echo "unknown row $ROW (0..11)" >&2; exit 2 ;;
esac

echo "row $ROW prepared: $(find "$SRC" -name '*.cs' | wc -l | tr -d ' ') sources in $SRC"
