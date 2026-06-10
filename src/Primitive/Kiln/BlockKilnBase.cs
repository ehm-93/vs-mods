using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Ehm93.VS.Primitive.Kiln;

// The placeable kiln-base block. All real logic lives in BlockEntityKilnBase; this routes interactions
// there and implements the bloomery-style hold-a-torch ignition (IIgnitable + the held CanIgnite flow).
// IMultiBlockBlockBreaking: the shell's multiblock filler blocks forward their breaking here — breaking any
// part of the shell deconstructs the kiln (refunds via the BE) WITHOUT also dropping the controller block,
// which is what the default filler forwarding would do.
public class BlockKilnBase : Block, IIgnitable, IMultiBlockBlockBreaking
{
    public void MBOnBlockBroken(IWorldAccessor world, BlockPos pos, Vec3i offset, IPlayer byPlayer, float dropQuantityMultiplier = 1f)
    {
        BlockPos controllerPos = pos.AddCopy(offset);
        (world.BlockAccessor.GetBlockEntity(controllerPos) as BlockEntityKilnBase)?.OnKilnBroken();
    }

    public float MBOnGettingBroken(IPlayer player, BlockSelection blockSel, ItemSlot itemslot, float remainingResistance, float dt, int counter, Vec3i offsetInv)
        => OnGettingBroken(player, blockSel, itemslot, remainingResistance, dt, counter);

    public int MBGetRandomColor(ICoreClientAPI capi, BlockPos pos, BlockFacing facing, int rndIndex, Vec3i offsetInv)
        => GetRandomColor(capi, pos.AddCopy(offsetInv), facing, rndIndex);

    public int MBGetColorWithoutTint(ICoreClientAPI capi, BlockPos pos, Vec3i offsetInv)
        => GetColorWithoutTint(capi, pos.AddCopy(offsetInv));
    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is BlockEntityKilnBase be
            && be.OnInteract(byPlayer))
        {
            return true;
        }
        return base.OnBlockInteractStart(world, byPlayer, blockSel);
    }

    // Mirrors BlockBloomery: hold an igniter ~4s, then the BE decides.
    public EnumIgniteState OnTryIgniteBlock(EntityAgent byEntity, BlockPos pos, float secondsIgniting)
    {
        var be = byEntity.World.BlockAccessor.GetBlockEntity(pos) as BlockEntityKilnBase;
        if (be == null || !be.CanIgnite()) return EnumIgniteState.NotIgnitablePreventDefault;
        return secondsIgniting > 4f ? EnumIgniteState.IgniteNow : EnumIgniteState.Ignitable;
    }

    public void OnTryIgniteBlockOver(EntityAgent byEntity, BlockPos pos, float secondsIgniting, ref EnumHandling handling)
    {
        handling = EnumHandling.PreventDefault;
        (byEntity.World.BlockAccessor.GetBlockEntity(pos) as BlockEntityKilnBase)?.TryIgnite();
    }

    EnumIgniteState IIgnitable.OnTryIgniteStack(EntityAgent byEntity, BlockPos pos, ItemSlot slot, float secondsIgniting)
        => EnumIgniteState.NotIgnitable;

    // A burning kiln glows like the vanilla ember block (lightHsv [0,7,8]); dimmer while cooling. Same
    // BE-driven pattern as BlockCoalPile — MarkDirty on the state change triggers the relight.
    private static readonly byte[] LitLight = { 0, 7, 8 };
    private static readonly byte[] CoolingLight = { 0, 7, 4 };

    public override byte[] GetLightHsv(IBlockAccessor blockAccessor, BlockPos pos, ItemStack? stack = null)
    {
        if (pos != null && blockAccessor.GetBlockEntity(pos) is BlockEntityKilnBase be)
        {
            switch (be.StateForLight)
            {
                case BlockEntityKilnBase.KilnState.Lit: return LitLight;
                case BlockEntityKilnBase.KilnState.Cooling: return CoolingLight;
            }
        }
        return base.GetLightHsv(blockAccessor, pos, stack);
    }

    public override void OnBlockBroken(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1f)
    {
        // Refund bricks/fuel and release the other bases before the BE disappears.
        (world.BlockAccessor.GetBlockEntity(pos) as BlockEntityKilnBase)?.OnKilnBroken();
        base.OnBlockBroken(world, pos, byPlayer, dropQuantityMultiplier);
    }
}
