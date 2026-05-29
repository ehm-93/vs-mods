using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Ehm93.VS.World.IceMelt;

// Keeps vanilla BlockLakeIce melt curve (chance = clamp((temp - 2) / 20, 0, 1)) but adds a fast path:
// when the block ticks and the local temperature is well above freezing AND the position isn't
// inside a Room (icehouse, basement, etc.), force melt this tick. Combined with the tick system
// scanning newly-loaded chunks shortly after load, this means exposed warm ice melts within seconds.
//
// World config keys (set in worldconfig.json or via /worldconfig):
//   worldicemelt:immediateMeltTemp   float  default 8   (°C above which exposed ice melts on next tick)
internal static class BlockLakeIcePatches
{
    [HarmonyPatchCategory(IceMeltModSystem.ModId)]
    [HarmonyPatch(typeof(BlockLakeIce), nameof(BlockLakeIce.ShouldReceiveServerGameTicks))]
    internal static class ShouldReceiveServerGameTicksPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(IWorldAccessor world, BlockPos pos, out object? extra, ref bool __result)
        {
            extra = null;

            float threshold = world.Config.GetFloat($"{IceMeltModSystem.ModId}:immediateMeltTemp", 8f);

            float temperature = world.BlockAccessor.GetClimateAt(
                pos,
                EnumGetClimateMode.ForSuppliedDate_TemperatureOnly,
                world.Calendar.TotalDays
            ).Temperature;

            if (temperature >= threshold)
            {
                var rooms = world.Api?.ModLoader.GetModSystem<RoomRegistry>();
                var room = rooms?.GetRoomForPosition(pos);
                if (room == null)
                {
                    __result = true;
                    return false; // skip vanilla, melt this tick
                }
            }

            return true; // fall through to vanilla probability curve
        }
    }
}
