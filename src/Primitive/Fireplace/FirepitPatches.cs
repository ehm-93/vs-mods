using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace Ehm93.VS.Primitive.Fireplace;

// Right-click a finished firepit with stones in hand to set one on the rim; sneak + right-click with
// an empty hand to take one back. The firepit handles right-clicks before held items do (it opens its
// GUI), so this has to be intercepted on the firepit itself — same approach as Pemmican's rack patch.
[HarmonyPatchCategory(FireplaceModSystem.ModId)]
[HarmonyPatch(typeof(BlockFirepit), nameof(BlockFirepit.OnBlockInteractStart))]
internal static class FirepitOnInteractPatch
{
    [HarmonyPrefix]
    public static bool Prefix(BlockFirepit __instance, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref bool __result)
    {
        if (blockSel?.Position == null || byPlayer == null) return true;
        if (__instance.Stage != 5) return true; // under construction: vanilla handles firewood adds

        var ring = world.BlockAccessor.GetBlockEntity(blockSel.Position)?.GetBehavior<BEBehaviorStoneRing>();
        if (ring == null) return true;

        ItemSlot hand = byPlayer.InventoryManager.ActiveHotbarSlot;

        // Holding stones: add one to the ring. Once full, fall through so stones-in-hand
        // behaves like vanilla again (opens the firepit GUI).
        if (hand?.Itemstack?.Collectible is ItemStone)
        {
            if (ring.IsComplete) return true;
            if (world.Side == EnumAppSide.Server) ring.TryAddStone(hand, byPlayer);
            __result = true;
            return false; // skip the firepit's own interaction (don't open its GUI)
        }

        // Sneaking with an empty hand: take the last stone back.
        if (hand != null && hand.Empty && byPlayer.Entity.Controls.ShiftKey && ring.Count > 0)
        {
            if (world.Side == EnumAppSide.Server) ring.TryRemoveStone(byPlayer);
            __result = true;
            return false;
        }

        return true;
    }
}

// Vanilla sets fuelBurnTime in exactly one place — igniteWithFuel — so scaling it there applies the
// ring bonus once per piece of fuel, with zero per-tick work. Fuel simulation only runs server-side
// (OnBurnTick early-returns on the client); the client receives fuelBurnTime through normal BE sync.
[HarmonyPatchCategory(FireplaceModSystem.ModId)]
[HarmonyPatch(typeof(BlockEntityFirepit), nameof(BlockEntityFirepit.igniteWithFuel))]
internal static class FirepitIgniteWithFuelPatch
{
    [HarmonyPostfix]
    public static void Postfix(BlockEntityFirepit __instance)
    {
        ICoreAPI? api = __instance.Api;
        if (api == null || api.Side != EnumAppSide.Server) return;

        var ring = __instance.GetBehavior<BEBehaviorStoneRing>();
        if (ring == null || !ring.IsComplete) return;

        FireplaceConfig? config = api.ModLoader.GetModSystem<FireplaceModSystem>()?.Config;
        if (config == null) return;

        float mul = 1f + config.FuelBonus;
        // Scale both so the fuel bar in the firepit GUI (fuelBurnTime / maxFuelBurnTime) stays proportional.
        __instance.fuelBurnTime *= mul;
        __instance.maxFuelBurnTime *= mul;
    }
}

// BlockEntityFirepit.OnTesselation never calls base, so behaviors' OnTesselation hooks are dead on
// firepits — the ring mesh has to be appended here instead.
[HarmonyPatchCategory(FireplaceModSystem.ModId)]
[HarmonyPatch(typeof(BlockEntityFirepit), nameof(BlockEntityFirepit.OnTesselation))]
internal static class FirepitOnTesselationPatch
{
    [HarmonyPostfix]
    public static void Postfix(BlockEntityFirepit __instance, ITerrainMeshPool mesher, ITesselatorAPI tesselator)
    {
        __instance.GetBehavior<BEBehaviorStoneRing>()?.AddRingMesh(mesher, tesselator);
    }
}
