using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Vintagestory.API.Common;

namespace Ehm93.VS.Performance.CraftingGrid.Internal;

/// <summary>
/// A fully pre-expanded view over the engine's <see cref="IWorldAccessor.FastSearchRecipesByIngredient"/>
/// index. The engine matches every distinct ingredient (exact AND wildcard/tag) against each grid slot
/// on every grid change — O(distinct ingredients) per slot. We invert that ONCE at world load: exact
/// ingredients map their literal code, and pattern ingredients (wildcard/tag) are expanded by walking
/// the collectible registry and recording every concrete item they accept. The result is a single
/// <c>code → recipes</c> hashmap, so the per-keystroke gather is a plain O(1) lookup per occupied slot,
/// independent of how many recipes (or patterns) exist.
///
/// Membership for patterns is decided by the engine's own <c>SatisfiesAsIngredient</c> against a
/// representative stack per collectible, so the expanded candidate set is identical to what the engine
/// would gather at match time. (Assumption: a collectible's tag set does not vary by stack attributes —
/// true for the default <c>CollectibleObject.GetTags</c>. Wildcards match purely on code, so they are
/// unconditionally safe.) The second pass <c>recipe.Matches</c> remains the authority, so any code that
/// resolves to a recipe whose finer requirements (attributes, shape) don't hold is still rejected.
///
/// Built lazily and shared process-wide (client and server see identical, synced recipes); rebuilt when
/// the engine index size changes (a world reload) or on explicit <see cref="Invalidate"/>.
/// </summary>
internal sealed class CraftingRecipeIndex
{
    /// <summary>Item/block code → every enabled grid recipe an item of that code can contribute to.</summary>
    public readonly Dictionary<AssetLocation, GridRecipe[]> ByCode = new();

    /// <summary>
    /// The candidate gather that replaces the engine's per-slot linear scan: each occupied grid item
    /// is a single hash lookup. Returns the deduplicated candidate recipes; the second pass
    /// <c>recipe.Matches</c> is the authority. Player-free, so it is directly testable/benchmarkable.
    /// </summary>
    public List<GridRecipe> GatherCandidates(ItemSlot[] slots)
    {
        var seen = new HashSet<GridRecipe>();
        var candidates = new List<GridRecipe>();

        foreach (ItemSlot slot in slots)
        {
            ItemStack? stack = slot?.Itemstack;
            if (stack == null || stack.StackSize == 0) continue;

            AssetLocation? code = stack.Collectible?.Code;
            if (code != null && ByCode.TryGetValue(code, out var recipes))
            {
                foreach (GridRecipe g in recipes) if (seen.Add(g)) candidates.Add(g);
            }
        }

        return candidates;
    }

    /// <summary>Total (code, recipe) incidences stored — i.e. the sum of all bucket lengths.</summary>
    public int EntryCount
    {
        get
        {
            int n = 0;
            foreach (var kv in ByCode) n += kv.Value.Length;
            return n;
        }
    }

    // One index PER WORLD (keyed on the IWorldAccessor instance). Singleplayer runs a separate client
    // world and server world in the same process, and their recipe registries can differ slightly (a
    // mod may register a recipe on only one side) — so a single shared cache would have the two sides
    // perpetually invalidate and rebuild each other's index. Keying on the world instance gives each
    // side exactly one build. A reloaded world is a new instance → it rebuilds, and the old index is
    // collected together with its (weakly-held) world.
    private sealed class Slot
    {
        public volatile CraftingRecipeIndex? Index;
        public int Building; // 0/1 latch — at most one build in flight per world
    }

    private static readonly ConditionalWeakTable<IWorldAccessor, Slot> byWorld = new();

    /// <summary>Drop all cached indexes (used by the bench; in-game, world reloads recycle on their own).</summary>
    public static void Invalidate() => byWorld.Clear();

    /// <summary>
    /// Hot-path accessor for the Harmony prefix: returns this world's index if it is already built.
    /// Otherwise it kicks off a ONE-TIME background build for that world and returns null, so the caller
    /// falls back to the engine's own matcher for this call. On a big modpack the build is multi-second
    /// (a registry-wide pattern sweep); doing it off the main thread means crafting never freezes — the
    /// first few crafts just run vanilla until the fast index is ready.
    /// </summary>
    public static CraftingRecipeIndex? GetReadyOrStartBuild(IWorldAccessor world)
    {
        Slot slot = byWorld.GetValue(world, static _ => new Slot());

        CraftingRecipeIndex? idx = slot.Index;
        if (idx != null) return idx;

        if (Interlocked.CompareExchange(ref slot.Building, 1, 0) == 0)
        {
            Task.Run(() =>
            {
                try
                {
                    slot.Index = Build(world); // latched on; never rebuilt for this world once set
                }
                catch (Exception e)
                {
                    world.Logger.Warning("[performancecraftinggrid] index build failed, using vanilla matcher: " + e);
                    Interlocked.Exchange(ref slot.Building, 0); // allow a retry on a later craft
                }
            });
        }
        return null;
    }

    /// <summary>Synchronous build-and-cache for one world (used by the bench/tests). Prefer
    /// <see cref="GetReadyOrStartBuild"/> on the game's hot path.</summary>
    public static CraftingRecipeIndex Get(IWorldAccessor world)
    {
        Slot slot = byWorld.GetValue(world, static _ => new Slot());
        if (slot.Index != null) return slot.Index;
        lock (slot)
        {
            return slot.Index ??= Build(world);
        }
    }

    private static CraftingRecipeIndex Build(IWorldAccessor world)
    {
        var sw = Stopwatch.StartNew();
        var src = world.FastSearchRecipesByIngredient;

        // Accumulate with a per-code set (cheap dedup); frozen to arrays at the end to drop the slack.
        var building = new Dictionary<AssetLocation, HashSet<GridRecipe>>();

        void Add(AssetLocation code, GridRecipe[] recipes)
        {
            if (!building.TryGetValue(code, out var set)) building[code] = set = new HashSet<GridRecipe>();
            foreach (GridRecipe r in recipes) set.Add(r);
        }

        List<CollectibleObject> collectibles = world.Collectibles;
        ItemStack?[]? stacks = null; // one representative stack per collectible, built only if needed

        foreach (var kv in src)
        {
            IRecipeIngredientBase ingredient = kv.Key;

            GridRecipe[] recipes = EnabledGridRecipes(kv.Value);
            if (recipes.Length == 0) continue;

            if (ingredient.MatchingType == EnumRecipeMatchType.Exact && ingredient.Code != null)
            {
                Add(ingredient.Code, recipes);
            }
            else
            {
                // Pre-expand: fold every concrete item this pattern accepts into the same hashmap.
                stacks ??= BuildStacks(collectibles);
                for (int i = 0; i < stacks.Length; i++)
                {
                    ItemStack? stack = stacks[i];
                    if (stack != null && ingredient.SatisfiesAsIngredient(stack, checkStackSize: false))
                    {
                        Add(collectibles[i].Code, recipes);
                    }
                }
            }
        }

        var index = new CraftingRecipeIndex();
        foreach (var kv in building) index.ByCode[kv.Key] = ToArray(kv.Value);

        sw.Stop();
        world.Logger.Notification(
            $"[performancecraftinggrid] pre-expanded index built in {sw.Elapsed.TotalMilliseconds:F1}ms — " +
            $"{src.Count} distinct ingredients, {collectibles.Count} collectibles swept, " +
            $"{index.ByCode.Count} code buckets, {index.EntryCount} entries");
        return index;
    }

    private static GridRecipe[] EnabledGridRecipes(List<IRecipeBase> recipes)
    {
        List<GridRecipe>? list = null;
        foreach (IRecipeBase r in recipes)
        {
            if (r is GridRecipe { Enabled: true } g) (list ??= new List<GridRecipe>()).Add(g);
        }
        return list?.ToArray() ?? Array.Empty<GridRecipe>();
    }

    private static ItemStack?[] BuildStacks(List<CollectibleObject> collectibles)
    {
        var stacks = new ItemStack?[collectibles.Count];
        for (int i = 0; i < collectibles.Count; i++)
        {
            CollectibleObject c = collectibles[i];
            stacks[i] = (c == null || c.Code == null) ? null : new ItemStack(c, 1);
        }
        return stacks;
    }

    private static GridRecipe[] ToArray(HashSet<GridRecipe> set)
    {
        var arr = new GridRecipe[set.Count];
        set.CopyTo(arr);
        return arr;
    }
}
