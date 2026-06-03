using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Ehm93.VS.Driftworks.Core.Internal;

/// <summary>
/// Debug command harness (/dw) over the <see cref="RunManager"/> — open/exit/close a run, list
/// live dimensions, and stress-test index recycling. The map device drives the same RunManager.
/// </summary>
internal sealed class RunSandbox
{
    private readonly ICoreServerAPI api;
    private readonly RunManager runs;

    public RunSandbox(ICoreServerAPI api, RunManager runs)
    {
        this.api = api;
        this.runs = runs;
    }

    public void RegisterCommands()
    {
        var parsers = api.ChatCommands.Parsers;
        api.ChatCommands.Create("dw")
            .WithDescription("Driftworks debug harness: ephemeral run-dimension lifecycle.")
            .RequiresPrivilege(Privilege.controlserver)
            .BeginSubCommand("open")
                .WithDescription("Open a fresh run and teleport in.")
                .HandleWith(OnOpen)
            .EndSubCommand()
            .BeginSubCommand("exit")
                .WithDescription("Return to the overworld; the run stays loaded.")
                .HandleWith(OnExit)
            .EndSubCommand()
            .BeginSubCommand("close")
                .WithDescription("Leave (if inside) and destroy your run, releasing its index.")
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
            .EndSubCommand()
            .BeginSubCommand("house")
                .WithDescription("Place a placeholder gate house (with a map device) just in front of you.")
                .HandleWith(OnHouse)
            .EndSubCommand();
    }

    private TextCommandResult OnOpen(TextCommandCallingArgs args)
    {
        if (args.Caller.Player is not IServerPlayer player) return TextCommandResult.Error("Players only.");
        runs.TryOpenRun(player, out string msg);
        return TextCommandResult.Success(msg);
    }

    private TextCommandResult OnExit(TextCommandCallingArgs args)
    {
        if (args.Caller.Player is not IServerPlayer player) return TextCommandResult.Error("Players only.");
        runs.ExitRun(player, out string msg);
        return TextCommandResult.Success(msg);
    }

    private TextCommandResult OnClose(TextCommandCallingArgs args)
    {
        if (args.Caller.Player is not IServerPlayer player) return TextCommandResult.Error("Players only.");
        runs.CloseRun(player, out string msg);
        return TextCommandResult.Success(msg);
    }

    private TextCommandResult OnHouse(TextCommandCallingArgs args)
    {
        if (args.Caller.Player is not IServerPlayer player) return TextCommandResult.Error("Players only.");
        var p = player.Entity.Pos.AsBlockPos;
        var center = new BlockPos(p.X, p.Y, p.Z + 5, 0);
        bool ok = GateHouse.Build(api.World.BlockAccessor, api.World, center);
        return TextCommandResult.Success(ok
            ? $"Placed a gate house at {center.X}, {center.Y}, {center.Z} (door faces you)."
            : "Failed to place (rock block not resolved).");
    }

    private TextCommandResult OnStatus(TextCommandCallingArgs args)
    {
        var all = runs.Manifold.Registry.All;
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

        var registry = runs.Manifold.Registry;
        int baseline = registry.All.Count;

        // Phase 1 - breadth: hold a batch open at once so the indices visibly span a range and the
        // live count rises; then release them all and confirm it falls back to baseline.
        int batch = Math.Min(count, 16);
        var held = new List<AssetLocation>();
        var batchIds = new List<int>();
        for (int i = 0; i < batch; i++)
        {
            var (dim, code, _) = runs.CreateRun(genRadius: 0);
            held.Add(code);
            batchIds.Add(dim.InternalId);
        }
        int peak = registry.All.Count;
        foreach (var code in held) registry.TryRemove(code);
        int afterBatch = registry.All.Count;

        // Phase 2 - recycle: rapid create+destroy; each should reuse the lowest freed index.
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var recycleIds = new HashSet<int>();
        for (int i = 0; i < count; i++)
        {
            var (dim, code, _) = runs.CreateRun(genRadius: 0);
            recycleIds.Add(dim.InternalId);
            registry.TryRemove(code);
        }
        sw.Stop();

        int after = registry.All.Count;
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
}
