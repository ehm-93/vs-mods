using System.Collections.Generic;
using Manifold.Api;
using Manifold.Api.Server;
using Manifold.Api.Transitions;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Ehm93.VS.Driftworks.Core.Internal;

/// <summary>
/// The run controller: owns the lifecycle of a player's ephemeral run dimension (open, exit, close)
/// on top of Manifold, generating the run's dungeon from content-registered rooms via
/// <see cref="PrefabWorldgen"/>. The map device and the /dw debug commands both drive it.
///
/// The run counter is persisted, so every run EVER gets a unique XZ origin (laid on a 2D grid). That
/// matters because we don't purge a closed run's chunks (accepted leak) and Manifold won't regenerate
/// already-generated columns — a repeated origin would surface a previous run's stale geometry.
/// </summary>
internal sealed class RunManager
{
    private const int ChunkSize = 32;
    private const int RunSpacingChunks = 64;   // 2048 blocks between run centres
    private const int RunGridWidth = 256;      // runs tile a 2D grid so origins never march off the world edge
    private const int ManifoldMinDim = 10;     // Manifold allocates mod dimension indices from 10 up
    private const string CounterKey = "driftworkscore:runCounter";

    private static readonly AssetLocation Overworld = new("manifold", "overworld");
    private static readonly TransitionOptions ToOverworld = new() { SpawnBehavior = SpawnBehavior.LastVisited };

    private readonly ICoreServerAPI api;
    private readonly IManifoldServer manifold;
    private readonly DriftworksContentModSystem content;

    private int runCounter;
    private bool counterLoaded;
    private readonly Dictionary<string, AssetLocation> active = new();

    public RunManager(ICoreServerAPI api, IManifoldServer manifold, DriftworksContentModSystem content)
    {
        this.api = api;
        this.manifold = manifold;
        this.content = content;
        api.Event.PlayerNowPlaying += OnPlayerNowPlaying;
    }

    /// <summary>Manifold facade (used by the debug status/cycle commands).</summary>
    public IManifoldServer Manifold => manifold;

    /// <summary>Whether the player currently has a run open (loaded).</summary>
    public bool HasRun(string playerUid) => active.ContainsKey(playerUid);

    /// <summary>Open a fresh run for the player and teleport them in. False if they already have one.</summary>
    public bool TryOpenRun(IServerPlayer player, out string message)
    {
        if (active.ContainsKey(player.PlayerUID) || player.Entity.Pos.Dimension != 0)
        {
            message = "You already have a run open. Leave or close it first.";
            return false;
        }

        var (dim, code, spawn) = CreateRun(genRadius: 2);
        active[player.PlayerUID] = code;
        manifold.Transitions.TeleportPlayer(player, code);

        // Manifold's own post-gen relight targets dimension 0 (a 3-arg BlockPos(x,0,z) decodes to
        // dim 0) and doesn't send to clients, so the run renders dark. Relight it ourselves — correct
        // dimension, sent to clients — once the client has the run's chunks from the transit.
        int ccx = spawn.X / ChunkSize;
        int ccz = spawn.Z / ChunkSize;
        api.Event.RegisterCallback(_ => RelightDungeon(dim.InternalId, ccx, ccz), 200);

        message = $"Entered a run ({code}, dim id {dim.InternalId}).";
        return true;
    }

    /// <summary>Return the player to the overworld where they entered; the run stays loaded.</summary>
    public bool ExitRun(IServerPlayer player, out string message)
    {
        if (player.Entity.Pos.Dimension == 0)
        {
            message = "You're not in a run.";
            return false;
        }

        SendToOverworld(player);
        message = "Returned to the overworld; the run stays loaded (use /dw close to destroy it).";
        return true;
    }

    /// <summary>Pull the player out (if inside) and destroy their run, releasing its dimension index.</summary>
    public bool CloseRun(IServerPlayer player, out string message)
    {
        bool inRun = player.Entity.Pos.Dimension != 0;
        AssetLocation? code = active.TryGetValue(player.PlayerUID, out var c)
            ? c
            : FindRunCode(player.Entity.Pos.Dimension);

        if (inRun) SendToOverworld(player);

        bool removed = false;
        if (code != null)
        {
            try { removed = manifold.Registry.TryRemove(code); }
            catch { /* not removable / already gone */ }
        }
        active.Remove(player.PlayerUID);

        if (!inRun && code == null)
        {
            message = "You have no open run.";
            return false;
        }
        message = removed
            ? $"Destroyed your run ({code}); back in the overworld."
            : "Returned to the overworld (the run was already gone).";
        return true;
    }

    /// <summary>
    /// Create (register + generate) a fresh ephemeral run dimension at a globally-unique XZ origin,
    /// its dungeon generated from the registered rooms. Returns the dimension, code, and spawn.
    /// Does NOT teleport anyone.
    /// </summary>
    public (IDimension dim, AssetLocation code, BlockPos spawn) CreateRun(int genRadius)
    {
        EnsureCounterLoaded();
        int n = ++runCounter;
        api.WorldManager.SaveGame.StoreData(CounterKey, runCounter);

        var code = new AssetLocation(CoreModSystem.ModId, "run_" + n);
        int centerCx = (n % RunGridWidth) * RunSpacingChunks;
        int centerCz = (n / RunGridWidth) * RunSpacingChunks;
        var spawn = new BlockPos(centerCx * ChunkSize + 16, PrefabWorldgen.FloorY + 2, centerCz * ChunkSize + 16, 0);

        var dim = manifold.Registry.Define(code)
            .Ephemeral()
            .WithWorldgen(new PrefabWorldgen(content.Rooms, centerCx, centerCz))
            .WithGenerationRadius(genRadius)
            .WithRelightHeight(PrefabWorldgen.RelightTop)  // relight above the sealed dungeon so torch light fills it
            .WithFixedSpawn(spawn)
            .Create();

        return (dim, code, spawn);
    }

    // Relight the run's dungeon volume in its own dimension and send the result to clients. The
    // 4-arg BlockPos sets the dimension directly (a 3-arg BlockPos(x,0,z) would decode to dim 0,
    // which is Manifold's relight bug). Best-effort — never block a run on a lighting hiccup.
    private void RelightDungeon(int dimId, int centerCx, int centerCz)
    {
        int minX = (centerCx - PrefabWorldgen.GridRadius) * ChunkSize;
        int minZ = (centerCz - PrefabWorldgen.GridRadius) * ChunkSize;
        int maxX = ((centerCx + PrefabWorldgen.GridRadius + 1) * ChunkSize) - 1;
        int maxZ = ((centerCz + PrefabWorldgen.GridRadius + 1) * ChunkSize) - 1;
        try
        {
            api.WorldManager.FullRelight(
                new BlockPos(minX, 0, minZ, dimId),
                new BlockPos(maxX, PrefabWorldgen.RelightTop, maxZ, dimId),
                sendToClients: true);
        }
        catch { /* relight is best-effort */ }
    }

    private void EnsureCounterLoaded()
    {
        if (counterLoaded) return;
        runCounter = api.WorldManager.SaveGame.GetData<int>(CounterKey, 0);
        counterLoaded = true;
    }

    private AssetLocation? FindRunCode(int dimId)
    {
        if (dimId == 0) return null;
        foreach (var d in manifold.Registry.All)
        {
            if (d.InternalId == dimId && d.Code.Domain == CoreModSystem.ModId) return d.Code;
        }
        return null;
    }

    // Get the player to the overworld. Prefer Manifold's transit (returns to their last overworld
    // position); if that doesn't take — e.g. the ephemeral run dimension was forgotten after a reload
    // — fall back to the engine primitive plus the player's spawn point.
    private void SendToOverworld(IServerPlayer player)
    {
        try { manifold.Transitions.TeleportPlayer(player, Overworld, ToOverworld); }
        catch { /* fall through to the engine path */ }

        if (player.Entity.Pos.Dimension != 0)
        {
            player.Entity.ChangeDimension(0);
            var sp = player.GetSpawnPosition(false);
            player.Entity.TeleportTo(new Vec3d(sp.X, sp.Y, sp.Z));
        }
    }

    // On join: if the player is in a Manifold dimension index that no longer exists (an ephemeral run
    // forgotten across a restart), rescue them to the overworld so they aren't stranded.
    private void OnPlayerNowPlaying(IServerPlayer player)
    {
        if (player.Entity?.Pos == null) return;
        int dimId = player.Entity.Pos.Dimension;
        if (dimId < ManifoldMinDim) return;  // overworld / mini / alt-world — not one of ours

        foreach (var d in manifold.Registry.All)
        {
            if (d.InternalId == dimId) return;  // a live dimension — leave them in it
        }

        SendToOverworld(player);
        active.Remove(player.PlayerUID);
        player.SendMessage(GlobalConstants.GeneralChatGroup,
            "Driftworks: your run ended while you were away — returned you to the overworld.", EnumChatType.Notification);
    }
}
