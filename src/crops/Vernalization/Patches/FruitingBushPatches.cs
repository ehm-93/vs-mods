using HarmonyLib;
using Vintagestory.GameContent;

namespace Ehm93.VS.Crops.Vernalization;

// Gates a fruiting bush's Mature -> Flowering transition (the start of a fruiting cycle) on the
// BerryChilling vernalization requirement. Beginning to flower consumes the accumulated chill, so
// each fruiting cycle requires a fresh cold dormant period. Mature -> Flowering is the only path
// to Flowering in the vanilla growth state machine, so gating it here is sufficient.
internal static class FruitingBushPatches
{
    [HarmonyPatchCategory(VernalizationModSystem.ModId)]
    [HarmonyPatch(typeof(BEBehaviorFruitingBush), "setGrowthState")]
    internal static class SetGrowthStatePatch
    {
        [HarmonyPrefix]
        public static void Before(BEBehaviorFruitingBush __instance, ref EnumFruitingBushGrowthState state)
        {
            if (state != EnumFruitingBushGrowthState.Flowering) return;

            var chilling = __instance.Blockentity?.GetBehavior<BEBehaviorBerryChilling>();
            if (chilling == null) return;

            if (chilling.Vernalized) chilling.ConsumeVernalization();
            else state = EnumFruitingBushGrowthState.Mature; // hold in Mature until vernalized
        }
    }
}
