#!/usr/bin/env bash
# Repopulates libs/ from a local Risk of Rain 2 + BepInEx install.
#
#   bash Build/sync-libs.sh ["C:/path/to/Risk of Rain 2"]
#
# libs/ is gitignored on purpose: those assemblies belong to the game and to the R2API
# packages, and are not ours to redistribute. Run this after a fresh clone, or after the
# game updates, so the project compiles against the version you actually run.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
GAME="${1:-C:/Program Files (x86)/Steam/steamapps/common/Risk of Rain 2}"
MANAGED="$GAME/Risk of Rain 2_Data/Managed"
BEP="$GAME/BepInEx"

[ -d "$MANAGED" ] || { echo "!! Managed folder not found at $MANAGED"; exit 1; }
[ -d "$BEP/core" ] || { echo "!! BepInEx not installed at $BEP - install BepInExPack first"; exit 1; }

mkdir -p "$ROOT/libs/game" "$ROOT/libs/bepinex" "$ROOT/libs/r2api"
rm -f "$ROOT/libs/game"/*.dll "$ROOT/libs/bepinex"/*.dll "$ROOT/libs/r2api"/*.dll

GAME_DLLS=(
  Assembly-CSharp.dll RoR2.dll LegacyResourcesAPI.dll
  HGCSharpUtils.dll HGUnityUtils.dll KinematicCharacterController.dll Rewired_Core.dll
  Unity.Addressables.dll Unity.ResourceManager.dll Unity.TextMeshPro.dll
  Unity.Postprocessing.Runtime.dll Unity.RenderPipelines.Core.Runtime.dll
  com.unity.multiplayer-hlapi.Runtime.dll Zio.dll
)
for dll in "${GAME_DLLS[@]}"; do
  if [ -f "$MANAGED/$dll" ]; then cp "$MANAGED/$dll" "$ROOT/libs/game/"; else echo "   missing: $dll"; fi
done
cp "$MANAGED"/UnityEngine*.dll "$ROOT/libs/game/"
# netstandard.dll would collide with the SDK's reference assemblies.
rm -f "$ROOT/libs/game/netstandard.dll"

for dll in BepInEx.dll 0Harmony.dll BepInEx.Harmony.dll MonoMod.RuntimeDetour.dll MonoMod.Utils.dll Mono.Cecil.dll; do
  [ -f "$BEP/core/$dll" ] && cp "$BEP/core/$dll" "$ROOT/libs/bepinex/"
done

find "$BEP/plugins" -name "R2API.*.dll" -exec cp {} "$ROOT/libs/r2api/" \;
# LoadoutAPI is obsolete and pulls in R2API_Skins, which this mod does not use.
rm -f "$ROOT/libs/r2api/R2API.Loadout.dll"

echo "game:    $(ls "$ROOT/libs/game" | wc -l) assemblies"
echo "bepinex: $(ls "$ROOT/libs/bepinex" | wc -l) assemblies"
echo "r2api:   $(ls "$ROOT/libs/r2api" | wc -l) assemblies"
