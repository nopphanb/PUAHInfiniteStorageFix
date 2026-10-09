using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace PUAHInfiniteStorageFix
{
    // Pick Up And Haul plans multi-item trips by counting free stack space in the
    // target cell (CapacityAt -> GetItemStackSpaceLeftFor). KV Infinite Storage
    // keeps items virtually, so that plan is nonsense and the HaulToInventory job
    // fails and restarts forever. Items bound for an Infinite Storage are handed
    // to vanilla's one-stack haul job instead, and Infinite Storage is never added
    // as an extra stop. Both mods are looked up by name; nothing is patched unless
    // both are loaded.
    // The diversion decision, kept free of game startup code so it can be tested.
    public static class HaulDiversion
    {
        public static bool IsInfiniteStorage(object destination, Type infiniteType) =>
            destination != null && infiniteType != null && infiniteType.IsInstanceOfType(destination);
    }

    [StaticConstructorOnStartup]
    public static class PUAHInfiniteStorageFix
    {
        public const string WorkGiverTypeName = "PickUpAndHaul.WorkGiver_HaulToInventory";
        public const string InfiniteStorageTypeName = "InfiniteStorage.Building_InfiniteStorage";
        private const string LogPrefix = "[PUAH × Infinite Storage Fix] ";
        private const int MapCheckInterval = 250;

        private static Type infiniteStorage;
        private static readonly Dictionary<int, KeyValuePair<int, bool>> mapHasStorage = new Dictionary<int, KeyValuePair<int, bool>>();

        static PUAHInfiniteStorageFix()
        {
            Type workGiver = AccessTools.TypeByName(WorkGiverTypeName);
            infiniteStorage = AccessTools.TypeByName(InfiniteStorageTypeName);
            if (workGiver == null || infiniteStorage == null)
            {
                Log.Warning(LogPrefix + "needs both Pick Up And Haul and KV Infinite Storage; doing nothing.");
                return;
            }
            var harmony = new Harmony("nopphan.puahinfinitestoragefix");
            harmony.Patch(AccessTools.DeclaredMethod(workGiver, "JobOnThing"),
                prefix: new HarmonyMethod(typeof(PUAHInfiniteStorageFix), nameof(JobOnThingPrefix)));
            harmony.Patch(AccessTools.DeclaredMethod(workGiver, "TryFindBestBetterStorageFor"),
                postfix: new HarmonyMethod(typeof(PUAHInfiniteStorageFix), nameof(ExtraStopPostfix)));
            Log.Message(LogPrefix + "Pick Up And Haul now leaves Infinite Storage to vanilla hauling.");
        }

        private static bool IsInfiniteAt(Map map, IntVec3 cell, IHaulDestination destination)
        {
            if (HaulDiversion.IsInfiniteStorage(destination, infiniteStorage))
                return true;
            return map != null && cell.IsValid && cell.InBounds(map)
                && HaulDiversion.IsInfiniteStorage(cell.GetSlotGroup(map)?.parent, infiniteStorage);
        }

        // Skips the extra storage search on maps without any Infinite Storage.
        private static bool MapHasInfiniteStorage(Map map)
        {
            int now = Find.TickManager.TicksGame;
            if (mapHasStorage.TryGetValue(map.uniqueID, out KeyValuePair<int, bool> cached) && now - cached.Key < MapCheckInterval)
                return cached.Value;
            bool has = map.listerBuildings.allBuildingsColonist.Any(b => infiniteStorage.IsInstanceOfType(b));
            mapHasStorage[map.uniqueID] = new KeyValuePair<int, bool>(now, has);
            return has;
        }

        // First item of a trip: if its best storage is an Infinite Storage, use
        // vanilla's one-stack haul job instead of Pick Up And Haul's plan.
        public static bool JobOnThingPrefix(Pawn pawn, Thing thing, bool forced, ref Job __result)
        {
            Map map = pawn?.Map;
            if (map == null || thing == null || !thing.Spawned || !MapHasInfiniteStorage(map))
                return true;
            if (!StoreUtility.TryFindBestBetterStorageFor(thing, pawn, map, StoreUtility.CurrentStoragePriorityOf(thing),
                    pawn.Faction, out IntVec3 cell, out IHaulDestination destination))
                return true;
            if (!IsInfiniteAt(map, cell, destination))
                return true;
            __result = HaulAIUtility.PawnCanAutomaticallyHaulFast(pawn, thing, forced)
                ? HaulAIUtility.HaulToStorageJob(pawn, thing, forced)
                : null;
            return false;
        }

        // Extra items of a multi-item trip: an Infinite Storage counts as "no
        // storage found", so it is never added as a stop.
        public static void ExtraStopPostfix(Map map, ref bool __result, ref IntVec3 foundCell,
            ref IHaulDestination haulDestination, ref ThingOwner innerInteractableThingOwner)
        {
            if (!__result || !IsInfiniteAt(map, foundCell, haulDestination))
                return;
            __result = false;
            foundCell = IntVec3.Invalid;
            haulDestination = null;
            innerInteractableThingOwner = null;
        }
    }
}
