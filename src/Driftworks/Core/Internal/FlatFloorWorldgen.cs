using Manifold.Api.Worldgen;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Ehm93.VS.Driftworks.Core.Internal;

/// <summary>
/// Placeholder run worldgen: a flat solid floor in every column so a transited player has
/// ground to stand on and a visible reference to move against. Replaced by prefab-room
/// stitching in a later build step — for now it only has to prove the round trip is walkable.
/// </summary>
internal sealed class FlatFloorWorldgen : IWorldgenStrategy
{
    private const int ChunkSize = 32;

    private readonly int floorHeight;
    private int floorBlockId;
    private int topBlockId;

    public FlatFloorWorldgen(int floorHeight)
    {
        this.floorHeight = floorHeight;
    }

    public void OnInitialize(IWorldgenInitContext ctx)
    {
        floorBlockId = ResolveFirst(ctx.Api, "game:rock-granite", "game:rock-andesite", "game:rock-basalt");
        topBlockId = ResolveFirst(ctx.Api, "game:soil-medium-normal", "game:soil-low-normal", "game:rock-granite");

        if (floorBlockId == 0)
        {
            ctx.Api.Logger.Warning("[driftworkscore] FlatFloorWorldgen: no floor block resolved; run dim will be void.");
        }
    }

    public void GenerateColumn(IWorldgenChunkContext ctx)
    {
        if (floorBlockId == 0)
        {
            return;
        }

        int baseX = ctx.ChunkX * ChunkSize;
        int baseZ = ctx.ChunkZ * ChunkSize;

        for (int lx = 0; lx < ChunkSize; lx++)
        {
            for (int lz = 0; lz < ChunkSize; lz++)
            {
                int wx = baseX + lx;
                int wz = baseZ + lz;
                for (int y = 1; y <= floorHeight; y++)
                {
                    int blockId = (y == floorHeight && topBlockId != 0) ? topBlockId : floorBlockId;
                    // Positions MUST be dimension-encoded with the dim being generated.
                    ctx.BlockAccessor.SetBlock(blockId, new BlockPos(wx, y, wz, ctx.DimensionId));
                }
            }
        }
    }

    private static int ResolveFirst(ICoreServerAPI api, params string[] codes)
    {
        foreach (string code in codes)
        {
            Block? block = api.World.GetBlock(new AssetLocation(code));
            if (block is not null && block.Id != 0)
            {
                return block.Id;
            }
        }

        return 0;
    }
}
