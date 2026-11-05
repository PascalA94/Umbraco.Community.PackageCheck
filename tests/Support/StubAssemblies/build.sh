#!/usr/bin/env bash
# Rebuilds the checked-in stub DLLs. Run from anywhere; needs a .NET SDK.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
out="$(mktemp -d)"
trap 'rm -rf "$out"' EXIT
for p in ManifestReaderStub ManagementApiStub PackageManifestOnlyStub PlainStub; do
  dotnet build "$here/src/$p/$p.csproj" -c Release -o "$out/$p" -v q -nologo
  cp "$out/$p/$p.dll" "$here/"
done
rm -rf "$here"/src/*/bin "$here"/src/*/obj
