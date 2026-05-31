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

    // Selection box 0 is the whole frame (general hang/take); boxes 1..RackSlots are the individual hang
    // spots, matched to the rendered item layout so the player can target one piece.
    static Cuboidf[]? selectionBoxes;
    public override Cuboidf[] GetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
    {
        if (selectionBoxes == null)
        {
            List<Cuboidf> boxes = new() { new Cuboidf(0.12f, 0f, 0.12f, 0.88f, 0.69f, 0.88f) };
            for (int i = 0; i < BlockEntityRack.RackSlots; i++)
            {
                float x = BlockEntityRack.PosX[i], z = BlockEntityRack.PosZ[i];
                boxes.Add(new Cuboidf(x - 0.13f, 0.62f, z - 0.13f, x + 0.13f, 1.0f, z + 0.13f));
            }
            selectionBoxes = boxes.ToArray();
        }
        return selectionBoxes;
    }

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        BlockEntityDryingRack? be = world.BlockAccessor.GetBlockEntity<BlockEntityDryingRack>(blockSel.Position);
        if (be == null) return base.OnBlockInteractStart(world, byPlayer, blockSel);

        ItemSlot hand = byPlayer.InventoryManager.ActiveHotbarSlot;
        ItemStack? held = hand.Itemstack;
        bool bulk = byPlayer.Entity.Controls.CtrlKey;
        // Box 1..RackSlots targets that exact hang spot; box 0 (frame) keeps the general behavior.
        int targeted = blockSel.SelectionBoxIndex >= 1 && blockSel.SelectionBoxIndex <= BlockEntityRack.RackSlots
            ? blockSel.SelectionBoxIndex : 0;

        // Hang meat (or fresh fruit, with Expanded Foods) on the rack. If a specific (empty) spot is
        // targeted, fill it; otherwise — including when the targeted spot is already full — fall back to
        // the first free spot so the click never silently no-ops.
        if (held != null && be.IsRackable(held))
        {
            if (world.Side == EnumAppSide.Server && !(targeted > 0 && be.TryHangSlot(targeted, hand)))
                be.TryHang(hand, bulk);
            return true;
        }

        // Empty hand: take the targeted piece, or — if that spot is empty — fall back to the top piece.
        if (held == null)
        {
            if (world.Side == EnumAppSide.Server && !(targeted > 0 && be.TryTakeSlot(targeted, byPlayer)))
                be.TryTake(byPlayer, bulk);
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
