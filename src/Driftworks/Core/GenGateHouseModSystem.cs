using System;
using Ehm93.VS.Driftworks.Core.Internal;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Ehm93.VS.Driftworks.Core;

/// <summary>
/// Sprinkles placeholder gate houses (each containing a map device) into the overworld during
/// worldgen, so the device is found rather than crafted. Placeholder frequency — tune for real
/// rarity once the gate dungeon proper exists. Use /dw house to place one on demand for testing.
/// </summary>
public class GenGateHouseModSystem : ModSystem
{
    private const int FrequencyInChunks = 60;  // ~1 placement per 60 chunks (placeholder)

    private ICoreServerAPI sapi = null!;
    private IWorldGenBlockAccessor? wgenBa;

    public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Server;

    public override void StartServerSide(ICoreServerAPI api)
    {
        sapi = api;
        api.Event.GetWorldgenBlockAccessor(chunkProvider => wgenBa = chunkProvider.GetBlockAccessor(updateHeightmap: true));
        api.Event.ChunkColumnGeneration(OnChunkColumnGen, EnumWorldGenPass.TerrainFeatures, "standard");
    }

    private void OnChunkColumnGen(IChunkColumnGenerateRequest request)
    {
        if (wgenBa == null) return;

        int cx = request.ChunkX;
        int cz = request.ChunkZ;

        // Deterministic per-chunk chance (seeded by world + chunk coords).
        int hash = unchecked((cx * 73856093) ^ (cz * 19349663) ^ sapi.World.Seed);
        if (new Random(hash).Next(FrequencyInChunks) != 0) return;

        int x = cx * 32 + 16;
        int z = cz * 32 + 16;
        int y = wgenBa.GetTerrainMapheightAt(new BlockPos(x, 0, z, 0));
        if (y <= 0 || y < sapi.World.SeaLevel) return;                                  // skip invalid / deep ocean
        if (wgenBa.GetBlock(new BlockPos(x, y + 1, z, 0))?.IsLiquid() == true) return;  // skip underwater

        if (GateHouse.Build(wgenBa, sapi.World, new BlockPos(x, y, z, 0)))
        {
            sapi.Logger.Notification("[{0}] Gate house placed at {1}, {2}, {3}", CoreModSystem.ModId, x, y, z);
        }
    }
}
