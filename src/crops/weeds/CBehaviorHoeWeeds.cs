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

        // Only take over the interaction when there's farmland to weed (otherwise let the vanilla hoe till).
        if (FindWeedBehavior(blockSel.Position) == null) return;

        // Just start the hoe swing here; the actual weeding happens at the END of the swing
        // (OnHeldInteractStep), so weeds come out when the swing lands rather than the instant you click.
        byEntity.Attributes.SetInt("didhoeweed", 0);
        handHandling = EnumHandHandling.PreventDefault; // plays the hoe's "hoe" use animation
        handling = EnumHandling.PreventSubsequent;
    }

    public override bool OnHeldInteractStep(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, ref EnumHandling handling)
    {
        if (blockSel == null || Api == null) return false;
        var behavior = FindWeedBehavior(blockSel.Position);
        if (behavior == null) return false;

        handling = EnumHandling.PreventSubsequent;

        // Land the effect once, ~0.6s into the ~1s swing — the same point vanilla hoe tilling fires.
        if (secondsUsed > 0.6f && byEntity.Attributes.GetInt("didhoeweed") == 0)
        {
            byEntity.Attributes.SetInt("didhoeweed", 1);
            ApplyHoe(behavior, slot, byEntity, blockSel);
        }
        return secondsUsed < 1f;
    }

    private void ApplyHoe(BEBehaviorCropWeeds behavior, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel)
    {
        if (Api == null) return;

        // Knock down the mod's weed level (weeds that grow alongside a crop)...
        var lvlBefore = behavior.WeedLevel;
        behavior.WeedLevel -= HoeImpact();
        var changed = lvlBefore != behavior.WeedLevel;

        // ...and clear vanilla's fallow-farmland weed block (tallgrass/horsetail), which the mod's weed
        // level doesn't track, so hoeing works uniformly whether or not a crop is present.
        changed |= ClearVanillaWeed(behavior.Blockentity.Pos);

        if (changed)
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
    }

    private BEBehaviorCropWeeds? FindWeedBehavior(BlockPos pos)
    {
        if (Api == null) return null;
        var entity = Api.World.BlockAccessor.GetBlockEntity(pos);
        // Crops have no block entity, so when hoeing the crop itself this is null — fall through to the
        // farmland one block below rather than bailing out (the old `entity == null` early-return is what
        // stopped hoeing a weedy crop from reducing weeds).
        if (entity is not BlockEntityFarmland) entity = Api.World.BlockAccessor.GetBlockEntity(pos.DownCopy());
        if (entity is not BlockEntityFarmland) return null;
        return entity.GetBehavior<BEBehaviorCropWeeds>();
    }

    // Vanilla grows weed blocks on fallow farmland (FarmingConfig.WeedBlockCodes — tallgrass*/horsetail),
    // placed one block above it, and they grow taller over time. The mod's WeedLevel doesn't track these,
    // so clear the block directly. Server-authoritative; returns true if a weed block was removed.
    private bool ClearVanillaWeed(BlockPos farmlandPos)
    {
        if (Api == null) return false;
        var weedPos = farmlandPos.UpCopy();
        if (!IsVanillaFarmWeed(Api.World.BlockAccessor.GetBlock(weedPos))) return false;
        if (Api.Side == EnumAppSide.Server) Api.World.BlockAccessor.SetBlock(0, weedPos);
        return true;
    }

    private bool IsVanillaFarmWeed(Block? block)
    {
        if (block?.Code == null) return false;
        var codes = Api?.ModLoader.GetModSystem<ModSystemFarming>()?.Config?.WeedBlockCodes;
        if (codes == null) return false;
        // Match by family (first code part) so grown variants (tallgrass-tall, -verytall, …) count too.
        var first = block.Code.FirstCodePart();
        foreach (var cc in codes)
            if (cc.Code?.FirstCodePart() == first) return true;
        return false;
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
