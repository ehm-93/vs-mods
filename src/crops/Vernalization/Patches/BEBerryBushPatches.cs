using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.GameContent;
using Ehm93.VS.Crops.Common;

namespace Ehm93.VS.Crops.Vernalization;

internal static class BEBerryBushPatches
{
    public static void Patch(Harmony harmony)
    {
        var onExchangedPrefix = new HarmonyMethod(typeof(OnExchangedPatch).GetMethod("Prefix", BindingFlags.Static | BindingFlags.Public));
        var checkGrowPrefix = new HarmonyMethod(typeof(CheckGrowPatch).GetMethod("Prefix", BindingFlags.Static | BindingFlags.Public));
        var getBlockInfoPostfix = new HarmonyMethod(typeof(GetBlockInfoPatch).GetMethod("Postfix", BindingFlags.Static | BindingFlags.Public));

        PatchUtil.PatchAllOverrides(harmony, typeof(BlockEntityBerryBush), "OnExchanged", prefix: onExchangedPrefix);
        PatchUtil.PatchAllOverrides(harmony, typeof(BlockEntityBerryBush), "CheckGrow", prefix: checkGrowPrefix);
        PatchUtil.PatchAllOverrides(harmony, typeof(BlockEntityBerryBush), "GetBlockInfo", postfix: getBlockInfoPostfix);
    }

    public static class OnExchangedPatch
    {
        public static void Prefix(BlockEntity __instance, Block block)
        {
            foreach (var onExchanged in __instance.Behaviors.OfType<IOnExchanged>())
            {
                onExchanged.OnExchanged(block);
            }
        }
    }

    public static class CheckGrowPatch
    {
        public static bool Prefix(BlockEntity __instance)
        {
            var behaviors = __instance.Behaviors.OfType<ICheckGrow>();
            foreach (var behavior in behaviors)
            {
                if (!behavior.CheckGrow()) return false;
            }
            return true;
        }
    }

    public static class GetBlockInfoPatch
    {
        public static void Postfix(BlockEntity __instance, IPlayer forPlayer, StringBuilder sb)
        {
            foreach (BlockEntityBehavior behavior in __instance.Behaviors)
            {
                behavior.GetBlockInfo(forPlayer, sb);
            }
        }
    }
}
