using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Ehm93.VS.Driftworks.Core.Internal;

/// <summary>
/// Builds the placeholder "gate house" — a simple rock box with a doorway and a map device on the
/// floor at its centre. Shared by worldgen placement and the /dw house debug command. Stands in for
/// a real gate-dungeon prefab; for now it just gets the device into the world to be found.
/// </summary>
internal static class GateHouse
{
    public const int Half = 3;       // 7x7 footprint
    public const int WallHeight = 4;

    /// <summary>
    /// Stamp the house with its floor at basePos.Y, centred on basePos's X/Z (in basePos's
    /// dimension). The doorway faces -Z (north). Returns false if the rock block can't be resolved.
    /// </summary>
    public static bool Build(IBlockAccessor ba, IWorldAccessor world, BlockPos basePos)
    {
        int rock = world.GetBlock(new AssetLocation("game:rock-granite"))?.Id ?? 0;
        if (rock == 0) return false;
        int device = world.GetBlock(new AssetLocation(CoreModSystem.ModId, "mapdevice"))?.Id ?? 0;

        int dim = basePos.dimension;
        int x0 = basePos.X, y0 = basePos.Y, z0 = basePos.Z;

        for (int dx = -Half; dx <= Half; dx++)
        {
            for (int dz = -Half; dz <= Half; dz++)
            {
                bool edge = dx == -Half || dx == Half || dz == -Half || dz == Half;
                ba.SetBlock(rock, new BlockPos(x0 + dx, y0, z0 + dz, dim));  // floor

                for (int dy = 1; dy <= WallHeight; dy++)
                {
                    ba.SetBlock(edge ? rock : 0, new BlockPos(x0 + dx, y0 + dy, z0 + dz, dim));  // walls / interior air
                }

                ba.SetBlock(rock, new BlockPos(x0 + dx, y0 + WallHeight + 1, z0 + dz, dim));  // roof
            }
        }

        // Doorway: 1 wide, 2 high, in the north (-Z) wall.
        ba.SetBlock(0, new BlockPos(x0, y0 + 1, z0 - Half, dim));
        ba.SetBlock(0, new BlockPos(x0, y0 + 2, z0 - Half, dim));

        // The map device on the floor at the centre.
        if (device != 0) ba.SetBlock(device, new BlockPos(x0, y0 + 1, z0, dim));

        return true;
    }
}
