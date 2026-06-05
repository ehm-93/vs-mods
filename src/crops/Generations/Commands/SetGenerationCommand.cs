using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace Ehm93.VS.Crops.Generations;

internal class SetGenerationCommand
{
    private readonly ICoreServerAPI sapi;

    private SetGenerationCommand(ICoreServerAPI sapi) { this.sapi = sapi; }

    public static void Register(ICoreServerAPI sapi)
    {
        var parser = sapi.ChatCommands.Parsers;
        sapi.ChatCommands.Create("generation")
            .WithDescription("Crops Generations debug commands")
            .RequiresPrivilege(Privilege.controlserver)
            .BeginSubCommand("set")
                .WithDescription("Set the generation of the target crop")
                .RequiresPlayer()
                .WithArgs(parser.Int("gen"))
                .HandleWith(new SetGenerationCommand(sapi).Handle)
            .EndSubCommand()
            .Validate();
    }

    private TextCommandResult Handle(TextCommandCallingArgs args)
    {
        var target = args.Caller.Player.CurrentBlockSelection;
        if (target?.Block == null) return TextCommandResult.Error("No targeted block.");

        BlockEntityFarmland? farmland;
        if (target.Block is BlockCrop) farmland = sapi.World.BlockAccessor.GetBlockEntity<BlockEntityFarmland>(target.Position.DownCopy());
        else if (target.Block is BlockFarmland) farmland = sapi.World.BlockAccessor.GetBlockEntity<BlockEntityFarmland>(target.Position);
        else return TextCommandResult.Error($"Target must be BlockCrop or BlockFarmland, found {target.Block.GetType().Name}.");

        if (farmland == null) return TextCommandResult.Error("No BlockEntityFarmland at target.");
        var behavior = farmland.GetBehavior<BEBehaviorFarmlandGeneration>();
        if (behavior == null) return TextCommandResult.Error("No FarmlandGeneration behavior on farmland.");

        behavior.Generation = (int)args[0];
        return TextCommandResult.Success();
    }
}
