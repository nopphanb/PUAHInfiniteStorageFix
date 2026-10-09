using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace PUAHInfiniteStorageFix
{
    // 1. Vanilla haul search picks an Infinite Storage that would refuse the item
    //    (e.g. unpowered); every haul to it fails at once and retries forever.
    //    Such storage is skipped by the search (needs only KV Infinite Storage).
    // 2. Pick Up And Haul plans multi-item trips by counting free stack space in
    //    the target cell; Infinite Storage keeps items virtually, so the plan is
    //    nonsense and HaulToInventory restarts forever. Items bound for an Infinite
    //    Storage go to vanilla's one-stack haul job, and it is never an extra stop.
    // 3. Pick Up And Haul reserves with errors always on, so a queued trip that lost
    //    an item to another hauler (e.g. a mech lifter) logs "Could not reserve";
    //    it now honours vanilla's errorOnFailed and fails quietly.
    // Both mods are looked up by name; patches apply only to what is loaded.
    // The decisions, kept free of game startup code so they can be tested.
    public static class HaulDiversion
    {
        public static bool IsInfiniteStorage(object destination, Type infiniteType) =>
            destination != null && infiniteType != null && infiniteType.IsInstanceOfType(destination);

        // An Infinite Storage that would refuse the item (unpowered, adds disabled,
        // filter) must not be offered as a haul destination.
        public static bool ShouldSkip(bool isInfiniteStorage, bool accepts) => isInfiniteStorage && !accepts;
    }

    [StaticConstructorOnStartup]
    public static class PUAHInfiniteStorageFix
    {
        public const string WorkGiverTypeName = "PickUpAndHaul.WorkGiver_HaulToInventory";
        public const string InfiniteStorageTypeName = "InfiniteStorage.Building_InfiniteStorage";
        public const string JobDriverTypeName = "PickUpAndHaul.JobDriver_HaulToInventory";
        private const string LogPrefix = "[PUAH × Infinite Storage Fix] ";
        private const int MapCheckInterval = 250;

        private static Type infiniteStorage;
        private static readonly Dictionary<int, KeyValuePair<int, bool>> mapHasStorage = new Dictionary<int, KeyValuePair<int, bool>>();

        static PUAHInfiniteStorageFix()
        {
            var harmony = new Harmony("nopphan.puahinfinitestoragefix");
            infiniteStorage = AccessTools.TypeByName(InfiniteStorageTypeName);
            Type workGiver = AccessTools.TypeByName(WorkGiverTypeName);
            Type jobDriver = AccessTools.TypeByName(JobDriverTypeName);
            var fixes = new List<string>();
            if (infiniteStorage != null)
            {
                // Vanilla's storage-cell search only checks the storage's item filter,
                // but haul jobs then check Accepts, where Infinite Storage's power check
                // lives. An unpowered storage was picked, every haul to it failed at
                // once, and haulers retried forever ("started 10 jobs in one tick").
                harmony.Patch(AccessTools.Method(typeof(StoreUtility), "TryFindBestBetterStoreCellForWorker"),
                    prefix: new HarmonyMethod(typeof(PUAHInfiniteStorageFix), nameof(StoreCellSearchPrefix)));
                fixes.Add("haulers skip Infinite Storage that would refuse an item");
            }
            if (infiniteStorage != null && workGiver != null)
            {
                harmony.Patch(AccessTools.DeclaredMethod(workGiver, "JobOnThing"),
                    prefix: new HarmonyMethod(typeof(PUAHInfiniteStorageFix), nameof(JobOnThingPrefix)));
                harmony.Patch(AccessTools.DeclaredMethod(workGiver, "TryFindBestBetterStorageFor"),
                    postfix: new HarmonyMethod(typeof(PUAHInfiniteStorageFix), nameof(ExtraStopPostfix)));
                fixes.Add("Pick Up And Haul leaves Infinite Storage to vanilla hauling");
            }
            if (jobDriver != null)
            {
                // Pick Up And Haul reserves with errorOnFailed always on, so a queued
                // trip that lost an item to another hauler logs "Could not reserve"
                // instead of failing quietly as vanilla asks for queued jobs.
                harmony.Patch(AccessTools.DeclaredMethod(jobDriver, "TryMakePreToilReservations"),
                    prefix: new HarmonyMethod(typeof(PUAHInfiniteStorageFix), nameof(QuietReservationsPrefix)));
                fixes.Add("Pick Up And Haul's queued trips fail quietly when another hauler took the item");
            }
            if (fixes.Count == 0)
                Log.Warning(LogPrefix + "neither Pick Up And Haul nor KV Infinite Storage is loaded; doing nothing.");
            else
                Log.Message(LogPrefix + string.Join("; ", fixes) + ".");
        }

        // Pick Up And Haul's TryMakePreToilReservations, but honouring errorOnFailed
        // (false for queued jobs, whose plan may have gone stale). Same reservations
        // as the original; an empty queue fails instead of throwing.
        public static bool QuietReservationsPrefix(JobDriver __instance, bool errorOnFailed, ref bool __result)
        {
            Pawn pawn = __instance.pawn;
            Job job = __instance.job;
            if (pawn == null || job?.targetQueueA == null || job.targetQueueA.Count == 0)
            {
                __result = false;
                return false;
            }
            pawn.ReserveAsManyAsPossible(job.targetQueueA, job);
            if (job.targetQueueB != null)
                pawn.ReserveAsManyAsPossible(job.targetQueueB, job);
            __result = pawn.Reserve(job.targetQueueA[0], job, errorOnFailed: errorOnFailed)
                && pawn.Reserve(job.targetB, job, errorOnFailed: errorOnFailed);
            return false;
        }

        private static bool Refuses(object member, Thing t) =>
            HaulDiversion.ShouldSkip(HaulDiversion.IsInfiniteStorage(member, infiniteStorage),
                member is Building_Storage storage && storage.Accepts(t));

        // Skip an Infinite Storage (or a linked group made only of them) that would
        // refuse the item, matching the Accepts check the haul job makes later.
        public static bool StoreCellSearchPrefix(Thing t, ISlotGroup slotGroup)
        {
            if (t == null || slotGroup == null)
                return true;
            if (slotGroup is SlotGroup group)
                return !Refuses(group.parent, t);
            if (slotGroup is StorageGroup linked && linked.members.Count > 0)
                return !linked.members.All(m => Refuses(m, t));
            return true;
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
