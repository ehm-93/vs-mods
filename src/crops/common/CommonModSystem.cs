using HarmonyLib;
using Vintagestory.API.Common;

namespace Ehm93.VS.Crops.Common;

public class CommonModSystem : ModSystem
{
    public const string ModId = "cropscommon";

    private Harmony? patcher;

    public override void Start(ICoreAPI api)
    {
        base.Start(api);
        if (!Harmony.HasAnyPatches(ModId))
        {
            patcher = new Harmony(ModId);
            patcher.PatchCategory(ModId);
            HoePatches.Patch(patcher);
        }
    }

    public override void Dispose()
    {
        patcher?.UnpatchAll(ModId);
    }
}
