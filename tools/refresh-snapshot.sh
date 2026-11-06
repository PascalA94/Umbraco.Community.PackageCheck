#!/usr/bin/env bash
# Refreshes the overrides snapshot bundled in the tool (design 7.8) from the latest umbraco-package-compat release.
# Run before each tool release. Writes data/overrides.seed.json (the asset's overrides) and data/umbraco-versions.json
# (its embedded majors table), after checking the asset's sha256.
#   tools/refresh-snapshot.sh                              # latest release on GitHub
#   COMPAT_RELEASE=<base-url> tools/refresh-snapshot.sh    # another base, e.g. file:///path/to/compat/dist/
set -euo pipefail

base="${COMPAT_RELEASE:-https://github.com/PascalA94/umbraco-package-compat/releases/latest/download/}"
case "$base" in */) ;; *) base="$base/" ;; esac
repo="$(cd "$(dirname "$0")/.." && pwd)"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

curl -fsSL --proto '=https,file' --max-filesize 1048576 -o "$work/overrides.json" "${base}overrides.json"
curl -fsSL --proto '=https,file' --max-filesize 1024 -o "$work/overrides.json.sha256" "${base}overrides.json.sha256"
(cd "$work" && shasum -a 256 -c overrides.json.sha256)

python3 -I - "$work/overrides.json" "$repo/data" <<'PY'
import json, sys
src, data = sys.argv[1], sys.argv[2]
with open(src, encoding="utf-8") as f:
    asset = json.load(f)
if asset.get("schemaVersion") != 1 or not isinstance(asset.get("overrides"), list):
    sys.exit("not a schemaVersion 1 overrides asset")
versions = asset.get("umbracoVersions")
if not isinstance(versions, dict) or not versions.get("majors"):
    sys.exit("the asset has no umbracoVersions table")
with open(f"{data}/overrides.seed.json", "w", encoding="utf-8", newline="\n") as f:
    json.dump({"schemaVersion": 1, "overrides": asset["overrides"]}, f, indent=2, ensure_ascii=False)
    f.write("\n")
with open(f"{data}/umbraco-versions.json", "w", encoding="utf-8", newline="\n") as f:
    json.dump(versions, f, indent=2, ensure_ascii=False)
    f.write("\n")
print(f"snapshot: {len(asset['overrides'])} package(s), {len(versions['majors'])} Umbraco majors")
PY
