#!/usr/bin/env bash
# NuGet pack shape check for the five publishable C# projects.
#
# release.yml packs these on a version tag and pushes them. Until this
# script existed, a tag run was the FIRST time `dotnet pack` ever touched
# them, so a missing licence expression or an empty lib/ folder surfaced
# with the release already half-cut. Packing needs no API key, so it can
# run on a pull request instead.
#
# Asserts, per package: the .nupkg exists at the version version.txt
# names; it carries the assembly under lib/netstandard2.0/; and the
# .nuspec has the metadata nuget.org requires (id, version, description,
# licence expression, repository URL). A missing readme is reported as a
# warning, not a failure — none of these projects sets PackageReadmeFile
# yet (docs/guides/publishing.md §c).
#
# Usage: bash scripts/check-nupkg-shape.sh   (needs the .NET SDK and unzip)
set -u -o pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="${NUPKG_OUT:-$ROOT/nupkgs}"
VERSION="$(cat "$ROOT/version.txt")"

# Same list, same order as release.yml's pack step. The three
# SqliteHost.Generated.Sample* projects and SqliteHost.Tests never publish.
IDS=(SqliteHost.Abstractions SqliteHost.Runtime SqliteHost.Conformance
     SqliteHost.Adapters.Native SqliteHost.Delivery)

rm -rf "$OUT"
failures=0
fail() { echo "  FAIL: $*" >&2; failures=$((failures + 1)); }

for id in "${IDS[@]}"; do
  echo "=== $id ==="
  if ! dotnet pack "$ROOT/csharp/$id/$id.csproj" -c Release -o "$OUT" --nologo -v q; then
    fail "$id did not pack"
    continue
  fi

  nupkg="$OUT/$id.$VERSION.nupkg"
  if [ ! -f "$nupkg" ]; then
    fail "expected $nupkg — pack produced a different version than version.txt ($VERSION)"
    continue
  fi

  listing="$(unzip -Z1 "$nupkg")" || { fail "$id: unreadable .nupkg"; continue; }
  case "$listing" in
    *"lib/netstandard2.0/$id.dll"*) ;;
    *) fail "$id: lib/netstandard2.0/$id.dll is not in the package" ;;
  esac
  case "$listing" in
    *README.md*) ;;
    *) echo "::warning::$id: no README.md in the package — nuget.org will show an empty page" ;;
  esac

  nuspec="$(unzip -p "$nupkg" "$id.nuspec")" || { fail "$id: no .nuspec"; continue; }
  for required in "<id>$id</id>" "<version>$VERSION</version>" "<description>" \
                  "<license type=\"expression\">MIT</license>" "<repository "; do
    case "$nuspec" in
      *"$required"*) ;;
      *) fail "$id: .nuspec is missing $required" ;;
    esac
  done
  echo "  ok — lib/netstandard2.0/$id.dll, licence + repository metadata present"
done

if [ "$failures" -ne 0 ]; then
  echo "NuGet pack shape check FAILED ($failures problem(s))." >&2
  exit 1
fi
echo "All ${#IDS[@]} packages pack at $VERSION with the metadata nuget.org requires."
