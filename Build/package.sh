#!/usr/bin/env bash
# Builds the mod and assembles a Thunderstore-ready zip in Build/dist.
#
#   bash Build/package.sh
#
# The zip layout is what Thunderstore expects: manifest.json, icon.png and README.md at the
# root, plugin files under plugins/KrisMod/.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
BUILD="$ROOT/Build"
STAGE="$BUILD/stage"
DIST="$BUILD/dist"

VERSION="$(python - "$BUILD/manifest.json" <<'PY'
import json, sys
print(json.load(open(sys.argv[1], encoding="utf-8"))["version_number"])
PY
)"

echo "==> building KrisMod $VERSION"
dotnet build "$ROOT/KrisMod/KrisMod.csproj" -c Release -p:DeployOnBuild=false --nologo -v q

DLL="$ROOT/KrisMod/bin/Release/KrisMod.dll"
[ -f "$DLL" ] || { echo "!! $DLL not found"; exit 1; }

echo "==> staging"
rm -rf "$STAGE"
mkdir -p "$STAGE/plugins/KrisMod"
cp "$DLL" "$STAGE/plugins/KrisMod/"
cp "$BUILD/manifest.json" "$STAGE/"
cp "$BUILD/icon.png" "$STAGE/"
# The player-facing page, not the repository README.
cp "$BUILD/README.md" "$STAGE/"

# Voice clips and images ship as loose files the plugin reads at startup.
SOUNDS="$ROOT/KrisMod/Assets/Sounds"
if [ -d "$SOUNDS" ]; then
  mkdir -p "$STAGE/plugins/KrisMod/Assets/Sounds"
  cp -r "$SOUNDS/." "$STAGE/plugins/KrisMod/Assets/Sounds/"
  echo "    included $(find "$SOUNDS" -name '*.wav' | wc -l) voice clips"
fi

for folder in Icons Hud; do
  SRC="$ROOT/KrisMod/Assets/$folder"
  if [ -d "$SRC" ]; then
    mkdir -p "$STAGE/plugins/KrisMod/Assets/$folder"
    cp -r "$SRC/." "$STAGE/plugins/KrisMod/Assets/$folder/"
    echo "    included $(find "$SRC" -type f | wc -l) file(s) from $folder"
  fi
done

# The model and its animations. Without it Kris is just Loader, so there is nothing to ship.
BUNDLE="$ROOT/KrisUnityProject/AssetBundles/kris"
[ -f "$BUNDLE" ] || { echo "!! $BUNDLE not found - run Kris > Set Up Kris (KrisSetup.Run) first"; exit 1; }
mkdir -p "$STAGE/plugins/KrisMod/Assets"
cp "$BUNDLE" "$STAGE/plugins/KrisMod/Assets/"
echo "    included asset bundle"

# The ACT menu's DELTARUNE font, a bundle of its own (KrisSetup.BuildFont). The plugin falls back
# to the HUD font without it, so a zip that forgets it runs fine and just looks wrong - 1.0.0
# shipped that way.
FONT="$ROOT/KrisUnityProject/AssetBundles/Font/krisfont"
[ -f "$FONT" ] || { echo "!! $FONT not found - run KrisSetup.BuildFont first"; exit 1; }
cp "$FONT" "$STAGE/plugins/KrisMod/Assets/"
echo "    included font bundle"

mkdir -p "$DIST"
ZIP="$DIST/KrisMod-$VERSION.zip"
rm -f "$ZIP"

# Built with Python rather than zip(1): Git Bash on Windows ships no zip, and PowerShell's
# Compress-Archive writes backslash separators, which Thunderstore rejects.
python - "$STAGE" "$ZIP" <<'PY'
import os, sys, zipfile

stage, out = sys.argv[1], sys.argv[2]
with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as zf:
    for root, _, files in os.walk(stage):
        for name in files:
            full = os.path.join(root, name)
            arc = os.path.relpath(full, stage).replace(os.sep, "/")
            zf.write(full, arc)
PY

echo "==> $ZIP"
