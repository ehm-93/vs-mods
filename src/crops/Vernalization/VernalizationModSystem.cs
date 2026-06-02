using HarmonyLib;
using Vintagestory.API.Common;

namespace Ehm93.VS.Crops.Vernalization;

public class VernalizationModSystem : ModSystem
{
    public const string ModId = "cropsvernalization";

    private Harmony? patcher;

    public override void Start(ICoreAPI api)
    {
        base.Start(api);
        api.RegisterBlockEntityBehaviorClass("BerryChilling", typeof(BEBehaviorBerryChilling));

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
