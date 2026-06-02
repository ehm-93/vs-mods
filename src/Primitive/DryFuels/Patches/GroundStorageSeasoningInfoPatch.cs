using System;
using System.Text;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace Ehm93.VS.Primitive.DryFuels;

// BlockEntityGroundStorage.GetBlockInfo builds its own text and does not iterate block-entity behaviors,
// so surface the seasoning progress of a fuel pile with a postfix.
[HarmonyPatchCategory(DryFuelsModSystem.ModId)]
[HarmonyPatch(typeof(BlockEntityGroundStorage), nameof(BlockEntityGroundStorage.GetBlockInfo))]
internal static class GroundStorageSeasoningInfoPatch
{
    [HarmonyPostfix]
    public static void After(BlockEntityGroundStorage __instance, IPlayer forPlayer, StringBuilder dsc)
    {
        var seasoning = __instance.GetBehavior<BEBehaviorFuelSeasoning>();
        if (seasoning == null || !seasoning.HasSeasonable) return;
        dsc.AppendLine(Lang.Get("dryfuels:seasoning-progress", (int)Math.Round(seasoning.Progress * 100)));
    }
}
