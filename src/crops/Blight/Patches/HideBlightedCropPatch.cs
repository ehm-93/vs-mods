using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Ehm93.VS.Crops.Blight;

// While a crop is blighted, the FarmlandBlight behavior renders a re-textured copy of it as an overlay
// (the farmland mesh, translated +1Y). Without this the real crop block would still tesselate underneath,
// double-rendering the healthy crop through the blighted overlay. So we empty the crop's own mesh for any
// position the behavior has flagged blighted. The flag set is a thread-safe ConcurrentDictionary precisely
// so this — which runs on a chunk-tesselation worker thread — never has to touch a BlockEntity.
[HarmonyPatchCategory(BlightModSystem.ModId)]
[HarmonyPatch(typeof(BlockCrop), "OnJsonTesselation")]
internal static class HideBlightedCropPatch
{
    [HarmonyPostfix]
    public static void After(ref MeshData sourceMesh, BlockPos pos)
    {
        // sourceMesh is the block TYPE's shared cached mesh — mutating it (e.g. Clear) blanks every crop of
        // that type, not just this position. Reassign the ref to a per-position empty mesh instead (the same
        // approach vanilla BlockCrop uses when it swaps in its on-farmland mesh).
        if (BEBehaviorFarmlandBlight.BlightedCrops.ContainsKey(pos)) sourceMesh = sourceMesh.EmptyClone();
    }
}
