using System;
using System.Collections.Generic;
using System.Linq;
using Manifold.Api;
using Manifold.Api.Server;
using Manifold.Api.Transitions;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Ehm93.VS.Driftworks.Core.Internal;

/// <summary>
/// Build-step 1 debug harness. Proves the ephemeral run-dimension round trip on top of Manifold —
/// open, enter, exit, and release the dimension index — and that the index recycles, ahead of the
/// real run controller. This is throwaway scaffolding to validate the foundation, not the device.
///
/// Each run gets a unique code and a unique, monotonic XZ origin, so a recycled dimension index
/// never lands on a previous run's leftover chunks (we deliberately do not purge them — VS saves
/// are large and the orphans double as a debugging archive; see the design brief).
/// </summary>
internal sealed class RunSandbox
{
    private const int ChunkSize = 32;
    private const int RunSpacingChunks = 64;  // 2048 blocks between run origins — comfortably non-overlapping
    private const int FloorHeight = 4;

    private static readonly AssetLocation Overworld = new("manifold", "overworld");

    private readonly ICoreServerAPI api;
    private readonly IManifoldServer manifold;

    private int runCounter;
    private readonly Dictionary<string, RunHandle> active = new();

    public RunSandbox(ICoreServerAPI api, IManifoldServer manifold)
    {
        this.api = api;
        this.manifold = manifold;
    }

    public void RegisterCommands()
    {
        var parsers = api.ChatCommands.Parsers;
        api.ChatCommands.Create("dw")
            .WithDescription("Driftworks debug harness: ephemeral run-dimension lifecycle.")
            .RequiresPrivilege(Privilege.controlserver)
            .BeginSubCommand("open")
                .WithDescription("Open a fresh ephemeral run and teleport in.")
                .HandleWith(OnOpen)
            .EndSubCommand()
            .BeginSubCommand("exit")
                .WithDescription("Return to the overworld; the run stays loaded.")
                .HandleWith(OnExit)
            .EndSubCommand()
            .BeginSubCommand("close")
                .WithDescription("Leave (if inside) and destroy your run, releasing its dimension index.")
                .HandleWith(OnClose)
            .EndSubCommand()
            .BeginSubCommand("status")
                .WithDescription("List live Manifold dimensions (code, id, lifetime/state, owner).")
                .HandleWith(OnStatus)
            .EndSubCommand()
            .BeginSubCommand("cycle")
                .WithDescription("Stress test: open+destroy N ephemeral dims; prove the index recycles and live count returns to baseline.")
                .WithArgs(parsers.OptionalInt("count", 100))
                .HandleWith(OnCycle)
            .EndSubCommand();
    }

    private TextCommandResult OnOpen(TextCommandCallingArgs args)
    {
        if (args.Caller.Player is not IServerPlayer player)
        {
            return TextCommandResult.Error("Players only.");
        }

        if (active.ContainsKey(player.PlayerUID))
        {
            return TextCommandResult.Error("You already have a run open. /dw close it first.");
        }

        var pos = player.Entity.Pos;
        var returnPos = new BlockPos((int)pos.X, (int)pos.Y, (int)pos.Z, 0);

        var (dim, code, spawn) = CreateRun(genRadius: 2);
        active[player.PlayerUID] = new RunHandle(code, returnPos);

        manifold.Transitions.TeleportPlayer(player, code);
        return TextCommandResult.Success(
            $"Opened {code} -> dim id {dim.InternalId}, spawn ({spawn.X},{spawn.Y},{spawn.Z}). " +
            $"Live dims: {manifold.Registry.All.Count}. Use /dw exit or /dw close.");
    }

    private TextCommandResult OnExit(TextCommandCallingArgs args)
    {
        if (args.Caller.Player is not IServerPlayer player)
        {
            return TextCommandResult.Error("Players only.");
        }

        if (!active.TryGetValue(player.PlayerUID, out var handle))
        {
            return TextCommandResult.Error("You have no open run.");
        }

        manifold.Transitions.TeleportPlayer(player, Overworld,
            new TransitionOptions { OverridePosition = handle.ReturnPos });
        return TextCommandResult.Success($"Left {handle.Code} (still loaded). /dw close to destroy it and release its index.");
    }

    private TextCommandResult OnClose(TextCommandCallingArgs args)
    {
        if (args.Caller.Player is not IServerPlayer player)
        {
            return TextCommandResult.Error("Players only.");
        }

        if (!active.TryGetValue(player.PlayerUID, out var handle))
        {
            return TextCommandResult.Error("You have no open run.");
        }

        var dim = manifold.Registry.Get(handle.Code);
        int id = dim?.InternalId ?? -1;

        // Get the player out of the run before destroying it.
        if (player.Entity.Pos.Dimension == id)
        {
            manifold.Transitions.TeleportPlayer(player, Overworld,
                new TransitionOptions { OverridePosition = handle.ReturnPos });
        }

        bool removed = manifold.Registry.TryRemove(handle.Code);
        active.Remove(player.PlayerUID);

        return removed
            ? TextCommandResult.Success($"Destroyed {handle.Code}; released dim index {id}. Live dims: {manifold.Registry.All.Count}.")
            : TextCommandResult.Error($"{handle.Code} was not found (already gone?). Cleared your handle.");
    }

    private TextCommandResult OnStatus(TextCommandCallingArgs args)
    {
        var all = manifold.Registry.All;
        var lines = new List<string> { $"Live Manifold dimensions: {all.Count}" };
        foreach (var d in all)
        {
            lines.Add($"  {d.Code}  id={d.InternalId}  {d.Lifetime}/{d.State}  owner={d.OwnerModId}");
        }
        return Report(args, lines);
    }

    private TextCommandResult OnCycle(TextCommandCallingArgs args)
    {
        int count = args.Parsers[0].GetValue() is int c ? c : 100;
        count = Math.Clamp(count, 1, 5000);

        int baseline = manifold.Registry.All.Count;

        // Phase 1 - breadth: hold a batch open at once, so the indices visibly span a range and the
        // live count rises; then release them all and confirm it falls back to baseline.
        int batch = Math.Min(count, 16);
        var held = new List<AssetLocation>();
        var batchIds = new List<int>();
        for (int i = 0; i < batch; i++)
        {
            var (dim, code, _) = CreateRun(genRadius: 0);
            held.Add(code);
            batchIds.Add(dim.InternalId);
        }
        int peak = manifold.Registry.All.Count;
        foreach (var code in held) manifold.Registry.TryRemove(code);
        int afterBatch = manifold.Registry.All.Count;

        // Phase 2 - recycle: rapid create+destroy; each should reuse the lowest freed index.
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var recycleIds = new HashSet<int>();
        for (int i = 0; i < count; i++)
        {
            var (dim, code, _) = CreateRun(genRadius: 0);
            recycleIds.Add(dim.InternalId);
            manifold.Registry.TryRemove(code);
        }
        sw.Stop();

        int after = manifold.Registry.All.Count;
        bool baselineHeld = after == baseline;
        bool recycled = recycleIds.Count == 1;
        bool breadthOk = batchIds.Count == batchIds.Distinct().Count() && afterBatch == baseline;

        string recycleLine = recycled
            ? $"reused {recycleIds.First()} every time"
            : $"varied across {string.Join(", ", recycleIds.OrderBy(x => x))}";

        return Report(args, new[]
        {
            $"Cycled {count} ephemeral dims (plus {batch} held concurrently).",
            $"  breadth: {batch} live at once -> ids [{string.Join(", ", batchIds)}], peak count {peak}, released back to {afterBatch}",
            $"  recycle: {count} create+destroy in {sw.ElapsedMilliseconds} ms; index {recycleLine}",
            $"  live dims: baseline {baseline} -> after {after} {(baselineHeld ? "(OK)" : "(LEAK!)")}",
            $"  verdict: {(recycled && baselineHeld && breadthOk ? "PASS - indices allocate, recycle, and free cleanly" : "INVESTIGATE")}",
        });
    }

    // VS's in-game chat HUD doesn't render a multi-line command result (the full text still
    // reaches client-chat.log). Send each line to the player so it shows in chat; fall back to a
    // joined single result when there is no player (e.g. run from the server console).
    private static TextCommandResult Report(TextCommandCallingArgs args, IReadOnlyList<string> lines)
    {
        if (args.Caller.Player is IServerPlayer player)
        {
            foreach (var line in lines)
            {
                player.SendMessage(args.Caller.FromChatGroupId, line, EnumChatType.Notification);
            }
            return TextCommandResult.Success();
        }
        return TextCommandResult.Success(string.Join("\n", lines));
    }

    private (IDimension dim, AssetLocation code, BlockPos spawn) CreateRun(int genRadius)
    {
        int n = ++runCounter;
        var code = new AssetLocation(CoreModSystem.ModId, "run_" + n);
        int originX = n * RunSpacingChunks * ChunkSize;  // unique, monotonic, non-overlapping
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
