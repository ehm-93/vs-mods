using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Client;
using Vintagestory.API.Server;
using Ehm93.VS.Performance.CraftingGrid.Internal;

namespace Ehm93.VS.Performance.CraftingGrid;

/// <summary>
/// Replaces the crafting grid's recipe matcher with a hash-indexed version. The engine's
/// <c>InventoryCraftingGrid.FindMatchingRecipe</c> linearly scans EVERY distinct recipe ingredient
/// (calling <c>SatisfiesAsIngredient</c>) for each occupied grid slot, on every grid change — on
/// both client and server. We pre-expand every recipe ingredient (exact AND wildcard/tag) into a
/// single <c>code → recipes</c> hashmap, so the per-keystroke match is an O(1) lookup per slot. The
/// pre-expansion sweeps the whole collectible registry, which is multi-second on a big modpack, so it
/// runs once on a background thread, prewarmed at world load. See
/// <see cref="Internal.FindMatchingRecipePatch"/> and <see cref="Internal.CraftingRecipeIndex"/>.
/// </summary>
public class CraftingGridModSystem : ModSystem
{
    public const string HarmonyId = "ehm93.performance.craftinggrid";

    private Harmony? harmony;

    public override void Start(ICoreAPI api)
    {
        // Recipes are resolved per world load; drop any stale index so a fresh world rebuilds.
        CraftingRecipeIndex.Invalidate();

        if (!Harmony.HasAnyPatches(HarmonyId))
        {
            harmony = new Harmony(HarmonyId);
            harmony.PatchAll(Assembly.GetExecutingAssembly());
        }
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        // Recipes + collectibles are fully resolved by the time the savegame loads. Kick off the
        // (background) index build now so it's ready before the player ever opens the crafting grid.
        api.Event.SaveGameLoaded += () => Prewarm(api);
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        // Client recipes are synced + resolved by the time the level finalizes.
        api.Event.LevelFinalize += () => Prewarm(api);
    }

    private static void Prewarm(ICoreAPI api)
    {
        api.Logger.Notification("[performancecraftinggrid] world ready — prewarming recipe index in the background");
        CraftingRecipeIndex.GetReadyOrStartBuild(api.World);
    }

    public override void Dispose()
    {
        harmony?.UnpatchAll(HarmonyId);
        CraftingRecipeIndex.Invalidate();
    }
}
