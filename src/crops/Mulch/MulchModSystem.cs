using HarmonyLib;
using Vintagestory.API.Common;

namespace Ehm93.VS.Crops.Mulch;

public class MulchModSystem : ModSystem
{
    public const string ModId = "cropsmulch";

    private Harmony? patcher;

    public override void Start(ICoreAPI api)
    {
        base.Start(api);
        api.RegisterBlockEntityBehaviorClass("FarmlandMulch", typeof(BEBehaviorFarmlandMulch));

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
