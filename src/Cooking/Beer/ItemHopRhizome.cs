using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace Ehm93.VS.Cooking.Beer;

// Hop rhizome — right-click tilled farmland to plant a beer:hopplant crown. The crown renders its own 3 m
// pole for the bine to climb, and the crop draws nutrients from the farmland as it grows.
public class ItemHopRhizome : Item
{
    public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel,
        EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
    {
        IWorldAccessor world = byEntity.World;
        if (blockSel == null)
        {
            base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handling);
            return;
        }

        IBlockAccessor ba = world.BlockAccessor;
        IPlayer? plr = (byEntity as EntityPlayer)?.Player;

        // Plant INTO a replaceable cell we looked straight at (snow/sprouts on the farmland), else the cell on
        // the clicked face (normally the top of the farmland block).
        Block clicked = ba.GetBlock(blockSel.Position);
        BlockPos placePos = clicked.Replaceable >= 6000 ? blockSel.Position.Copy() : blockSel.Position.AddCopy(blockSel.Face);

        Block atPlace = ba.GetBlock(placePos);
        Block? hopplant = world.GetBlock(new AssetLocation("beer:hopplant"));

        bool spaceFree = atPlace.Replaceable >= 6000;
        bool onFarmland = ba.GetBlockEntity(placePos.DownCopy()) is BlockEntityFarmland;

        if (hopplant != null && spaceFree && onFarmland)
        {
            handling = EnumHandHandling.PreventDefault;
            if (world.Side == EnumAppSide.Server)
            {
                ba.SetBlock(hopplant.BlockId, placePos);
                ba.MarkBlockDirty(placePos);
                if (plr == null || plr.WorldData.CurrentGameMode != EnumGameMode.Creative) slot.TakeOut(1);
                world.PlaySoundAt(new AssetLocation("game", "sounds/block/plant"),
                    placePos.X + 0.5, placePos.Y, placePos.Z + 0.5, plr);
            }
            return;
        }

        // Tell the player why nothing happened, rather than failing silently.
        if (world.Side == EnumAppSide.Server && plr is IServerPlayer sp)
            sp.SendMessage(GlobalConstants.GeneralChatGroup,
                Lang.Get(onFarmland ? "beer:hoprhizome-blocked" : "beer:hoprhizome-needs-farmland"),
                EnumChatType.Notification);
        handling = EnumHandHandling.PreventDefault;
    }
}
