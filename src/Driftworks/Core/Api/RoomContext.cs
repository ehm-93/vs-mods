using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Ehm93.VS.Driftworks.Core.Api;

/// <summary>
/// Handed to a <see cref="RoomDef"/> when the run-dungeon generator stamps that room into a cell.
/// A room may only write blocks within the interior bounds [Min..Max] (inclusive) — the generator
/// owns the cell's floor, walls, ceiling, and the doorways between cells. Writes outside the bounds
/// are ignored, so a room can't bleed into a neighbour or the shell.
/// </summary>
public sealed class RoomContext
{
    private readonly IBlockAccessor blocks;
    private readonly int dimensionId;

    public RoomContext(IBlockAccessor blocks, IWorldAccessor world, int dimensionId,
        int minX, int minY, int minZ, int maxX, int maxY, int maxZ, LCGRandom rng)
    {
        this.blocks = blocks;
        World = world;
        this.dimensionId = dimensionId;
        MinX = minX; MinY = minY; MinZ = minZ;
        MaxX = maxX; MaxY = maxY; MaxZ = maxZ;
        Rng = rng;
    }

    public IWorldAccessor World { get; }
    public int MinX { get; }
    public int MinY { get; }
    public int MinZ { get; }
    public int MaxX { get; }
    public int MaxY { get; }
    public int MaxZ { get; }

    /// <summary>Deterministic per-cell random source.</summary>
    public LCGRandom Rng { get; }

    public int CentreX => (MinX + MaxX) / 2;
    public int CentreZ => (MinZ + MaxZ) / 2;

    /// <summary>Resolve a block id from its code (e.g. "game:rock-granite"); 0 if not found.</summary>
    public int BlockId(string code) => World.GetBlock(new AssetLocation(code))?.Id ?? 0;

    /// <summary>Set a block by id at a world position. Ignored if outside the interior bounds.</summary>
    public void SetBlock(int blockId, int x, int y, int z)
    {
        if (x < MinX || x > MaxX || y < MinY || y > MaxY || z < MinZ || z > MaxZ) return;
        blocks.SetBlock(blockId, new BlockPos(x, y, z, dimensionId));
    }
}
