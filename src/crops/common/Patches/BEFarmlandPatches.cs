using System.Linq;
using System.Text;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace Ehm93.VS.Crops.Common;

internal static class BEFarmlandPatches
{
    [HarmonyPatchCategory(CommonModSystem.ModId)]
    [HarmonyPatch(typeof(BlockEntityFarmland), "GetBlockInfo")]
    internal static class GetBlockInfoPatch
    {
        [HarmonyPostfix]
        public static void After(BlockEntityFarmland __instance, IPlayer forPlayer, StringBuilder dsc)
        {
            foreach (BlockEntityBehavior behavior in __instance.Behaviors)
            {
                behavior.GetBlockInfo(forPlayer, dsc);
            }
        }
    }

    [HarmonyPatchCategory(CommonModSystem.ModId)]
    [HarmonyPatch(typeof(BlockEntityFarmland), "OnTesselation")]
    internal static class OnTesselationPatch
    {
        [HarmonyPostfix]
        public static void After(BlockEntityFarmland __instance, ref bool __result, ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
        {
            bool flag = false;
            for (int i = 0; i < __instance.Behaviors.Count; i++)
            {
                flag |= __instance.Behaviors[i].OnTesselation(mesher, tessThreadTesselator);
            }
            __result |= flag;
        }
    }

    [HarmonyPatchCategory(CommonModSystem.ModId)]
    [HarmonyPatch(typeof(BlockEntityFarmland), "OnBlockInteract")]
    internal static class OnBlockInteractPatch
    {
        [HarmonyPrefix]
        public static bool Before(BlockEntityFarmland __instance, ref bool __result, IPlayer byPlayer)
        {
            var behaviors = __instance.Behaviors.Where(i => i is IOnBlockInteract);
            foreach (IOnBlockInteract behavior in behaviors)
            {
                var handled = behavior.OnBlockInteract(byPlayer);
                if (handled)
                {
                    __result = handled;
                    return false;
                }
            }
            return true;
        }
    }
}
