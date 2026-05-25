using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace Ehm93.VS.Crops.Weeds;

public class WeedsModSystem : ModSystem
{
    public const string ModId = "cropsweeds";

    public override void Start(ICoreAPI api)
    {
        base.Start(api);
        api.RegisterBlockEntityBehaviorClass("FarmlandWeeds", typeof(BEBehaviorCropWeeds));
        api.RegisterCollectibleBehaviorClass("HoeWeeds", typeof(CBehaviorHoeWeeds));
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        SetWeedinessCommand.Register(api);
    }
}
