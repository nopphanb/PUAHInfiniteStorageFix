# Pick Up And Haul × Infinite Storage Fix

A tiny RimWorld 1.6 compatibility mod for **Pick Up And Haul** (Mehni.PickUpAndHaul) and **KV Infinite Storage**
(Mlie.KVInfiniteStorage).

## The problem
Pick Up And Haul plans multi-item trips by counting free stack space in the target storage cell
(`WorkGiver_HaulToInventory.CapacityAt` → `GetItemStackSpaceLeftFor`). Infinite Storage keeps items virtually and
answers "yes" to every reservation on its cells. So the plan is nonsense, the `HaulToInventory` job fails and
restarts forever ("Invalid count: -1, setting to 1. Job was HaulToInventory …"), and deep chains of instantly
failing jobs can overflow the stack and crash the game.

## Also: unpowered Infinite Storage loop (1.1)
Vanilla's storage-cell search (`StoreUtility.TryFindBestBetterStoreCellForWorker`) only checks the storage's item
filter. Haul jobs then validate the destination with `Building_Storage.Accepts`, which is where KV Infinite Storage
checks power. An unpowered storage (solar at night, a blackout, a solar flare, or simply not wired) was chosen as
the destination, every haul to it failed the moment it started, and colonists retried forever
("X started 10 jobs in one tick. newJob=HaulToCell …"). This needs only KV Infinite Storage, not Pick Up And Haul.

## The fix
Harmony patches, applied only for what's loaded (both mods are looked up by type name):

| Patch | Effect |
|---|---|
| Prefix on vanilla `StoreUtility.TryFindBestBetterStoreCellForWorker` (needs only Infinite Storage) | Skips an Infinite Storage that would refuse the item (`Accepts` false, e.g. unpowered), or a linked group made only of such storages. |
| Prefix on `WorkGiver_HaulToInventory.JobOnThing` | If the item's best storage (vanilla `StoreUtility.TryFindBestBetterStorageFor`) is an Infinite Storage, return vanilla's `HaulAIUtility.HaulToStorageJob`, the one-stack haul Infinite Storage is built for. |
| Postfix on `WorkGiver_HaulToInventory.TryFindBestBetterStorageFor` | For extra items on a multi-item trip, an Infinite Storage counts as "no storage found", so it's never added as a stop. |

Everything else is unchanged: Pick Up And Haul still makes multi-item trips to every normal stockpile and shelf.
Maps without an Infinite Storage skip the extra check (cached every 250 ticks).

## Install
Needs Harmony, Pick Up And Haul and KV Infinite Storage. Load it **after** both (About.xml declares `loadAfter`).

## Development
`bash Tests/run-tests.sh` builds the DLL and runs all checks against the real installed Pick Up And Haul and
KV Infinite Storage assemblies:
- both patch targets exist, and every patch parameter matches a target parameter by name and type (Harmony binds
  by name);
- the vanilla calls the fix makes exist;
- only Infinite Storage is diverted, while normal storage keeps multi-item trips;
- the load order is right, and it doesn't ship its own Harmony.

## License
MIT, see [LICENSE](LICENSE).
