#!/bin/bash
set -euo pipefail

source_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
game_path="${1:-${RIMWORLD_APP:-$HOME/Library/Application Support/Steam/steamapps/common/RimWorld/RimWorldMac.app}}"
if [[ -f "$game_path/Assembly-CSharp.dll" ]]; then
  managed_dir="$game_path"
elif [[ -d "$game_path/RimWorldMac.app" ]]; then
  managed_dir="$game_path/RimWorldMac.app/Contents/Resources/Data/Managed"
else
  managed_dir="$game_path/Contents/Resources/Data/Managed"
fi

if [[ ! -f "$managed_dir/Assembly-CSharp.dll" ]]; then
  echo "Cannot find RimWorld's Assembly-CSharp.dll in: $managed_dir" >&2
  echo 'Usage: bash build.sh "/path/to/RimWorldMac.app"' >&2
  exit 1
fi
dotnet_cmd="${DOTNET_CMD:-dotnet}"
if ! command -v "$dotnet_cmd" >/dev/null 2>&1; then
  echo 'Install the .NET 8 SDK from https://dotnet.microsoft.com/en-us/download/dotnet/8.0 and reopen Terminal.' >&2
  exit 1
fi
export DOTNET_CLI_TELEMETRY_OPTOUT=1
"$dotnet_cmd" build "$source_dir/PUAHInfiniteStorageFix.csproj" -c Release "-p:GameManagedDir=$managed_dir" --nologo
echo "Ready: $source_dir/../Assemblies/PUAHInfiniteStorageFix.dll"
