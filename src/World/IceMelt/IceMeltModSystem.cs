using HarmonyLib;
using Vintagestory.API.Common;

namespace Ehm93.VS.World.IceMelt;

public class IceMeltModSystem : ModSystem
{
    public const string ModId = "worldicemelt";

    private Harmony? patcher;

    public override void Start(ICoreAPI api)
    {
        base.Start(api);
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
