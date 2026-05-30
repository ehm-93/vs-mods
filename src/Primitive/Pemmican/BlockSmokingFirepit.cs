using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace Ehm93.VS.Primitive.Pemmican;

// A firepit with a smoking rack on it, as a single block. Right-click with firewood to fuel it, with raw
// meat to hang it on the rack, or with a torch/firestarter to light it. Empty-handed: takes a piece off
// the rack, or — when the rack is empty — pops the rack back off, returning a plain firepit + the rack.
public class BlockSmokingFirepit : Block, IIgnitable
{
    WorldInteraction[]? interactions;

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);

        interactions = new[]
        {
            new WorldInteraction
            {
                ActionLangCode = "pemmican:blockhelp-smokingfirepit-meat",
                MouseButton = EnumMouseButton.Right,
                Itemstacks = MeatStacks(api.World)
            },
            new WorldInteraction
            {
                ActionLangCode = "pemmican:blockhelp-smokingfirepit-fuel",
                MouseButton = EnumMouseButton.Right,
                Itemstacks = FuelStacks(api.World)
            },
            new WorldInteraction
            {
                ActionLangCode = "pemmican:blockhelp-smokingfirepit-take",
                MouseButton = EnumMouseButton.Right
            }
        };
    }

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        BlockEntitySmokingFirepit? be = world.BlockAccessor.GetBlockEntity<BlockEntitySmokingFirepit>(blockSel.Position);
        if (be == null) return base.OnBlockInteractStart(world, byPlayer, blockSel);

        ItemSlot hand = byPlayer.InventoryManager.ActiveHotbarSlot;
        ItemStack? held = hand.Itemstack;

        // Hang meat on the rack.
        if (held != null && be.IsAcceptedMeat(held))
        {
            if (world.Side == EnumAppSide.Server) be.TryAddMeat(hand);
            return true;
        }

        // Add fuel.
        if (held != null && be.IsFuelItem(held))
        {
            if (world.Side == EnumAppSide.Server) be.TryAddFuel(hand);
            return true;
        }

        // Let torches / firestarters light it (their own interaction calls OnTryIgniteBlock).
        if (held?.Block != null && held.Block.HasBehavior<BlockBehaviorCanIgnite>() && be.GetIgnitableState(0f) == EnumIgniteState.Ignitable)
        {
            return false;
        }

        // Empty hand: take a piece off the rack, or detach the rack when it's empty.
        if (held == null)
        {
            if (world.Side == EnumAppSide.Server)
            {
                if (be.RackIsEmpty()) DetachRack(world, blockSel.Position, byPlayer, be);
                else be.TryTakeMeat(byPlayer);
            }
            return true;
        }

        return false;
    }

    // Swap this block back to a plain firepit (same burn state), restore its fuel/fire, and return the rack.
    void DetachRack(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, BlockEntitySmokingFirepit be)
    {
        string burnstate = Variant["burnstate"];
        Block? firepit = world.GetBlock(new AssetLocation("game", "firepit-" + burnstate));
        if (firepit == null) return;

        // Snapshot fire state and pull the fuel out before the block entity is replaced.
        bool wasBurning = be.fuelBurnTime > 0f;
        int maxTemperature = be.maxTemperature;
        bool canIgniteFuel = be.canIgniteFuel;
        double extinguishedTotalHours = be.extinguishedTotalHours;
        ItemStack? fuel = be.TakeOutFuel();

        world.BlockAccessor.SetBlock(firepit.BlockId, pos);

        if (world.BlockAccessor.GetBlockEntity(pos) is BlockEntityFirepit fp)
        {
            // The smoker counts burn time in in-game hours; the firepit uses real seconds. If it was lit,
            // give it a fresh firewood-length burn so the firepit picks up where it left off.
            fp.fuelBurnTime = fp.maxFuelBurnTime = wasBurning ? 24f : 0f;
            fp.maxTemperature = maxTemperature;
            fp.canIgniteFuel = canIgniteFuel;
            fp.extinguishedTotalHours = extinguishedTotalHours;
            if (fuel != null) fp.fuelStack = fuel;
            fp.MarkDirty(true);
        }

        GiveOrDropRack(world, pos, byPlayer);
    }

    static void GiveOrDropRack(IWorldAccessor world, BlockPos pos, IPlayer byPlayer)
    {
        Block? rack = world.GetBlock(new AssetLocation(PemmicanModSystem.ModId, "smokerack"));
        if (rack == null) return;
        ItemStack stack = new ItemStack(rack);
        if (!byPlayer.InventoryManager.TryGiveItemstack(stack))
        {
            world.SpawnItemEntity(stack, pos.ToVec3d().Add(0.5, 0.7, 0.5));
        }
    }

    public override void OnBlockBroken(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1f)
    {
        if (world.Side == EnumAppSide.Server)
        {
            BlockEntitySmokingFirepit? be = world.BlockAccessor.GetBlockEntity<BlockEntitySmokingFirepit>(pos);
            if (be != null)
            {
                be.DropContents();
                Block? rack = world.GetBlock(new AssetLocation(PemmicanModSystem.ModId, "smokerack"));
                if (rack != null) world.SpawnItemEntity(new ItemStack(rack), pos.ToVec3d().Add(0.5, 0.5, 0.5));
            }
        }
        base.OnBlockBroken(world, pos, byPlayer, dropQuantityMultiplier);
    }

    // ---------------- ignition (IIgnitable) ----------------

    public EnumIgniteState OnTryIgniteStack(EntityAgent byEntity, BlockPos pos, ItemSlot slot, float secondsIgniting)
    {
        BlockEntitySmokingFirepit? be = api.World.BlockAccessor.GetBlockEntity<BlockEntitySmokingFirepit>(pos);
        if (be != null && be.IsBurning)
        {
            return secondsIgniting > 2f ? EnumIgniteState.IgniteNow : EnumIgniteState.Ignitable;
        }
        return EnumIgniteState.NotIgnitable;
    }

    public EnumIgniteState OnTryIgniteBlock(EntityAgent byEntity, BlockPos pos, float secondsIgniting)
    {
        BlockEntitySmokingFirepit? be = api.World.BlockAccessor.GetBlockEntity<BlockEntitySmokingFirepit>(pos);
        if (be == null) return EnumIgniteState.NotIgnitable;
        return be.GetIgnitableState(secondsIgniting);
    }

    public void OnTryIgniteBlockOver(EntityAgent byEntity, BlockPos pos, float secondsIgniting, ref EnumHandling handling)
    {
        BlockEntitySmokingFirepit? be = api.World.BlockAccessor.GetBlockEntity<BlockEntitySmokingFirepit>(pos);
        if (be != null && !be.canIgniteFuel)
        {
            be.canIgniteFuel = true;
            be.extinguishedTotalHours = api.World.Calendar.TotalHours;
        }
        handling = EnumHandling.PreventDefault;
    }

    public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
    {
        return (interactions ?? System.Array.Empty<WorldInteraction>()).Append(base.GetPlacedBlockInteractionHelp(world, selection, forPlayer));
    }

    static ItemStack[] MeatStacks(IWorldAccessor world)
    {
        System.Collections.Generic.List<ItemStack> stacks = new();
        foreach (string code in new[] { "redmeat-raw", "bushmeat-raw", "fish-raw", "poultry-raw" })
        {
            Item? item = world.GetItem(new AssetLocation("game", code));
            if (item != null) stacks.Add(new ItemStack(item));
        }
        return stacks.ToArray();
    }

    static ItemStack[] FuelStacks(IWorldAccessor world)
    {
        Item? firewood = world.GetItem(new AssetLocation("game", "firewood"));
        return firewood != null ? new[] { new ItemStack(firewood) } : System.Array.Empty<ItemStack>();
    }
}
