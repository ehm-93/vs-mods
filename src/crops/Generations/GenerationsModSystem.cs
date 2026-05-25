using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace Ehm93.VS.Crops.Generations;

public class GenerationsModSystem : ModSystem
{
    public const string ModId = "cropsgenerations";

    private Harmony? patcher;

    public override void Start(ICoreAPI api)
    {
        base.Start(api);
        api.RegisterBlockEntityBehaviorClass("FarmlandGeneration", typeof(BEBehaviorFarmlandGeneration));

        if (!Harmony.HasAnyPatches(ModId))
        {
            patcher = new Harmony(ModId);
            patcher.PatchCategory(ModId);
        }
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        SetGenerationCommand.Register(api);
    }

    public override void Dispose()
    {
        patcher?.UnpatchAll(ModId);
    }
}
