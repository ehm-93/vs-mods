using System.Collections.Generic;
using Ehm93.VS.Driftworks.Core.Api;
using Manifold.Api.Worldgen;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Ehm93.VS.Driftworks.Core.Internal;

/// <summary>
/// The run-dungeon generator. Lays out a small grid of one-chunk room cells around the run's centre,
/// stamps each cell's shell (floor, walls, ceiling) with doorways to its in-grid neighbours, and
/// fills each non-centre cell's interior with a content-registered <see cref="RoomDef"/>. The centre
/// cell is left plain as a safe spawn. Each cell is exactly one chunk column, so generation stays
/// per-column with no cross-chunk writes.
/// </summary>
internal sealed class PrefabWorldgen : IWorldgenStrategy
{
    public const int FloorY = 20;
    public const int Height = 6;
    public const int Cell = 32;        // one chunk per cell
    public const int GridRadius = 1;   // 3x3 cells

    /// <summary>Relight up to here (above the sealed ceiling) so the torch block-light is computed.</summary>
    public const int RelightTop = FloorY + Height + 6;  // 32

    private readonly IReadOnlyList<RoomDef> rooms;
    private readonly int centerCx;
    private readonly int centerCz;

    private IWorldAccessor world = null!;
    private int rockId;
    private int torchId;

    public PrefabWorldgen(IReadOnlyList<RoomDef> rooms, int centerCx, int centerCz)
    {
        this.rooms = rooms;
        this.centerCx = centerCx;
        this.centerCz = centerCz;
    }

    public void OnInitialize(IWorldgenInitContext ctx)
    {
        world = ctx.Api.World;
        rockId = world.GetBlock(new AssetLocation("game:rock-granite"))?.Id ?? 0;
        // Manifold custom dimensions have no sky/ambient light (its relight pass only propagates
        // existing light), so a sealed run is pitch black without block light. Place torches.
        torchId = world.GetBlock(new AssetLocation("game:torch-basic-lit-up"))?.Id ?? 0;
    }

    public void GenerateColumn(IWorldgenChunkContext ctx)
    {
        if (rockId == 0) return;

        int gx = ctx.ChunkX - centerCx;
        int gz = ctx.ChunkZ - centerCz;
        if (gx < -GridRadius || gx > GridRadius || gz < -GridRadius || gz > GridRadius) return;

        bool doorW = gx > -GridRadius;
        bool doorE = gx < GridRadius;
        bool doorN = gz > -GridRadius;
        bool doorS = gz < GridRadius;

        int baseX = ctx.ChunkX * Cell;
        int baseZ = ctx.ChunkZ * Cell;
        int dim = ctx.DimensionId;
        var ba = ctx.BlockAccessor;

        // Shell: floor + ceiling everywhere, walls on the perimeter minus doorways. Interior and
        // doorways are left as the dimension's void (air), so we only ever place solid blocks.
        for (int lx = 0; lx < Cell; lx++)
        {
            for (int lz = 0; lz < Cell; lz++)
            {
                int wx = baseX + lx;
                int wz = baseZ + lz;
                ba.SetBlock(rockId, new BlockPos(wx, FloorY, wz, dim));
                ba.SetBlock(rockId, new BlockPos(wx, FloorY + Height + 1, wz, dim));  // ceiling (sealed)

                bool edge = lx == 0 || lx == Cell - 1 || lz == 0 || lz == Cell - 1;
                if (!edge) continue;

                for (int dy = 1; dy <= Height; dy++)
                {
                    if (IsDoorway(lx, lz, dy, doorN, doorS, doorE, doorW)) continue;
                    ba.SetBlock(rockId, new BlockPos(wx, FloorY + dy, wz, dim));
                }
            }
        }

        // Interior content: a registered room in each non-centre cell; the centre is the safe spawn.
        if ((gx != 0 || gz != 0) && rooms.Count > 0)
        {
            int sel = (ctx.ChunkX * 73856093) ^ (ctx.ChunkZ * 19349663);
            RoomDef room = rooms[(int)((uint)sel % (uint)rooms.Count)];
            room.Build(new RoomContext(ba, world, dim,
                baseX + 1, FloorY + 1, baseZ + 1,
                baseX + Cell - 2, FloorY + Height, baseZ + Cell - 2,
                ctx.Rng));
        }

        // Baseline light, placed last so content never buries it: four upright floor torches near
        // the wall midpoints. (Lighting becomes a content/modifier knob later — darkness is design.)
        if (torchId != 0)
        {
            int c = Cell / 2;
            ba.SetBlock(torchId, new BlockPos(baseX + c, FloorY + 1, baseZ + 2, dim));
            ba.SetBlock(torchId, new BlockPos(baseX + c, FloorY + 1, baseZ + Cell - 3, dim));
            ba.SetBlock(torchId, new BlockPos(baseX + 2, FloorY + 1, baseZ + c, dim));
            ba.SetBlock(torchId, new BlockPos(baseX + Cell - 3, FloorY + 1, baseZ + c, dim));
        }
    }

    private static bool IsDoorway(int lx, int lz, int dy, bool doorN, bool doorS, bool doorE, bool doorW)
    {
        if (dy > 2) return false;  // doorways are 2 high
        bool midX = lx == Cell / 2 - 1 || lx == Cell / 2;
        bool midZ = lz == Cell / 2 - 1 || lz == Cell / 2;
        if (doorN && lz == 0 && midX) return true;
        if (doorS && lz == Cell - 1 && midX) return true;
        if (doorW && lx == 0 && midZ) return true;
        if (doorE && lx == Cell - 1 && midZ) return true;
        return false;
    }
}
