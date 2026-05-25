using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace Ehm93.VS.Crops.Blight;

public class BlightModSystem : ModSystem
{
    public const string ModId = "cropsblight";

    public override void Start(ICoreAPI api)
    {
        base.Start(api);
        api.RegisterBlockEntityBehaviorClass("FarmlandBlight", typeof(BEBehaviorFarmlandBlight));
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        BlightCommands.Register(api);
    }
}
