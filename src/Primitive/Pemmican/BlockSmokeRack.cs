using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Ehm93.VS.Primitive.Pemmican;

// The smoking rack is a held-only item (marked Unplaceable). It can't be placed in the world; instead,
// right-clicking a built, empty firepit with it converts the firepit into a combined "smoking firepit"
// block. That conversion is driven by a Harmony patch on the firepit's interaction (see FirepitPatches),
// because the firepit handles the right-click before a held item ever would.
public class BlockSmokeRack : Block
{
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
