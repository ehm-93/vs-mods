using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Util;

namespace Ehm93.VS.Cooking.Beer;

// The planted hop crown + BlockEntityHopPlant growth. A non-colliding 3 m pole renders automatically as soon
// as the crown is planted; the bine climbs it over the seasons. Empty-hand right-click when ripe to harvest.
public class BlockHopPlant : Block
{
    private WorldInteraction[] harvestInteraction = Array.Empty<WorldInteraction>();

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        harvestInteraction = new[]
        {
            new WorldInteraction { ActionLangCode = "beer:blockhelp-hopplant-harvest", MouseButton = EnumMouseButton.Right }
        };
    }

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        ItemSlot hand = byPlayer.InventoryManager.ActiveHotbarSlot;
        var be = world.BlockAccessor.GetBlockEntity<BlockEntityHopPlant>(blockSel.Position);
        if (be == null) return base.OnBlockInteractStart(world, byPlayer, blockSel);

        // Empty hand + ripe -> harvest.
        if (hand.Itemstack == null && be.IsRipe)
        {
            if (world.Side == EnumAppSide.Server) be.Harvest(byPlayer);
            return true;
        }

        return base.OnBlockInteractStart(world, byPlayer, blockSel);
    }

    public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
    {
        var be = world.BlockAccessor.GetBlockEntity<BlockEntityHopPlant>(selection.Position);
        WorldInteraction[] mine = (be != null && be.IsRipe) ? harvestInteraction : Array.Empty<WorldInteraction>();
        return mine.Append(base.GetPlacedBlockInteractionHelp(world, selection, forPlayer));
    }
}
