#!/bin/bash
# Builds the fix, then checks it against the real Pick Up And Haul and KV Infinite Storage assemblies.
set -euo pipefail
root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
app="${RIMWORLD_APP:-$HOME/Library/Application Support/Steam/steamapps/common/RimWorld/RimWorldMac.app}"
managed="$app/Contents/Resources/Data/Managed"
workshop="${WORKSHOP:-$HOME/Library/Application Support/Steam/steamapps/workshop/content/294100}"
puah="$workshop/1279012058/1.6/Assemblies/PickUpAndHaul.dll"
kv="$workshop/2932669764/1.6/Assemblies/InfiniteStorage.dll"
harmony="$(find "$workshop/2009463077" -name 0Harmony.dll | head -1)"
dotnet_cmd="${DOTNET_CMD:-dotnet}"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
bash "$root/Source/build.sh" "$app"
"$dotnet_cmd" run --project "$root/Tests/Checks" -- "$managed" "$root" "$puah" "$kv" "$harmony" | tail -n 4
echo "All checks passed."
