using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;

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
            BEBerryBushPatches.Patch(patcher);
        }
    }

    public override void AssetsFinalize(ICoreAPI api)
    {
        // https://github.com/gabriella-campos-davis/Herbarium/pull/20
        // Herbarium already wires this up itself, so skip when present.
        if (!Herbarium.IsLoaded()) AttachBerryChilling(api);
        base.AssetsFinalize(api);
    }

    public override void Dispose()
    {
        patcher?.UnpatchAll(ModId);
    }

    private void AttachBerryChilling(ICoreAPI api)
    {
        if (api.Side != EnumAppSide.Server) return;

        foreach (var b in api.World.Blocks)
        {
            if (b.Code.Domain != "game") continue;
            if (!b.Code.Path.StartsWith("bigberrybush-") || b.Code.Path.StartsWith("smallberrybush-")) continue;

            b.EntityClass ??= "Generic";
            b.BlockEntityBehaviors =
            [
                .. b.BlockEntityBehaviors,
                new BlockEntityBehaviorType
                {
                    Name = "BerryChilling",
                    properties = JsonObject.FromJson("{\"chillTemp\":7,\"chilledDaysRequired\":10}"),
                },
            ];
        }
    }
}
