using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Ehm93.VS.Primitive.DryFuels;

// Drop-in replacement for the vanilla charcoal-pit block entity (wired in by patching charcoalpit's
// entityClass). It runs the vanilla conversion unchanged, then upgrades any charcoal pile that came from
// SEASONED firewood (dryfuels:firewood) to the higher-yield "seasoned charcoal pile" (2x charcoal). Pits with
// only ordinary firewood behave exactly like vanilla.
public class BlockEntitySeasonedCharcoalPit : BlockEntityCharcoalPit
{
    protected override void ConvertPit()
    {
        // Record which firewood-pile positions hold seasoned firewood, before the conversion consumes them.
        var seasonedPositions = new HashSet<BlockPos>();
        WalkPit(bpos => { if (IsSeasonedFirewood(bpos)) seasonedPositions.Add(bpos.Copy()); }, defaultCheckAction);

        base.ConvertPit(); // vanilla yield math: places charcoalpile-N at the firewood positions

        if (seasonedPositions.Count == 0) return;

        foreach (BlockPos pos in seasonedPositions)
        {
            Block placed = Api.World.BlockAccessor.GetBlock(pos);
            // The column can run out of charcoal before reaching a position (left as air) — nothing to upgrade.
            if (placed.FirstCodePart() != "charcoalpile") continue;
            Block? seasoned = Api.World.GetBlock(new AssetLocation("dryfuels", "seasonedcharcoalpile-" + placed.Variant["amount"]));
            if (seasoned != null) Api.World.BlockAccessor.SetBlock(seasoned.BlockId, pos);
        }
    }

    protected virtual bool IsSeasonedFirewood(BlockPos pos)
    {
        var be = Api.World.BlockAccessor.GetBlockEntity<BlockEntityGroundStorage>(pos);
        AssetLocation? code = be?.Inventory[0]?.Itemstack?.Collectible?.Code;
        return code?.Domain == "dryfuels" && code.Path == "firewood";
    }
}
