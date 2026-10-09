# Pick Up And Haul × Infinite Storage Fix

A tiny RimWorld 1.6 compatibility mod for **Pick Up And Haul** (Mehni.PickUpAndHaul) and **KV Infinite Storage**
(Mlie.KVInfiniteStorage).

## The problem
Pick Up And Haul plans multi-item trips by counting free stack space in the target storage cell
(`WorkGiver_HaulToInventory.CapacityAt` → `GetItemStackSpaceLeftFor`). Infinite Storage keeps items virtually and
answers "yes" to every reservation on its cells. So the plan is nonsense, the `HaulToInventory` job fails and
restarts forever ("Invalid count: -1, setting to 1. Job was HaulToInventory …"), and deep chains of instantly
failing jobs can overflow the stack and crash the game.

## The fix
Two Harmony patches, applied only when both mods are loaded (both are looked up by type name):

| Patch | Effect |
|---|---|
| Prefix on `WorkGiver_HaulToInventory.JobOnThing` | If the item's best storage (vanilla `StoreUtility.TryFindBestBetterStorageFor`) is an Infinite Storage, return vanilla's `HaulAIUtility.HaulToStorageJob`, the one-stack haul Infinite Storage is built for. |
| Postfix on `WorkGiver_HaulToInventory.TryFindBestBetterStorageFor` | For extra items on a multi-item trip, an Infinite Storage counts as "no storage found", so it's never added as a stop. |

Everything else is unchanged: Pick Up And Haul still makes multi-item trips to every normal stockpile and shelf.
Maps without an Infinite Storage skip the extra check (cached every 250 ticks).

## Install
Needs Harmony, Pick Up And Haul and KV Infinite Storage. Load it **after** both (About.xml declares `loadAfter`).

## Development
`bash Tests/run-tests.sh` builds the DLL and runs 27 checks against the real installed Pick Up And Haul and
KV Infinite Storage assemblies:
- both patch targets exist, and every patch parameter matches a target parameter by name and type (Harmony binds
  by name);
- the vanilla calls the fix makes exist;
- only Infinite Storage is diverted, while normal storage keeps multi-item trips;
- the load order is right, and it doesn't ship its own Harmony.

## License
MIT, see [LICENSE](LICENSE).
