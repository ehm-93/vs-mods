using System;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Ehm93.VS.Crops.Generations;

internal static class BlockCropPatches
{
    // Exponential yield scaling: gen 1 → 0.5, gen 5 → 1.0, gen 10 → 1.5, gen ∞ → 2.0
    private static float YieldMultiplier(int generation) =>
        -1.695f * (float)Math.Exp(-0.1221f * generation) + 2f;

    private static int GetFarmlandGeneration(IWorldAccessor world, BlockPos cropPos)
    {
        var farmland = world.BlockAccessor.GetBlockEntity<BlockEntityFarmland>(cropPos.DownCopy());
        return farmland?.GetBehavior<BEBehaviorFarmlandGeneration>()?.Generation ?? 0;
    }

    [HarmonyPatchCategory(GenerationsModSystem.ModId)]
    [HarmonyPatch(typeof(BlockCrop), "GetDrops")]
    internal static class GetDropsPatch
    {
        [HarmonyPostfix]
        public static void After(BlockCrop __instance, ref ItemStack[] __result, IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier)
        {
            if (__result == null) return;

            int gen = GetFarmlandGeneration(world, pos);
            float yieldMultiplier = YieldMultiplier(gen);

            foreach (var drop in __result)
            {
                float effectiveMultiplier = drop.Item is ItemPlantableSeed
                    ? Math.Max(yieldMultiplier, 1f)
                    : yieldMultiplier;

                double scaled = effectiveMultiplier * drop.StackSize;
                int baseAmount = (int)Math.Floor(scaled);
                double fractional = scaled - baseAmount;
                if (world.Rand.NextDouble() < fractional) baseAmount += 1;
                drop.StackSize = baseAmount;
            }

            bool isRipe = IsRipe(__instance);
            int nextGen = isRipe ? gen + 1 : gen;
            if (world.BlockAccessor.GetBlockEntity<BlockEntityFarmland>(pos.DownCopy()) == null)
            {
                // wild crops always drop gen 0
                nextGen = 0;
            }

            foreach (var drop in __result)
            {
                if (drop.Item is ItemPlantableSeed)
                {
                    drop.Attributes.SetInt("generation", nextGen);
                }
            }
        }
    }

    [HarmonyPatchCategory(GenerationsModSystem.ModId)]
    [HarmonyPatch(typeof(BlockCrop), "GetPlacedBlockInfo")]
    internal static class GetPlacedBlockInfoPatch
    {
        [HarmonyPostfix]
        public static void After(BlockCrop __instance, ref string __result, IWorldAccessor world, BlockPos pos, IPlayer forPlayer)
        {
            int gen = GetFarmlandGeneration(world, pos);
            if (gen != 0) __result += "\n" + Lang.Get("Generation: {0}", gen);
        }
    }

    private static bool IsRipe(BlockCrop crop)
    {
        if (!int.TryParse(crop.LastCodePart(), out var stage)) return false;
        return stage >= crop.CropProps.GrowthStages;
    }
}
