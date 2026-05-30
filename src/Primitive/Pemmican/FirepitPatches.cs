using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace Ehm93.VS.Primitive.Pemmican;

// The firepit handles a right-click before any held item does, so attaching the smoking rack has to be
// intercepted on the firepit itself. When the player right-clicks a built, empty firepit while holding a
// smoking rack, convert it into a smoking firepit instead of opening the firepit UI.
[HarmonyPatchCategory(PemmicanModSystem.ModId)]
[HarmonyPatch(typeof(BlockFirepit), nameof(BlockFirepit.OnBlockInteractStart))]
internal static class FirepitOnInteractPatch
{
    [HarmonyPrefix]
    public static bool Prefix(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref bool __result)
    {
        if (blockSel?.Position == null || byPlayer == null) return true;

        ItemSlot hand = byPlayer.InventoryManager.ActiveHotbarSlot;
        if (hand?.Itemstack?.Collectible is not BlockSmokeRack) return true;

        bool canAttach = BlockSmokeRack.CanAttach(world, blockSel.Position, out string failCode);

        // Holding a rack but this firepit isn't even a candidate (e.g. still under construction):
        // let the firepit behave normally.
        if (!canAttach && failCode.Length == 0) return true;

        if (canAttach)
        {
            if (world.Side == EnumAppSide.Server)
            {
                BlockSmokeRack.AttachToFirepit(world, blockSel.Position);
                if (byPlayer.WorldData.CurrentGameMode != EnumGameMode.Creative)
                {
                    hand.TakeOut(1);
                    hand.MarkDirty();
                }
            }
        }
        else if (world.Side == EnumAppSide.Client)
        {
            (world.Api as ICoreClientAPI)?.TriggerIngameError(typeof(FirepitOnInteractPatch), failCode, Lang.Get("pemmican:" + failCode));
        }

        __result = true;
        return false; // skip the firepit's own interaction (don't open its UI)
    }
}
