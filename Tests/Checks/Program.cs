using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Xml.Linq;

// args: <RimWorld Managed dir> <mod root> <Pick Up And Haul 1.6 dll> <KV Infinite Storage 1.6 dll> <Harmony dll>
string managed = args[0], modRoot = args[1];
var extraDirs = new[] { managed, Path.GetDirectoryName(args[2])!, Path.GetDirectoryName(args[3])!, Path.GetDirectoryName(args[4])!, Path.Combine(modRoot, "Assemblies") };
AssemblyLoadContext.Default.Resolving += (ctx, name) => {
    foreach (var d in extraDirs) { var p = Path.Combine(d, name.Name + ".dll"); if (File.Exists(p)) return ctx.LoadFromAssemblyPath(p); }
    return null;
};
Assembly game = Assembly.LoadFrom(Path.Combine(managed, "Assembly-CSharp.dll"));
Assembly puah = Assembly.LoadFrom(args[2]);
Assembly kv = Assembly.LoadFrom(args[3]);
Assembly.LoadFrom(args[4]);
Assembly fix = Assembly.LoadFrom(Path.Combine(modRoot, "Assemblies/PUAHInfiniteStorageFix.dll"));
int checks = 0;
void Check(bool ok, string msg) { if (!ok) throw new Exception(msg); checks++; Console.WriteLine("PASS " + msg); }
Type GameType(string n) => game.GetType("Verse." + n) ?? game.GetType("RimWorld." + n) ?? game.GetType("Verse.AI." + n) ?? throw new Exception(n);

Type fixType = fix.GetType("PUAHInfiniteStorageFix.PUAHInfiniteStorageFix", true)!;
string wgName = (string)fixType.GetField("WorkGiverTypeName")!.GetRawConstantValue()!;
string isName = (string)fixType.GetField("InfiniteStorageTypeName")!.GetRawConstantValue()!;
Type workGiver = puah.GetType(wgName); Type infinite = kv.GetType(isName);
Check(workGiver != null, "Pick Up And Haul type the fix looks up exists: " + wgName);
Check(infinite != null && infinite.IsSubclassOf(GameType("Building_Storage")), "KV Infinite Storage type the fix looks up exists and is a vanilla storage building: " + isName);

// Harmony binds patch parameters by name, so every one must exist on the target.
void ParamsMatch(MethodInfo target, MethodInfo patch, string what) {
    var names = target.GetParameters().Select(p => p.Name).ToHashSet();
    foreach (var p in patch.GetParameters())
        Check(p.Name == "__result" || names.Contains(p.Name), what + ": patch parameter '" + p.Name + "' exists on the target");
    var res = patch.GetParameters().SingleOrDefault(p => p.Name == "__result");
    if (res != null) Check(res.ParameterType.GetElementType() == target.ReturnType, what + ": __result type matches the target's return type " + target.ReturnType.Name);
}
var jobOnThing = workGiver!.GetMethod("JobOnThing", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)!;
Check(jobOnThing != null && jobOnThing.GetBaseDefinition().DeclaringType == GameType("WorkGiver_Scanner"), "Target 1: Pick Up And Haul's own JobOnThing override (the trip planner)");
ParamsMatch(jobOnThing, fixType.GetMethod("JobOnThingPrefix")!, "JobOnThing prefix");
Check(fixType.GetMethod("JobOnThingPrefix")!.ReturnType == typeof(bool), "JobOnThing prefix can skip Pick Up And Haul's planner");

var extra = workGiver.GetMethod("TryFindBestBetterStorageFor", BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)!;
Check(extra != null && extra.ReturnType == typeof(bool), "Target 2: Pick Up And Haul's own storage search for extra items");
ParamsMatch(extra, fixType.GetMethod("ExtraStopPostfix")!, "Extra-stop postfix");
foreach (var p in fixType.GetMethod("ExtraStopPostfix")!.GetParameters().Where(p => p.Name != "map" && p.Name != "__result"))
    Check(extra.GetParameters().Single(t => t.Name == p.Name).IsOut && p.ParameterType.IsByRef, "Extra-stop postfix rewrites the out parameter " + p.Name);

// The vanilla calls the fix makes exist with these signatures.
var store = GameType("StoreUtility");
var vanillaSearch = store.GetMethods().Single(m => m.Name == "TryFindBestBetterStorageFor");
var vp = vanillaSearch.GetParameters();
Check(vp.Take(7).Select(x => x.Name).SequenceEqual(new[] { "t", "carrier", "map", "currentPriority", "faction", "foundCell", "haulDestination" }) && vp[5].IsOut && vp[6].IsOut && vp.Skip(7).All(x => x.IsOptional),
    "Vanilla StoreUtility.TryFindBestBetterStorageFor(t, carrier, map, priority, faction, out cell, out destination[, optional]) exists (the same search Pick Up And Haul starts with)");
var haul = GameType("HaulAIUtility");
Check(haul.GetMethod("HaulToStorageJob", new[] { GameType("Pawn"), GameType("Thing"), typeof(bool) }) != null, "Vanilla HaulAIUtility.HaulToStorageJob(pawn, thing, forced) exists");
Check(haul.GetMethod("PawnCanAutomaticallyHaulFast", new[] { GameType("Pawn"), GameType("Thing"), typeof(bool) }) != null, "Vanilla HaulAIUtility.PawnCanAutomaticallyHaulFast exists");

// The decision: only Infinite Storage is diverted to vanilla hauling.
var isInf = fix.GetType("PUAHInfiniteStorageFix.HaulDiversion", true)!.GetMethod("IsInfiniteStorage")!;
bool Is(object? o) => (bool)isInf.Invoke(null, new[] { o, infinite })!;
Check(Is(RuntimeHelpers.GetUninitializedObject(infinite!)), "An Infinite Storage is diverted to vanilla hauling");
Check(!Is(RuntimeHelpers.GetUninitializedObject(GameType("Building_Storage"))) && !Is(null), "Normal storage buildings (and no destination) keep Pick Up And Haul's multi-item trips");

// 1.1: vanilla storage-cell search skips an Infinite Storage that would refuse the item.
var worker = store.GetMethod("TryFindBestBetterStoreCellForWorker", BindingFlags.NonPublic | BindingFlags.Static)!;
Check(worker != null && worker.GetParameters().Any(x => x.Name == "slotGroup" && x.ParameterType == GameType("ISlotGroup")), "Target 3: vanilla StoreUtility.TryFindBestBetterStoreCellForWorker(…, ISlotGroup slotGroup, …) exists");
ParamsMatch(worker, fixType.GetMethod("StoreCellSearchPrefix")!, "Store-cell search prefix");
Check(fixType.GetMethod("StoreCellSearchPrefix")!.ReturnType == typeof(bool), "Store-cell search prefix can skip a storage group");
Check(GameType("Building_Storage").GetMethod("Accepts", new[] { GameType("Thing") }) != null && GameType("SlotGroup").GetField("parent") != null && GameType("StorageGroup").GetField("members") != null, "Vanilla Building_Storage.Accepts, SlotGroup.parent and StorageGroup.members exist");
Check(kv.GetTypes().Any(t => t.Name == "Building_Storage_Accepts"), "KV Infinite Storage puts its power check in Building_Storage.Accepts (its Building_Storage_Accepts patch), which the fix calls");
var skip = fix.GetType("PUAHInfiniteStorageFix.HaulDiversion", true)!.GetMethod("ShouldSkip")!;
bool Skip(bool inf, bool acc) => (bool)skip.Invoke(null, new object[] { inf, acc })!;
Check(Skip(true, false) && !Skip(true, true) && !Skip(false, false) && !Skip(false, true), "Only an Infinite Storage that would refuse the item is skipped; working ones and normal storage are still found");

// Mod metadata: loads after both mods and Harmony; doesn't ship its own Harmony.
var about = XDocument.Load(Path.Combine(modRoot, "About/About.xml")).Root!;
var after = about.Element("loadAfter")!.Elements("li").Select(e => e.Value).ToList();
Check(after.Contains("Mehni.PickUpAndHaul") && after.Contains("Mlie.KVInfiniteStorage") && after.Contains("brrainz.harmony"), "About.xml loads after Pick Up And Haul, KV Infinite Storage and Harmony");
Check(about.Element("supportedVersions")!.Elements("li").Any(e => e.Value == "1.6") && about.Descendants("packageId").First().Value == "nopphan.puahinfinitestoragefix", "About.xml: RimWorld 1.6, own packageId");
Check(!File.Exists(Path.Combine(modRoot, "Assemblies/0Harmony.dll")), "Doesn't ship a second copy of Harmony (uses the Harmony mod)");
Console.WriteLine($"PUAH x INFINITE STORAGE FIX 1.1: {checks} checks passed.");
