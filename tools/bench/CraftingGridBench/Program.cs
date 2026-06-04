using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Vintagestory.API.Common;
using Ehm93.VS.Performance.CraftingGrid.Internal;
using FastIndex = System.Collections.Generic.OrderedDictionary<
    Vintagestory.API.Common.IRecipeIngredientBase,
    System.Collections.Generic.List<Vintagestory.API.Common.IRecipeBase>>;

namespace CraftingGridBench;

/// <summary>
/// Stand-alone correctness + performance harness for the crafting-grid matcher, with NO Vintage Story
/// runtime: we fake the few engine seams the real types touch (IWorldAccessor / ICoreAPI / ILogger via
/// DispatchProxy), build real <see cref="Item"/>s and real <see cref="GridRecipe"/>s, and resolve them.
/// Resolving auto-populates <c>world.FastSearchRecipesByIngredient</c> exactly as the engine does, so we
/// can run BOTH the engine's candidate-gather (a faithful transcription of InventoryCraftingGrid.
/// FindMatchingRecipe's scan) and our <see cref="CraftingRecipeIndex.GatherCandidates"/> against the
/// SAME real ingredients, asserting they pick the same candidate set and timing the difference.
///
/// What this DOES cover: the candidate gather — where both the speedup and the only behavioural risk
/// live, using the real SatisfiesAsIngredient / WildcardUtil / the real index. What it does NOT cover:
/// the second pass (recipe.Matches), which needs a player; it is byte-identical in both code paths, so
/// candidate-set equality is the dispositive correctness property.
/// </summary>
internal static class Program
{
    private static string[] _probeDirs = Array.Empty<string>();

    private static int Main(string[] args)
    {
        // Register the resolver BEFORE any VS type is touched. Main references only BCL types; all VS
        // usage is in Run(), JIT-compiled (and thus type-resolved) only once it is actually called.
        string install =
            Environment.GetEnvironmentVariable("VINTAGE_STORY")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Vintagestory");
        _probeDirs = new[] { install, Path.Combine(install, "Lib") };

        if (!File.Exists(Path.Combine(install, "VintagestoryAPI.dll")))
        {
            Console.Error.WriteLine($"VintagestoryAPI.dll not found under '{install}'. Set VINTAGE_STORY.");
            return 2;
        }

        AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
        {
            string file = new AssemblyName(e.Name).Name + ".dll";
            foreach (string dir in _probeDirs)
            {
                string p = Path.Combine(dir, file);
                if (File.Exists(p)) return Assembly.LoadFrom(p);
            }
            return null;
        };

        return Run(args);
    }

    // ---- tunables (override with: bench [exactRecipes] [patternRecipes] [grids]) ------------------
    private static int Run(string[] args)
    {
        int exactRecipes = args.Length > 0 ? int.Parse(args[0]) : 1500;
        int patternRecipes = args.Length > 1 ? int.Parse(args[1]) : 40;
        int gridCount = args.Length > 2 ? int.Parse(args[2]) : 20000;

        var logger = DispatchProxy.Create<ILogger, NoOpProxy>();
        var api = DispatchProxy.Create<ICoreAPI, ApiProxy>();
        var world = DispatchProxy.Create<IWorldAccessor, WorldProxy>();

        var index = new FastIndex();
        var items = new Dictionary<string, Item>();

        ((WorldProxy)(object)world).Bind(index, items, logger);
        ((ApiProxy)(object)api).Bind(world, logger);

        var build = new Builder(world, api, items);

        // --- build a representative recipe set -----------------------------------------------------
        // Each exact recipe consumes one unique item; each pattern recipe consumes one wildcard family.
        var exactItems = new List<Item>();
        for (int i = 0; i < exactRecipes; i++)
        {
            Item ing = build.Item($"bench:item_{i}");
            exactItems.Add(ing);
            build.ExactRecipe($"ex_{i}", ing.Code.ToString(), build.Item($"bench:out_{i}").Code.ToString());
        }

        var patternItems = new List<Item>();
        for (int p = 0; p < patternRecipes; p++)
        {
            // family "bench:mat{p}-*" with a handful of concrete members the wildcard accepts
            foreach (char v in "abcde") patternItems.Add(build.Item($"bench:mat{p}-{v}"));
            build.PatternRecipe($"pat_{p}", $"bench:mat{p}-*", build.Item($"bench:patout_{p}").Code.ToString());
        }

        var noiseItems = new List<Item>();
        for (int n = 0; n < 200; n++) noiseItems.Add(build.Item($"bench:noise_{n}"));

        // The grid item pool, weighted toward real ingredients (a realistic crafting attempt).
        var pool = new List<Item>();
        pool.AddRange(exactItems);
        pool.AddRange(patternItems);
        pool.AddRange(patternItems); // weight wildcards a little heavier
        pool.AddRange(noiseItems);

        var fast = world.FastSearchRecipesByIngredient;
        int distinct = fast.Count;
        int patternKeys = 0;
        foreach (var kv in fast) if (kv.Key.MatchingType != EnumRecipeMatchType.Exact) patternKeys++;

        // Build the pre-expanded index inside a GC fence so we can measure its retained size. Build-time
        // transients (representative stacks, the accumulator sets) are freed by the trailing collection,
        // so the delta is just the kept index (dictionary + frozen arrays + recipe references).
        CraftingRecipeIndex.Invalidate();
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        long memBefore = GC.GetTotalMemory(true);
        CraftingRecipeIndex ourIndex = CraftingRecipeIndex.Get(world);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        long overhead = GC.GetTotalMemory(true) - memBefore;
        GC.KeepAlive(ourIndex);

        int keys = ourIndex.ByCode.Count;
        int entries = ourIndex.EntryCount;
        int totalRecipes = exactRecipes + patternRecipes;

        Console.WriteLine("=== crafting-grid matcher: isolated bench (no VS runtime) ===");
        Console.WriteLine($"recipes:            {exactRecipes} exact + {patternRecipes} pattern  (= {totalRecipes})");
        Console.WriteLine($"distinct ingredients in engine index: {distinct}  ({patternKeys} pattern / {distinct - patternKeys} exact)");
        Console.WriteLine($"collectibles swept: {world.Collectibles.Count}");
        Console.WriteLine($"pre-expanded index: {keys} code buckets, {entries} (code,recipe) entries");
        Console.WriteLine($"index memory:       {overhead / 1048576.0:F2} MB retained" +
                          $"  ({(entries > 0 ? overhead / (double)entries : 0):F0} B/entry, {overhead / (double)totalRecipes:F0} B/recipe)");
        Console.WriteLine($"grid item pool:     {pool.Count}");
        Console.WriteLine();

        // --- generate fixed random grids -----------------------------------------------------------
        var rng = new Random(1234);
        ItemSlot[][] grids = new ItemSlot[gridCount][];
        for (int g = 0; g < gridCount; g++) grids[g] = RandomGrid(rng, pool);

        // --- correctness: our candidate set must equal the engine's, for every grid ----------------
        int mismatches = 0, supersetOnly = 0;
        for (int g = 0; g < gridCount; g++)
        {
            var vanilla = new HashSet<GridRecipe>(VanillaGather(grids[g], fast));
            var ours = new HashSet<GridRecipe>(ourIndex.GatherCandidates(grids[g]));
            if (!ours.SetEquals(vanilla))
            {
                if (ours.IsSupersetOf(vanilla)) supersetOnly++;
                else { mismatches++; if (mismatches <= 3) Console.WriteLine($"  MISMATCH grid {g}: vanilla={vanilla.Count} ours={ours.Count} (ours not a superset!)"); }
            }
        }
        Console.WriteLine($"correctness:  {gridCount} grids — {mismatches} hard mismatches, {supersetOnly} superset-only (extra candidates the 2nd pass filters)");
        Console.WriteLine(mismatches == 0
            ? "              PASS — our gather always contains the engine's full candidate set."
            : "              FAIL — our gather dropped a candidate the engine would have found.");
        Console.WriteLine();

        // --- benchmark ----------------------------------------------------------------------------
        // warm up (JIT both paths)
        for (int g = 0; g < Math.Min(2000, gridCount); g++) { VanillaGather(grids[g], fast); ourIndex.GatherCandidates(grids[g]); }

        long vTicks = Time(() => { foreach (var grid in grids) VanillaGather(grid, fast); });
        long oTicks = Time(() => { foreach (var grid in grids) ourIndex.GatherCandidates(grid); });

        double vMs = vTicks * 1000.0 / Stopwatch.Frequency;
        double oMs = oTicks * 1000.0 / Stopwatch.Frequency;
        double vPer = vMs * 1_000_000.0 / gridCount; // ns per gather
        double oPer = oMs * 1_000_000.0 / gridCount;

        Console.WriteLine($"benchmark:    {gridCount} grids, one full FindMatchingRecipe candidate-gather each");
        Console.WriteLine($"  engine linear scan : {vMs,8:F1} ms total  |  {vPer,9:F0} ns/gather");
        Console.WriteLine($"  our pre-exp index  : {oMs,8:F1} ms total  |  {oPer,9:F0} ns/gather");
        Console.WriteLine($"  speedup            : {vMs / oMs,8:F1}x");
        return 0;
    }

    private static long Time(Action a)
    {
        var sw = Stopwatch.StartNew();
        a();
        sw.Stop();
        return sw.ElapsedTicks;
    }

    // A faithful transcription of the engine's candidate gather (InventoryCraftingGrid.FindMatchingRecipe):
    // for each occupied slot, scan EVERY distinct ingredient and collect the enabled grid recipes whose
    // ingredient it satisfies, deduplicated (AddIfNotPresent).
    private static List<GridRecipe> VanillaGather(ItemSlot[] slots, FastIndex fast)
    {
        var list = new List<GridRecipe>();
        foreach (ItemSlot slot in slots)
        {
            ItemStack? stack = slot?.Itemstack;
            if (stack == null || stack.StackSize == 0) continue;

            foreach (var kv in fast)
            {
                if (!kv.Key.SatisfiesAsIngredient(stack, checkStackSize: false)) continue;
                foreach (IRecipeBase r in kv.Value)
                    if (r is GridRecipe g && g.Enabled && !list.Contains(g)) list.Add(g);
            }
        }
        return list;
    }

    private static ItemSlot[] RandomGrid(Random rng, List<Item> pool)
    {
        var slots = new ItemSlot[9];
        for (int i = 0; i < 9; i++) slots[i] = new DummySlot();
        int occupied = 1 + rng.Next(9); // 1..9 items
        foreach (int idx in Enumerable.Range(0, 9).OrderBy(_ => rng.Next()).Take(occupied))
            slots[idx] = new DummySlot(new ItemStack(pool[rng.Next(pool.Count)], 1));
        return slots;
    }
}

/// <summary>Builds resolved items + grid recipes against the fake world (resolving fills the index).</summary>
internal sealed class Builder
{
    private static readonly FieldInfo ApiField =
        typeof(CollectibleObject).GetField("api", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("CollectibleObject.api field not found");

    private readonly IWorldAccessor _world;
    private readonly ICoreAPI _api;
    private readonly Dictionary<string, Item> _items;
    private int _nextId = 1;

    public Builder(IWorldAccessor world, ICoreAPI api, Dictionary<string, Item> items)
    {
        _world = world; _api = api; _items = items;
    }

    public Item Item(string code)
    {
        if (_items.TryGetValue(code, out var existing)) return existing;
        var it = new Item { ItemId = _nextId++, Code = new AssetLocation(code) };
        ApiField.SetValue(it, _api); // so CollectibleObject.Satisfies' `api.World` is non-null
        _items[code] = it;
        return it;
    }

    public void ExactRecipe(string name, string ingredientCode, string outputCode)
        => MakeRecipe(name, EnumItemClass.Item, ingredientCode, outputCode);

    public void PatternRecipe(string name, string wildcardCode, string outputCode)
        => MakeRecipe(name, EnumItemClass.Item, wildcardCode, outputCode);

    private void MakeRecipe(string name, EnumItemClass type, string ingredientCode, string outputCode)
    {
        var recipe = new GridRecipe
        {
            IngredientPattern = "I",
            Width = 1,
            Height = 1,
            Name = new AssetLocation("bench:" + name),
            Ingredients = new Dictionary<string, CraftingRecipeIngredient>
            {
                ["I"] = new CraftingRecipeIngredient { Type = type, Code = new AssetLocation(ingredientCode), Quantity = 1 },
            },
            Output = new CraftingRecipeIngredient { Type = type, Code = new AssetLocation(outputCode), Quantity = 1 },
        };
        if (!recipe.Resolve(_world, "bench"))
            throw new InvalidOperationException($"recipe '{name}' failed to resolve");
    }
}

// ---- minimal DispatchProxy fakes ----------------------------------------------------------------

/// <summary>Base proxy: every unhandled member is a no-op returning default(T).</summary>
public class NoOpProxy : DispatchProxy
{
    protected virtual object? Handle(MethodInfo m, object?[]? args) => Unhandled(m);

    protected static object? Unhandled(MethodInfo m)
    {
        Type rt = m.ReturnType;
        if (rt == typeof(void)) return null;
        return rt.IsValueType ? Activator.CreateInstance(rt) : null;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handle(targetMethod!, args);
}

public class WorldProxy : NoOpProxy
{
    private System.Collections.Generic.OrderedDictionary<IRecipeIngredientBase, List<IRecipeBase>>? _index;
    private Dictionary<string, Item>? _items;
    private ILogger? _logger;

    public void Bind(System.Collections.Generic.OrderedDictionary<IRecipeIngredientBase, List<IRecipeBase>> index,
                     Dictionary<string, Item> items, ILogger logger)
    { _index = index; _items = items; _logger = logger; }

    protected override object? Handle(MethodInfo m, object?[]? args)
    {
        switch (m.Name)
        {
            case "get_FastSearchRecipesByIngredient": return _index;
            case "get_Logger": return _logger;
            case "get_Collectibles": return new List<CollectibleObject>(_items!.Values);
            case "GetItem": return _items!.TryGetValue(args![0]!.ToString()!, out var it) ? it : null;
            case "GetBlock": return null;
            default: return Unhandled(m);
        }
    }
}

public class ApiProxy : NoOpProxy
{
    private IWorldAccessor? _world;
    private ILogger? _logger;

    public void Bind(IWorldAccessor world, ILogger logger) { _world = world; _logger = logger; }

    protected override object? Handle(MethodInfo m, object?[]? args)
    {
        switch (m.Name)
        {
            case "get_World": return _world;
            case "get_Logger": return _logger;
            default: return Unhandled(m);
        }
    }
}
