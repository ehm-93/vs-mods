using System;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace Ehm93.VS.Crops.Mulch;

internal class BEFarmlandMulchPatches
{
    [HarmonyPatchCategory(MulchModSystem.ModId)]
    [HarmonyPatch(typeof(BlockEntityFarmland), "updateMoistureLevel", new Type[] {
        typeof(double), typeof(float), typeof(bool), typeof(ClimateCondition)
    })]
    internal static class UpdateMoistureLevelPatch
    {
        private static readonly FieldInfo moistureLevel = AccessTools.Field(typeof(BlockEntityFarmland), "moistureLevel");

        [HarmonyPrefix]
        public static void Before(BlockEntityFarmland __instance, ref float __state)
        {
            var behavior = __instance.GetBehavior<BEBehaviorFarmlandMulch>();
            if (behavior == null) return;
            __state = __instance.MoistureLevel;
        }

        [HarmonyPostfix]
        public static void After(BlockEntityFarmland __instance, ref float __state)
        {
            var behavior = __instance.GetBehavior<BEBehaviorFarmlandMulch>();
            if (behavior == null) return;

            var diff = __state - __instance.MoistureLevel;
            var mulchCoef = 0.0075f * behavior.MulchLevel;
            if (diff > 0)
            {
                var newVal = __instance.MoistureLevel + (float)mulchCoef * diff;
                moistureLevel.SetValue(__instance, newVal);
            }
        }
    }
}
