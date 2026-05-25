using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace Ehm93.VS.Crops.Common;

internal static class HoePatches
{
    public static void Patch(Harmony harmony)
    {
        var onLoadedPrefix = new HarmonyMethod(typeof(OnLoadedPatch).GetMethod("Prefix", BindingFlags.Static | BindingFlags.Public));
        var onHeldInteractStartPrefix = new HarmonyMethod(typeof(OnHeldInteractStartPatch).GetMethod("Prefix", BindingFlags.Static | BindingFlags.Public));
        var onHeldInteractStepPrefix = new HarmonyMethod(typeof(OnHeldInteractStepPatch).GetMethod("Prefix", BindingFlags.Static | BindingFlags.Public));

        PatchUtil.PatchAllOverrides(harmony, typeof(ItemHoe), "OnLoaded", prefix: onLoadedPrefix);
        PatchUtil.PatchAllOverrides(harmony, typeof(ItemHoe), "OnHeldInteractStart", prefix: onHeldInteractStartPrefix);
        PatchUtil.PatchAllOverrides(harmony, typeof(ItemHoe), "OnHeldInteractStep", prefix: onHeldInteractStepPrefix);
    }

    internal static class OnLoadedPatch
    {
        public static void Prefix(Item __instance, ICoreAPI api)
        {
            foreach (var behavior in __instance.CollectibleBehaviors)
            {
                behavior.OnLoaded(api);
            }
        }
    }

    internal static class OnHeldInteractStartPatch
    {
        public static bool Prefix(Item __instance, ItemSlot itemslot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handHandling)
        {
            EnumHandHandling localHandling = EnumHandHandling.NotHandled;
            bool handled = false;

            foreach (var behavior in __instance.CollectibleBehaviors)
            {
                EnumHandling behaviorHandling = EnumHandling.PassThrough;
                behavior.OnHeldInteractStart(itemslot, byEntity, blockSel, entitySel, firstEvent, ref localHandling, ref behaviorHandling);

                if (behaviorHandling != EnumHandling.PassThrough)
                {
                    handHandling = localHandling;
                    handled = true;
                }

                if (behaviorHandling == EnumHandling.PreventSubsequent)
                    return false;
            }

            return !handled;
        }
    }

    internal static class OnHeldInteractStepPatch
    {
        public static bool Prefix(Item __instance, ref bool __result, float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel)
        {
            bool finalResult = true;
            bool anyHandled = false;

            foreach (var behavior in __instance.CollectibleBehaviors)
            {
                EnumHandling handling = EnumHandling.PassThrough;
                bool result = behavior.OnHeldInteractStep(secondsUsed, slot, byEntity, blockSel, entitySel, ref handling);

                if (handling != EnumHandling.PassThrough)
                {
                    finalResult &= result;
                    anyHandled = true;
                }

                if (handling == EnumHandling.PreventSubsequent)
                {
                    __result = finalResult;
                    return false;
                }
            }

            if (anyHandled)
            {
                __result = finalResult;
                return false;
            }

            return true;
        }
    }
}
