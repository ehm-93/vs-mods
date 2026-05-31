using System.Collections.Generic;
using HarmonyLib;
using Vintagestory.API.Common;

namespace Ehm93.VS.Primitive.Pemmican;

public class PemmicanModSystem : ModSystem
{
    public const string ModId = "pemmican";

    private Harmony? patcher;
    private readonly List<DryingRecipe> dryingRecipes = new();

    public override void Start(ICoreAPI api)
    {
        base.Start(api);

        api.RegisterBlockClass("SmokeRack", typeof(BlockSmokeRack));
        api.RegisterBlockClass("SmokingFirepit", typeof(BlockSmokingFirepit));
        api.RegisterBlockEntityClass("SmokingFirepit", typeof(BlockEntitySmokingFirepit));
        api.RegisterBlockEntityClass("DryingRack", typeof(BlockEntityDryingRack));

        if (!Harmony.HasAnyPatches(ModId))
        {
            patcher = new Harmony(ModId);
            patcher.PatchCategory(ModId);
        }
    }

    // Load the data-driven drying/smoking recipes from assets/<domain>/config/drying/*.json so the rack's
    // input->output mappings live in JSON, not code. These live under config/ (a Universal asset category)
    // rather than recipes/ — recipes/ is server-only (EnumAppSide.Server), so loading there leaves the
    // CLIENT with zero recipes, making IsRackable always false client-side and breaking the hang
    // interaction. config/ loads on both sides, so client and server agree with no network sync. Disabled
    // recipes and those whose dependsOn is unsatisfied are dropped here.
    public override void AssetsFinalize(ICoreAPI api)
    {
        base.AssetsFinalize(api);
        dryingRecipes.Clear();

        foreach (IAsset asset in api.Assets.GetMany("config/drying/", null, true))
        {
            DryingRecipe[]? entries;
            try { entries = asset.ToObject<DryingRecipe[]>(); }
            catch { api.Logger.Warning("[{0}] Could not parse drying recipes {1}", ModId, asset.Location); continue; }
            if (entries == null) continue;

            foreach (DryingRecipe r in entries)
            {
                if (!r.Enabled) continue;
                if (!r.DependenciesSatisfied(api.ModLoader)) continue;
                if (string.IsNullOrEmpty(r.Input?.Code))
                {
                    api.Logger.Warning("[{0}] Drying recipe '{1}' in {2} has no input code; skipping.", ModId, r.Code ?? "?", asset.Location);
                    continue;
                }
                r.Init();
                dryingRecipes.Add(r);
            }
        }

        api.Logger.Notification("[{0}] Loaded {1} drying recipe(s).", ModId, dryingRecipes.Count);
    }

    // The drying result for a hung input — output item, output quantity, and hours-to-finish — or null
    // if nothing matches or the output item is missing (e.g. an unsupported modded fruit). The
    // output-exists check keeps un-convertible items off the rack.
    public DryingResult? Match(IWorldAccessor world, ItemStack? stack)
    {
        if (stack?.Collectible?.Code is not AssetLocation code) return null;

        foreach (DryingRecipe r in dryingRecipes)
        {
            AssetLocation? outCode = r.OutputFor(code);
            if (outCode != null && world.GetItem(outCode) is Item item)
                return new DryingResult(item, r.Output.Quantity, r.Hours, r.RequiresFire);
        }
        return null;
    }

    public override void Dispose()
    {
        patcher?.UnpatchAll(ModId);
    }
}
