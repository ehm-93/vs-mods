using Vintagestory.API.Common;

namespace Ehm93.VS.Berrychilling;

public class BerrychillingModSystem : ModSystem
{
    public const string ModId = "cropsberrychilling";
    
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
