using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.Common;

namespace Ehm93.VS.Performance.CraftingGrid.Internal;

/// <summary>
/// Drop-in replacement for <c>InventoryCraftingGrid.FindMatchingRecipe</c>. Gathers candidate recipes
/// via <see cref="CraftingRecipeIndex"/> (one O(1) hash lookup per occupied slot against the
/// pre-expanded code→recipes map) instead of the engine's full linear scan over every distinct
/// ingredient. The second pass — non-shapeless then shapeless, first <c>recipe.Matches</c> wins — is
/// preserved verbatim, including the call to <c>Matches</c> (so the MatchesGridRecipe event still
/// fires), so the chosen recipe and output are identical to vanilla. Runs on client and server alike.
///
/// Caveat: when two distinct recipes BOTH fully match the same grid, the engine's tie-break is the
/// ingredient-index iteration order; ours is grid-slot order. The candidate SET is identical, so this
/// only differs for genuinely ambiguous recipe sets (none in vanilla) and never changes whether a
/// craft is found.
///
/// Telemetry: every call is timed; a summary (count / avg / max µs) is logged every
/// <see cref="LogEvery"/> calls. First call includes the one-time index build, so its "last" is large.
/// </summary>
[HarmonyPatch(typeof(InventoryCraftingGrid), "FindMatchingRecipe")]
internal static class FindMatchingRecipePatch
{
    private const long LogEvery = 100;

    private static long _calls;
    private static long _totalTicks;
    private static long _maxTicks;

    private static bool Prefix(InventoryCraftingGrid __instance)
    {
        // Only take over once our index is built; until then (a multi-second build on big modpacks,
        // run in the background) let the engine's own matcher handle this craft. No freeze, no risk.
        CraftingRecipeIndex? index = CraftingRecipeIndex.GetReadyOrStartBuild(__instance.Api.World);
        if (index == null) return true; // run the original FindMatchingRecipe

        long start = Stopwatch.GetTimestamp();
        int candidates = DoMatch(__instance, index);
        Record(__instance.Api.Logger, Stopwatch.GetTimestamp() - start, candidates);
        return false; // skip the original
    }

    // The actual matcher: gather candidates (O(1)/slot) then run the engine's authority pass.
    // Returns the candidate count (for telemetry).
    private static int DoMatch(InventoryCraftingGrid inv, CraftingRecipeIndex index)
    {
        IWorldAccessor world = inv.Api.World;

        int outputIndex = inv.Count - 1;                       // output slot id (== GridSizeSq)
        int gridWidth = (int)Math.Round(Math.Sqrt(outputIndex)); // 3 for the 3×3 grid

        var slots = new ItemSlot[outputIndex];
        for (int i = 0; i < outputIndex; i++) slots[i] = inv[i];
        ItemSlot output = inv[outputIndex];

        inv.MatchingRecipe = null;
        output.Itemstack = null;

        List<GridRecipe> candidates = index.GatherCandidates(slots);

        // Authority pass — identical to the engine: shaped recipes first, then shapeless, first match wins.
        IPlayer forPlayer = inv.Player;
        foreach (GridRecipe r in candidates)
        {
            if (!r.Shapeless && r.Matches(forPlayer, world, slots, gridWidth)) { FoundMatch(inv, r, slots, output, outputIndex); return candidates.Count; }
        }
        foreach (GridRecipe r in candidates)
        {
            if (r.Shapeless && r.Matches(forPlayer, world, slots, gridWidth)) { FoundMatch(inv, r, slots, output, outputIndex); return candidates.Count; }
        }

        inv.MarkSlotDirty(outputIndex);
        return candidates.Count;
    }

    private static void FoundMatch(InventoryCraftingGrid inv, GridRecipe recipe, ItemSlot[] slots, ItemSlot output, int outputIndex)
    {
        inv.MatchingRecipe = recipe;
        recipe.GenerateOutputStack(slots, output);
        inv.MarkSlotDirty(outputIndex);
    }

    private static void Record(ILogger logger, long ticks, int candidates)
    {
        long n = Interlocked.Increment(ref _calls);
        Interlocked.Add(ref _totalTicks, ticks);
        if (ticks > _maxTicks) _maxTicks = ticks; // racy, telemetry-only

        if (n % LogEvery == 0)
        {
            double toUs = 1_000_000.0 / Stopwatch.Frequency;
            double avg = _totalTicks * toUs / n;
            logger.Notification(
                $"[performancecraftinggrid] {n} matches | avg {avg:F2}us | max {_maxTicks * toUs:F2}us " +
                $"| last {ticks * toUs:F2}us ({candidates} candidates)");
        }
    }
}
