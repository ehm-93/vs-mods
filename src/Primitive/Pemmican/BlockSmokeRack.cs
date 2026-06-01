using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace Ehm93.VS.Primitive.Pemmican;

// The smoking rack does double duty. Held and right-clicked on a built, empty firepit it converts the
// firepit into a combined "smoking firepit" (driven by a Harmony patch on the firepit's interaction, since
// the firepit handles the right-click before a held item would — see FirepitPatches). Placed on the ground
// or in a cellar it becomes a standalone, fireless DRYING RACK (BlockEntityDryingRack) that air-dries its
// contents by climate. Same item, two modes.
public class BlockSmokeRack : Block
{
    WorldInteraction[]? interactions;

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);

        ItemStack[] rackables = RackableStacks(api.World);
        interactions = new[]
        {
            new WorldInteraction
            {
                ActionLangCode = "pemmican:blockhelp-dryingrack-hang",
                MouseButton = EnumMouseButton.Right,
                Itemstacks = rackables
            },
            new WorldInteraction
            {
                ActionLangCode = "pemmican:blockhelp-dryingrack-hang-bulk",
                MouseButton = EnumMouseButton.Right,
                HotKeyCode = "ctrl",
                Itemstacks = rackables
            },
            new WorldInteraction
            {
                ActionLangCode = "pemmican:blockhelp-dryingrack-take",
                MouseButton = EnumMouseButton.Right
            },
            new WorldInteraction
            {
                ActionLangCode = "pemmican:blockhelp-dryingrack-take-bulk",
                MouseButton = EnumMouseButton.Right,
                HotKeyCode = "ctrl"
            }
        };
    }

    // The standalone rack is a half-slab: a cell always has a BOTTOM (level 0) half-rack and, once you stack
    // a second one on it, a TOP (level 1). One clean selection box per PRESENT half (no busy per-slot boxes);
    // the exact hang spot is picked from the aim point (HitPosition) instead. The top box only appears once
    // the top half exists, so a single slab shows just one outline.
    static readonly Cuboidf BottomBox = new Cuboidf(0.12f, 0f, 0.12f, 0.88f, 0.5f, 0.88f);
    static readonly Cuboidf TopBox = new Cuboidf(0.12f, 0.5f, 0.12f, 0.88f, 1.0f, 0.88f);
    static readonly Cuboidf[] BottomOnly = { BottomBox };
    static readonly Cuboidf[] BothBoxes = { BottomBox, TopBox };

    Cuboidf[] HalfBoxes(IBlockAccessor blockAccessor, BlockPos pos)
        => blockAccessor.GetBlockEntity<BlockEntityDryingRack>(pos)?.HasTop == true ? BothBoxes : BottomOnly;

    public override Cuboidf[] GetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos) => HalfBoxes(blockAccessor, pos);
    public override Cuboidf[] GetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos) => HalfBoxes(blockAccessor, pos);

    // Nearest hang spot to the aim point within a half (0 = none/frame). HitPosition is block-relative 0..1.
    static int SlotFromHit(BlockSelection sel)
    {
        Vec3d hit = sel.HitPosition;
        int best = 0;
        double bestD = 0.02; // ~0.14-block radius around a hang spot
        for (int i = 0; i < BlockEntityDryingRack.HalfSlots; i++)
        {
            double dx = hit.X - BlockEntityDryingRack.PosX[i];
            double dz = hit.Z - BlockEntityDryingRack.PosZ[i];
            double d = dx * dx + dz * dz;
            if (d < bestD) { bestD = d; best = i + 1; }
        }
        return best;
    }

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        BlockEntityDryingRack? be = world.BlockAccessor.GetBlockEntity<BlockEntityDryingRack>(blockSel.Position);
        if (be == null) return base.OnBlockInteractStart(world, byPlayer, blockSel);

        int level = blockSel.SelectionBoxIndex >= 1 ? 1 : 0; // box 1 = top half (only present once HasTop)
        ItemSlot hand = byPlayer.InventoryManager.ActiveHotbarSlot;
        ItemStack? held = hand.Itemstack;
        bool bulk = byPlayer.Entity.Controls.CtrlKey;

        // Holding the rack item with no top yet -> stack a second half-rack on top of this one.
        if (held?.Collectible?.Code == Code && !be.HasTop)
        {
            if (world.Side == EnumAppSide.Server)
            {
                be.InstallHalf(1);
                if (byPlayer.WorldData.CurrentGameMode != EnumGameMode.Creative) { hand.TakeOut(1); hand.MarkDirty(); }
            }
            return true;
        }

        // Hang meat (or fresh fruit, with Expanded Foods) on the targeted half: the piece nearest the aim
        // point first, else the first free spot so the click never silently no-ops.
        if (held != null && be.HasHalf(level) && be.IsRackable(held))
        {
            int slot = SlotFromHit(blockSel);
            if (world.Side == EnumAppSide.Server && !(slot > 0 && be.TryHangSlot(level, slot, hand)))
                be.TryHang(level, hand, bulk);
            return true;
        }

        // Empty hand: take the piece nearest the aim point, or fall back to the top piece of that half.
        if (held == null && be.HasHalf(level))
        {
            int slot = SlotFromHit(blockSel);
            if (world.Side == EnumAppSide.Server && !(slot > 0 && be.TryTakeSlot(level, slot, byPlayer)))
                be.TryTake(level, byPlayer, bulk);
            return true;
        }

        return base.OnBlockInteractStart(world, byPlayer, blockSel);
    }

    public override void OnBlockBroken(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1f)
    {
        if (world.Side == EnumAppSide.Server)
        {
            world.BlockAccessor.GetBlockEntity<BlockEntityDryingRack>(pos)?.DropContents();
        }
        base.OnBlockBroken(world, pos, byPlayer, dropQuantityMultiplier);
        // Removing a rack can re-shape the whole group's convex decomposition, so re-mesh the lot.
        BlockEntityDryingRack.RemeshGroup(world, pos);
    }

    // A neighbouring rack appearing or disappearing changes which legs we merge, so re-mesh this one.
    public override void OnNeighbourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neibpos)
    {
        base.OnNeighbourBlockChange(world, pos, neibpos);
        if (world.Side == EnumAppSide.Client)
            world.BlockAccessor.GetBlockEntity<BlockEntityDryingRack>(pos)?.MarkDirty(true);
    }

    public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
    {
        return (interactions ?? System.Array.Empty<WorldInteraction>()).Append(base.GetPlacedBlockInteractionHelp(world, selection, forPlayer));
    }

    static ItemStack[] RackableStacks(IWorldAccessor world)
    {
        List<ItemStack> stacks = new();
        foreach (string code in new[] { "redmeat-raw", "bushmeat-raw", "fish-raw", "poultry-raw" })
        {
            Item? item = world.GetItem(new AssetLocation("game", code));
            if (item != null) stacks.Add(new ItemStack(item));
        }
        return stacks.ToArray();
    }

    // ---------------- firepit attach (held-item path, via FirepitPatches) ----------------

    public static bool CanAttach(IWorldAccessor world, BlockPos pos, out string failCode)
    {
        failCode = "";
        Block block = world.BlockAccessor.GetBlock(pos);
        if (block is not BlockFirepit) return false;

        string burnstate = block.Variant["burnstate"];
        if (burnstate != "extinct" && burnstate != "lit" && burnstate != "cold") return false; // still being built

        if (world.BlockAccessor.GetBlockEntity(pos) is not BlockEntityFirepit fp) return false;
        if (!fp.inputSlot.Empty || !fp.outputSlot.Empty)
        {
            failCode = "smokingfirepit-firepitbusy";
            return false;
        }
        return true;
    }

    public static void AttachToFirepit(IWorldAccessor world, BlockPos pos)
    {
        if (world.BlockAccessor.GetBlockEntity(pos) is not BlockEntityFirepit fp) return;

        Block firepitBlock = world.BlockAccessor.GetBlock(pos);
        string burnstate = firepitBlock.Variant["burnstate"];
        Block? smoking = world.GetBlock(new AssetLocation(PemmicanModSystem.ModId, "smokingfirepit-" + burnstate));
        if (smoking == null) return;

        // Snapshot fire state and pull the fuel out before the firepit's block entity is replaced
        // (so it doesn't drop its fuel when removed).
        bool wasBurning = fp.fuelBurnTime > 0f;
        int maxTemperature = fp.maxTemperature;
        bool canIgniteFuel = fp.canIgniteFuel;
        double extinguishedTotalHours = fp.extinguishedTotalHours;
        // TakeOutWhole() throws on an empty slot, so only pull fuel when there's actually some.
        ItemStack? fuel = fp.fuelSlot.Empty ? null : fp.fuelSlot.TakeOutWhole();

        world.BlockAccessor.SetBlock(smoking.BlockId, pos);

        if (world.BlockAccessor.GetBlockEntity(pos) is BlockEntitySmokingFirepit be)
        {
            // The firepit's fuelBurnTime is in real seconds; the smoker counts in in-game hours. If it was
            // lit, give it a short burst and it'll relight from the fuel stack on the next tick.
            be.fuelBurnTime = wasBurning ? 1f : 0f;
            be.maxFuelBurnTime = be.fuelBurnTime;
            be.maxTemperature = maxTemperature;
            be.canIgniteFuel = canIgniteFuel;
            be.extinguishedTotalHours = extinguishedTotalHours;
            if (fuel != null) be.FuelStack = fuel;
            be.MarkDirty(true);
        }
    }
}
