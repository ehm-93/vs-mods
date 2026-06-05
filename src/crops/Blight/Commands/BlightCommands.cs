using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace Ehm93.VS.Crops.Blight;

internal static class BlightCommands
{
    public static void Register(ICoreServerAPI sapi)
    {
        var parser = sapi.ChatCommands.Parsers;
        var root = sapi.ChatCommands.Create("blight")
            .WithDescription("Crops Blight debug commands")
            .RequiresPrivilege(Privilege.controlserver);

        root.BeginSubCommand("set")
            .WithDescription("Set the blight level on the targeted farmland")
            .RequiresPlayer()
            .WithArgs(parser.IntRange("blight", 0, 100))
            .HandleWith(args => SetBlight(sapi, args))
            .EndSubCommand();

        root.BeginSubCommand("spores")
            .WithDescription("Set the spore level on the targeted farmland")
            .RequiresPlayer()
            .WithArgs(parser.IntRange("spores", 0, 100))
            .HandleWith(args => SetSpores(sapi, args))
            .EndSubCommand();

        root.Validate();
    }

    private static TextCommandResult SetBlight(ICoreServerAPI sapi, TextCommandCallingArgs args)
    {
        var farmland = ResolveFarmland(sapi, args, out var err);
        if (farmland == null) return TextCommandResult.Error(err);
        var behavior = farmland.GetBehavior<BEBehaviorFarmlandBlight>();
        if (behavior == null) return TextCommandResult.Error("No FarmlandBlight behavior on farmland.");
        behavior.BlightLevel = (int)args[0];
        return TextCommandResult.Success();
    }

    private static TextCommandResult SetSpores(ICoreServerAPI sapi, TextCommandCallingArgs args)
    {
        var farmland = ResolveFarmland(sapi, args, out var err);
        if (farmland == null) return TextCommandResult.Error(err);
        var behavior = farmland.GetBehavior<BEBehaviorFarmlandBlight>();
        if (behavior == null) return TextCommandResult.Error("No FarmlandBlight behavior on farmland.");
        behavior.SporeLevel = (int)args[0];
        return TextCommandResult.Success();
    }

    private static BlockEntityFarmland? ResolveFarmland(ICoreServerAPI sapi, TextCommandCallingArgs args, out string err)
    {
        err = "";
        var target = args.Caller.Player.CurrentBlockSelection;
        if (target?.Block == null) { err = "No targeted block."; return null; }

        BlockEntityFarmland? farmland;
        if (target.Block is BlockCrop) farmland = sapi.World.BlockAccessor.GetBlockEntity<BlockEntityFarmland>(target.Position.DownCopy());
        else if (target.Block is BlockFarmland) farmland = sapi.World.BlockAccessor.GetBlockEntity<BlockEntityFarmland>(target.Position);
        else { err = $"Target must be BlockCrop or BlockFarmland, found {target.Block.GetType().Name}."; return null; }

        if (farmland == null) { err = "No BlockEntityFarmland at target."; return null; }
        return farmland;
    }
}
