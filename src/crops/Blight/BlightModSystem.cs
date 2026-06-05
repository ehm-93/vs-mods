using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace Ehm93.VS.Crops.Blight;

public class BlightModSystem : ModSystem
{
    public const string ModId = "cropsblight";

    private Harmony? patcher;

    public override void Start(ICoreAPI api)
    {
        base.Start(api);
        api.RegisterBlockEntityBehaviorClass("FarmlandBlight", typeof(BEBehaviorFarmlandBlight));
        if (!Harmony.HasAnyPatches(ModId))
        {
            patcher = new Harmony(ModId);
            patcher.PatchCategory(ModId);
        }
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        BlightCommands.Register(api);
    }

    public override void Dispose()
    {
        patcher?.UnpatchAll(ModId);
    }
}
