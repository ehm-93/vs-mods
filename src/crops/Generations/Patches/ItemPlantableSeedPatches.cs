using System.Text;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace Ehm93.VS.Crops.Generations;

internal static class ItemPlantableSeedPatches
{
    // Show seed generation in held item info.
    [HarmonyPatchCategory(GenerationsModSystem.ModId)]
    [HarmonyPatch(typeof(ItemPlantableSeed), "GetHeldItemInfo")]
    internal static class GetHeldItemInfoPatch
    {
        [HarmonyPostfix]
        public static void After(ItemPlantableSeed __instance, ItemSlot inSlot, StringBuilder dsc)
        {
            var gen = inSlot.Itemstack?.Attributes?.GetInt("generation") ?? 0;
            if (gen != 0) dsc.AppendLine(Lang.Get("Generation: {0}", gen));
        }
    }

    // After a successful plant, transfer the seed's generation to the farmland behavior.
    [HarmonyPatchCategory(GenerationsModSystem.ModId)]
    [HarmonyPatch(typeof(ItemPlantableSeed), "OnHeldInteractStart")]
    internal static class OnHeldInteractStartPatch
    {
        [HarmonyPrefix]
        public static void Before(ItemSlot itemslot, BlockSelection blockSel, out PlantContext __state)
        {
            __state = new PlantContext
            {
                SeedGen = itemslot.Itemstack?.Attributes?.GetInt("generation") ?? 0,
                Position = blockSel?.Position,
            };
        }

        [HarmonyPostfix]
        public static void After(EntityAgent byEntity, PlantContext __state)
        {
            if (__state?.Position == null) return;
            var farmland = byEntity.World.BlockAccessor.GetBlockEntity<BlockEntityFarmland>(__state.Position);
            if (farmland == null) return;

            if (byEntity.World.BlockAccessor.GetBlock(__state.Position.UpCopy()) is not BlockCrop) return;

            var behavior = farmland.GetBehavior<BEBehaviorFarmlandGeneration>();
            if (behavior == null) return;
            behavior.Generation = __state.SeedGen;
        }
    }

    public class PlantContext
    {
        public int SeedGen;
        public Vintagestory.API.MathTools.BlockPos? Position;
    }
}
