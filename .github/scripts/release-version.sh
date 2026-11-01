#!/usr/bin/env bash
# Turns a release version (SemVer, no leading "v") into the version of each artifact.
#
#   0.1.0                -> nuget 0.1.0                 extension 0.1.0       stable
#   0.1.0-preview.1      -> nuget 0.1.0-preview.1       extension 0.1.1       pre-release
#   0.2.0-rc.3           -> nuget 0.2.0-rc.3            extension 0.2.3       pre-release
#
# The VS Code Marketplace and the Visual Studio manifest only take numeric major.minor.patch, so a pre-release
# `X.Y.Z-label.N` becomes `X.Y.(Z*1000+N)`. Previews of one line therefore sort above the stable X.Y.Z of that
# line: after previews of 0.1.0, the first stable release must raise the minor (0.2.0) or the major.
# See docs/RELEASING.md.
#
# Usage: release-version.sh <version>     Prints key=value lines (appended to $GITHUB_OUTPUT when set).
set -euo pipefail

version="${1:?usage: release-version.sh <version>}"

re='^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-([0-9A-Za-z-]+)\.([0-9]+))?$'
if [[ ! "$version" =~ $re ]]; then
  echo "Version '$version' must be X.Y.Z or X.Y.Z-<label>.<N> (for example 0.1.0-preview.1)." >&2
  exit 1
fi

major="${BASH_REMATCH[1]}"
minor="${BASH_REMATCH[2]}"
patch="${BASH_REMATCH[3]}"
label="${BASH_REMATCH[5]}"
number="${BASH_REMATCH[6]}"

if [[ -n "$label" ]]; then
  prerelease=true
  extension="$major.$minor.$((10#$patch * 1000 + 10#$number))"
  if ((10#$number > 999)); then
    echo "Pre-release number $number is above 999." >&2
    exit 1
  fi
else
  prerelease=false
  extension="$version"
fi

out="nuget=$version
extension=$extension
prerelease=$prerelease"

echo "$out"
if [[ -n "${GITHUB_OUTPUT:-}" ]]; then
  echo "$out" >> "$GITHUB_OUTPUT"
fi
