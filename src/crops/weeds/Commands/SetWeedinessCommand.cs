using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace Ehm93.VS.Crops.Weeds;

internal class SetWeedinessCommand
{
    private readonly ICoreServerAPI sapi;

    private SetWeedinessCommand(ICoreServerAPI sapi)
    {
        this.sapi = sapi;
    }

    public static void Register(ICoreServerAPI sapi)
    {
        var parser = sapi.ChatCommands.Parsers;
        sapi.ChatCommands.Create("cropsweeds")
            .WithDescription("Crops Weeds debug commands")
            .RequiresPrivilege(Privilege.controlserver)
            .BeginSubCommand("set-weediness")
                .WithAlias("weed")
                .WithDescription("Set the weediness of the target farmland")
                .RequiresPrivilege(Privilege.controlserver)
                .RequiresPlayer()
                .WithArgs(parser.IntRange("weediness", 0, 100))
                .HandleWith(new SetWeedinessCommand(sapi).Handle)
            .EndSubCommand()
            .Validate();
    }

    private TextCommandResult Handle(TextCommandCallingArgs args)
    {
        var caller = args.Caller.Player;
        var target = caller.CurrentBlockSelection;
        if (target?.Block == null) return TextCommandResult.Error("No targeted block.");

        BlockEntityFarmland? farmland;
        if (target.Block is BlockCrop) farmland = sapi.World.BlockAccessor.GetBlockEntity<BlockEntityFarmland>(target.Position.DownCopy());
        else if (target.Block is BlockFarmland) farmland = sapi.World.BlockAccessor.GetBlockEntity<BlockEntityFarmland>(target.Position);
        else return TextCommandResult.Error($"Target must be BlockCrop or BlockFarmland, found {target.Block.GetType().Name}.");

        if (farmland == null) return TextCommandResult.Error("No BlockEntityFarmland found.");

        var behavior = farmland.GetBehavior<BEBehaviorCropWeeds>();
        if (behavior == null) return TextCommandResult.Error("No CropWeeds behavior on farmland.");

        behavior.WeedLevel = (int)args[0];
        return TextCommandResult.Success();
    }
}
