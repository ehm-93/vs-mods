using HarmonyLib;
using Vintagestory.API.Common;

namespace Ehm93.VS.Primitive.Pemmican;

public class PemmicanModSystem : ModSystem
{
    public const string ModId = "pemmican";

    private Harmony? patcher;

    public override void Start(ICoreAPI api)
    {
        base.Start(api);

        api.RegisterBlockClass("SmokeRack", typeof(BlockSmokeRack));
        api.RegisterBlockClass("SmokingFirepit", typeof(BlockSmokingFirepit));
        api.RegisterBlockEntityClass("SmokingFirepit", typeof(BlockEntitySmokingFirepit));

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
