using System;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Ehm93.VS.Crops.Weeds;

public class CBehaviorHoeWeeds : CollectibleBehavior
{
    protected ICoreAPI? Api;

    public ItemHoe Hoe => (ItemHoe)collObj;

    public CBehaviorHoeWeeds(CollectibleObject collObj) : base(collObj)
    {
        if (collObj is not ItemHoe) throw new ArgumentException("Configuration error! HoeWeeds behavior may only be used on hoes!");
    }

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        Api = api;
    }

    public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handHandling, ref EnumHandling handling)
    {
        base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handHandling, ref handling);

        if (blockSel == null || Api == null) return;

        var behavior = FindWeedBehavior(blockSel.Position);
        if (behavior == null) return;

        var lvlBefore = behavior.WeedLevel;
        behavior.WeedLevel -= HoeImpact();
        if (lvlBefore != behavior.WeedLevel)
        {
            var player = (byEntity as EntityPlayer)?.Player;
            Api.World.PlaySoundAt(
                new AssetLocation("cropsweeds:sounds/weeds/hoe"),
                blockSel.Position.X + 0.5, blockSel.Position.Y + 0.5, blockSel.Position.Z + 0.5,
                player,
                randomizePitch: true,
                range: 8
            );
            if (player != null)
            {
                slot.Itemstack.Collectible.DamageItem(byEntity.World, byEntity, player.InventoryManager.ActiveHotbarSlot);
            }
            if (slot.Empty)
            {
                byEntity.World.PlaySoundAt(new AssetLocation("game:sounds/effect/toolbreak"), byEntity.Pos.X, byEntity.Pos.InternalY, byEntity.Pos.Z);
            }
        }

        handling = EnumHandling.PreventSubsequent;
        handHandling = EnumHandHandling.PreventDefault;
    }

    private BEBehaviorCropWeeds? FindWeedBehavior(BlockPos pos)
    {
        if (Api == null) return null;
        var entity = Api.World.BlockAccessor.GetBlockEntity(pos);
        if (entity == null) return null;
        if (entity is not BlockEntityFarmland) entity = Api.World.BlockAccessor.GetBlockEntity(pos.DownCopy());
        if (entity is not BlockEntityFarmland) return null;
        return entity.GetBehavior<BEBehaviorCropWeeds>();
    }

    private double HoeImpact()
    {
        return Hoe.Code.EndVariant() switch
        {
            "flint" => 15,
            "obsidian" => 15,
            "copper" => 20,
            "tinbronze" => 25,
            "bismuthbronze" => 25,
            "blackbronze" => 25,
            "iron" => 35,
            "meteoriciron" => 35,
            "steel" => 50,
            _ => 10
        };
    }
}
