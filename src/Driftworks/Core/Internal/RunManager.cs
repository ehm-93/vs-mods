using System.Collections.Generic;
using Manifold.Api;
using Manifold.Api.Server;
using Manifold.Api.Transitions;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Ehm93.VS.Driftworks.Core.Internal;

/// <summary>
/// The run controller: owns the lifecycle of a player's ephemeral run dimension (open, exit,
/// close) on top of Manifold. Build-step 1 proved the underlying round trip and index recycling;
/// this is the reusable service the map device and the /dw debug commands both drive.
///
/// Each run gets a unique code and a unique, monotonic XZ origin so a recycled dimension index
/// never inherits a previous run's leftover chunks (we deliberately don't purge them).
/// </summary>
internal sealed class RunManager
{
    private const int ChunkSize = 32;
    private const int RunSpacingChunks = 64;  // 2048 blocks between run origins
    private const int FloorHeight = 4;

    private static readonly AssetLocation Overworld = new("manifold", "overworld");

    private readonly ICoreServerAPI api;
    private readonly IManifoldServer manifold;

    private int runCounter;
    private readonly Dictionary<string, RunHandle> active = new();

    public RunManager(ICoreServerAPI api, IManifoldServer manifold)
    {
        this.api = api;
        this.manifold = manifold;
    }

    /// <summary>Manifold facade (used by the debug status/cycle commands).</summary>
    public IManifoldServer Manifold => manifold;

    /// <summary>Whether the player currently has a run open (loaded).</summary>
    public bool HasRun(string playerUid) => active.ContainsKey(playerUid);

    /// <summary>Open a fresh run for the player and teleport them in. False if they already have one.</summary>
    public bool TryOpenRun(IServerPlayer player, out string message)
    {
        if (active.ContainsKey(player.PlayerUID))
        {
            message = "You already have a run open. Leave or close it first.";
            return false;
        }

        var pos = player.Entity.Pos;
        var returnPos = new BlockPos((int)pos.X, (int)pos.Y, (int)pos.Z, 0);

        var (dim, code, _) = CreateRun(genRadius: 2);
        active[player.PlayerUID] = new RunHandle(code, returnPos);

        manifold.Transitions.TeleportPlayer(player, code);
        message = $"Entered a run ({code}, dim id {dim.InternalId}).";
        return true;
    }

    /// <summary>Return the player to the overworld where they entered; the run stays loaded.</summary>
    public bool ExitRun(IServerPlayer player, out string message)
    {
        if (!active.TryGetValue(player.PlayerUID, out var handle))
        {
            message = "You have no open run.";
            return false;
        }

        manifold.Transitions.TeleportPlayer(player, Overworld,
            new TransitionOptions { OverridePosition = handle.ReturnPos });
        message = "Returned to the overworld; the run is still loaded.";
        return true;
    }

    /// <summary>Pull the player out (if inside) and destroy their run, releasing its dimension index.</summary>
    public bool CloseRun(IServerPlayer player, out string message)
    {
        if (!active.TryGetValue(player.PlayerUID, out var handle))
        {
            message = "You have no open run.";
            return false;
        }

        var dim = manifold.Registry.Get(handle.Code);
        int id = dim?.InternalId ?? -1;
        if (player.Entity.Pos.Dimension == id)
        {
            manifold.Transitions.TeleportPlayer(player, Overworld,
                new TransitionOptions { OverridePosition = handle.ReturnPos });
        }

        bool removed = manifold.Registry.TryRemove(handle.Code);
        active.Remove(player.PlayerUID);
        message = removed
            ? $"Destroyed {handle.Code}; released dim index {id}."
            : $"{handle.Code} was not found (already gone?).";
        return removed;
    }

    /// <summary>
    /// Create (register + generate) a fresh ephemeral run dimension at a unique, monotonic XZ origin.
    /// Returns the dimension, its code, and the fixed spawn. Does NOT teleport anyone.
    /// </summary>
    public (IDimension dim, AssetLocation code, BlockPos spawn) CreateRun(int genRadius)
    {
        int n = ++runCounter;
        var code = new AssetLocation(CoreModSystem.ModId, "run_" + n);
        int originX = n * RunSpacingChunks * ChunkSize;
        var spawn = new BlockPos(originX + 16, FloorHeight + 2, 16, 0);

        var dim = manifold.Registry.Define(code)
            .Ephemeral()
            .WithWorldgen(new FlatFloorWorldgen(FloorHeight))
            .WithGenerationRadius(genRadius)
            .WithFixedSpawn(spawn)
            .Create();

        return (dim, code, spawn);
    }

    private readonly record struct RunHandle(AssetLocation Code, BlockPos ReturnPos);
}
