using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Ehm93.VS.Crops.Common;

public static class GreenhouseUtil
{
    // True if pos sits in an enclosed room whose roof is majority skylight blocks (glass).
    // Matches the semantics BEBehaviorBerryChilling already uses for vernalization detection.
    public static bool IsGreenhouse(ICoreAPI api, BlockPos pos)
    {
        var rooms = api.ModLoader.GetModSystem<RoomRegistry>();
        var room = rooms?.GetRoomForPosition(pos);
        return room != null && room.SkylightCount > room.NonSkylightCount && room.ExitCount == 0;
    }
}
