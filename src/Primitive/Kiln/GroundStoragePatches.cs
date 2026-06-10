using System.Text;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace Ehm93.VS.Primitive.Kiln;

// A rawbrick ground-storage pile is the body of a brick clamp. Ground storage handles its own right-clicks
// (put/take) before held items get a say, so — like the firepit stone ring — the clamp interaction has to be
// intercepted on the storage's own interaction method. The BrickClamp behavior (patched onto every
// groundstorage block) decides whether to consume the click.
[HarmonyPatchCategory(KilnModSystem.ModId)]
[HarmonyPatch(typeof(BlockEntityGroundStorage), nameof(BlockEntityGroundStorage.OnPlayerInteractStart))]
internal static class GroundStorageInteractPatch
{
    // __state: the pile's rawbrick count before vanilla ran, or -1 if it wasn't a rawbrick pile.
    [HarmonyPrefix]
    public static bool Prefix(BlockEntityGroundStorage __instance, IPlayer player, BlockSelection bs, ref bool __result, out int __state)
    {
        var stack = __instance.Inventory?[0]?.Itemstack;
        __state = stack?.Collectible?.Code?.Path?.StartsWith("rawbrick-") == true ? stack.StackSize : -1;

        var clamp = __instance.GetBehavior<BEBehaviorBrickClamp>();
        if (clamp == null) return true; // not a clamp-capable pile: let vanilla put/take run

        // Returns false to consume the click (clamp handled it), true to fall through to vanilla.
        return clamp.OnInteract(player, bs, ref __result);
    }

    // If vanilla's put/take removed bricks from a clamp pile, the clamp is no longer all-full — refund its
    // fuel (the behavior no-ops when there's no fuel or the clamp is already lit). Runs even when the prefix
    // consumed the click, but then the stack is unchanged and nothing happens.
    [HarmonyPostfix]
    public static void Postfix(BlockEntityGroundStorage __instance, int __state)
    {
        if (__state < 0) return;
        var stack = __instance.Inventory?[0]?.Itemstack;
        int now = stack?.Collectible?.Code?.Path?.StartsWith("rawbrick-") == true ? stack.StackSize : 0;
        if (now < __state) __instance.GetBehavior<BEBehaviorBrickClamp>()?.OnBricksRemoved();
    }
}

// BlockEntityGroundStorage.GetBlockInfo does NOT call base.GetBlockInfo, so the BrickClamp behavior's own
// GetBlockInfo override never fires — append the clamp's fuel/firing line here instead (same pattern as the
// firepit OnTesselation postfix).
[HarmonyPatchCategory(KilnModSystem.ModId)]
[HarmonyPatch(typeof(BlockEntityGroundStorage), nameof(BlockEntityGroundStorage.GetBlockInfo))]
internal static class GroundStorageBlockInfoPatch
{
    [HarmonyPostfix]
    public static void Postfix(BlockEntityGroundStorage __instance, IPlayer forPlayer, StringBuilder dsc)
    {
        __instance.GetBehavior<BEBehaviorBrickClamp>()?.AppendClampInfo(forPlayer, dsc);
    }
}
