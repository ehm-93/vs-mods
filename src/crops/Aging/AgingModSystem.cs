using Vintagestory.API.Common;

namespace Ehm93.VS.Crops.Aging;

public class AgingModSystem : ModSystem
{
    public const string ModId = "cropsaging";

    public override void Start(ICoreAPI api)
    {
        base.Start(api);
        api.RegisterBlockEntityBehaviorClass("FarmlandAging", typeof(BEBehaviorFarmlandAging));
    }
}
