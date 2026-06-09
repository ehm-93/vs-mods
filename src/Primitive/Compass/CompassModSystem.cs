using Vintagestory.API.Common;
using Vintagestory.API.Client;

namespace Ehm93.VS.Primitive.Compass;

public class CompassModSystem : ModSystem
{
    public const string ModId = "primitivecompass";

    public override void Start(ICoreAPI api)
    {
        base.Start(api);
        api.RegisterBlockClass("BlockCompass", typeof(BlockCompass));
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        base.StartClientSide(api);
        CompassTuningCommand.Register(api);
    }
}
