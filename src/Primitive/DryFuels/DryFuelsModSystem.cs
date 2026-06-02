using HarmonyLib;
using Vintagestory.API.Common;

namespace Ehm93.VS.Primitive.DryFuels;

public class DryFuelsModSystem : ModSystem
{
    public const string ModId = "dryfuels";

    private Harmony? patcher;

    public override void Start(ICoreAPI api)
    {
        base.Start(api);
        api.RegisterBlockEntityBehaviorClass("FuelSeasoning", typeof(BEBehaviorFuelSeasoning));
        api.RegisterBlockEntityClass("SeasonedCharcoalPit", typeof(BlockEntitySeasonedCharcoalPit));

        if (!Harmony.HasAnyPatches(ModId))
        {
            patcher = new Harmony(ModId);
            patcher.PatchCategory(ModId);
        }
    }

    public override void Dispose()
    {
        patcher?.UnpatchAll(ModId);
    }
}
