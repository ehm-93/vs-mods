using Vintagestory.API.Common;

namespace Ehm93.VS.Fallow;

public class FallowModSystem : ModSystem
{
    public const string ModId = "cropsfallow";
    
    public override void Start(ICoreAPI api)
    {
        api.Logger.Notification($"[{ModId}] Starting...");
    }
    
    public override void StartServerSide(ICoreServerAPI api)
    {
        api.Logger.Notification($"[{ModId}] Server-side initialization");
    }
    
    public override void StartClientSide(ICoreClientAPI api)
    {
        api.Logger.Notification($"[{ModId}] Client-side initialization");
    }
}
