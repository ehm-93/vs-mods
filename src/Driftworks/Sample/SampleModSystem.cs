using Ehm93.VS.Driftworks.Core;
using Ehm93.VS.Driftworks.Core.Api;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace Ehm93.VS.Driftworks.Sample;

/// <summary>
/// The reference content pack: registers a couple of placeholder run-dungeon rooms with Driftworks
/// core, proving the core+content contract. Real tilesets follow the same shape (richer rooms).
/// </summary>
public class SampleModSystem : ModSystem
{
    public const string ModId = "driftworkssample";

    // Run after Driftworks core (0.5) so its content registry is available.
    public override double ExecuteOrder() => 1.0;

    public override void StartServerSide(ICoreServerAPI api)
    {
        var content = api.ModLoader.GetModSystem<DriftworksContentModSystem>();
        if (content == null)
        {
            api.Logger.Warning("[{0}] Driftworks core content registry not found; no rooms registered.", ModId);
            return;
        }

        content.RegisterRoom(new RoomDef($"{ModId}:pillars", BuildPillars));
        content.RegisterRoom(new RoomDef($"{ModId}:rubble", BuildRubble));
        api.Logger.Notification("[{0}] Registered 2 placeholder rooms.", ModId);
    }

    // Four stone pillars from floor to ceiling, inset from the corners.
    private static void BuildPillars(RoomContext ctx)
    {
        int rock = ctx.BlockId("game:rock-granite");
        if (rock == 0) return;

        int[] xs = { ctx.MinX + 4, ctx.MaxX - 4 };
        int[] zs = { ctx.MinZ + 4, ctx.MaxZ - 4 };
        foreach (int x in xs)
        {
            foreach (int z in zs)
            {
                for (int y = ctx.MinY; y <= ctx.MaxY; y++)
                {
                    ctx.SetBlock(rock, x, y, z);
                }
            }
        }
    }

    // Scattered rubble piles on the floor.
    private static void BuildRubble(RoomContext ctx)
    {
        int rock = ctx.BlockId("game:rock-granite");
        if (rock == 0) return;

        for (int i = 0; i < 24; i++)
        {
            int x = ctx.MinX + ctx.Rng.NextInt(ctx.MaxX - ctx.MinX + 1);
            int z = ctx.MinZ + ctx.Rng.NextInt(ctx.MaxZ - ctx.MinZ + 1);
            int h = 1 + ctx.Rng.NextInt(2);
            for (int y = ctx.MinY; y < ctx.MinY + h; y++)
            {
                ctx.SetBlock(rock, x, y, z);
            }
        }
    }
}
