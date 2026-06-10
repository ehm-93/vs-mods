using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Ehm93.VS.Primitive.Fireplace;

// State and rendering for the stone ring on a firepit. Attached to the vanilla firepit block entity
// through patches/firepit.json. Stones are added/removed by the interaction patch in FirepitPatches.cs.
//
// Rendering note: BlockEntityFirepit.OnTesselation does NOT call base, so this behavior's own
// OnTesselation hook is never invoked. AddRingMesh is instead called from a Harmony postfix on the
// firepit's OnTesselation (also in FirepitPatches.cs). Save/load, GetBlockInfo, and OnBlockBroken all
// flow through base calls the firepit does make, so those use the normal behavior overrides.
public class BEBehaviorStoneRing : BlockEntityBehavior, ITexPositionSource
{
    private const string TreeKey = "fireplaceStoneRing";

    // Vanilla renders the loose stone on the ground at scale 4.5 (groundTransform); that's far too big
    // for a ring on the firepit rim, so the stones sit at their raw item-mesh size here.
    private const float MeshScale = 1f;
    private const float RingRadius = 0.46f;

    // Rock variant of each placed stone (e.g. "granite"), in placement order. Mixed rings are fine.
    private readonly List<string> stones = new();

    // Set transiently while tesselating a stone item so the ITexPositionSource indexer can resolve its
    // textures into the block atlas (see AddRingMesh).
    private ICoreClientAPI? capi;
    private CollectibleObject? nowTesselatingObj;
    private Shape? nowTesselatingShape;

    public BEBehaviorStoneRing(BlockEntity blockentity) : base(blockentity) { }

    public int Count => stones.Count;

    public bool IsComplete => Count >= Config.RingSize;

    private FireplaceConfig Config => Api.ModLoader.GetModSystem<FireplaceModSystem>().Config;

    // Server-side: take one stone from the slot and set it on the rim.
    public bool TryAddStone(ItemSlot slot, IPlayer byPlayer)
    {
        if (IsComplete || slot.Itemstack?.Collectible is not ItemStone stone) return false;

        stones.Add(stone.Variant["rock"] ?? "granite");

        if (byPlayer.WorldData.CurrentGameMode != EnumGameMode.Creative)
        {
            slot.TakeOut(1);
            slot.MarkDirty();
        }

        PlayStoneSound();
        Blockentity.MarkDirty(true);
        return true;
    }

    // Server-side: take the last-placed stone back.
    public bool TryRemoveStone(IPlayer byPlayer)
    {
        if (Count == 0) return false;

        ItemStack? stack = MakeStack(stones[^1]);
        stones.RemoveAt(stones.Count - 1);

        if (stack != null && !byPlayer.InventoryManager.TryGiveItemstack(stack))
        {
            Api.World.SpawnItemEntity(stack, Pos.ToVec3d().Add(0.5, 0.5, 0.5));
        }

        PlayStoneSound();
        Blockentity.MarkDirty(true);
        return true;
    }

    public override void OnBlockBroken(IPlayer? byPlayer = null)
    {
        base.OnBlockBroken(byPlayer);
        if (Api.Side != EnumAppSide.Server) return;

        foreach (var group in stones.GroupBy(rock => rock))
        {
            ItemStack? stack = MakeStack(group.Key);
            if (stack == null) continue;
            stack.StackSize = group.Count();
            Api.World.SpawnItemEntity(stack, Pos.ToVec3d().Add(0.5, 0.5, 0.5));
        }
        stones.Clear();
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetString(TreeKey, string.Join(",", stones));
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);

        string joined = tree.GetString(TreeKey, "") ?? "";
        string[] loaded = joined.Length == 0 ? Array.Empty<string>() : joined.Split(',');

        bool changed = !loaded.SequenceEqual(stones);
        stones.Clear();
        stones.AddRange(loaded);

        // The server's MarkDirty(true) only syncs the tree; the client must retesselate itself,
        // same as BEFirepit does for its burn state. Api is null during world load — fine, the
        // initial tesselation hasn't happened yet at that point.
        if (changed && Api?.Side == EnumAppSide.Client) Blockentity.MarkDirty(true);
    }

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        base.GetBlockInfo(forPlayer, dsc);
        if (Count == 0) return;

        dsc.AppendLine(Lang.Get("primitivefireplace:ringinfo", Count, Config.RingSize));
        if (IsComplete)
        {
            dsc.AppendLine(Lang.Get("primitivefireplace:ringbonus", (int)Math.Round(Config.FuelBonus * 100)));
        }
    }

    // Called from the OnTesselation postfix. Runs on the tesselation thread; only use the passed
    // tesselator, never the client API's main-thread one.
    public void AddRingMesh(ITerrainMeshPool mesher, ITesselatorAPI tesselator)
    {
        if (stones.Count == 0 || Api is not ICoreClientAPI clientApi) return;
        capi = clientApi;

        int ringSize = Math.Max(1, Config.RingSize);
        Vec3f origin = new(0.5f, 0f, 0.5f);
        var meshByRock = new Dictionary<string, MeshData>();

        for (int i = 0; i < stones.Count; i++)
        {
            if (!meshByRock.TryGetValue(stones[i], out MeshData? baseMesh))
            {
                Item? item = capi.World.GetItem(new AssetLocation("game", "stone-" + stones[i]));
                if (item == null) continue;

                // Tesselate into the BLOCK atlas via ITexPositionSource — items live in a separate atlas,
                // so a plain TesselateItem(out mesh) added to the chunk mesh renders invisible.
                nowTesselatingObj = item;
                nowTesselatingShape = item.Shape?.Base != null ? capi.TesselatorManager.GetCachedShape(item.Shape.Base) : null;
                tesselator.TesselateItem(item, out baseMesh, this);
                Array.Fill(baseMesh.RenderPassesAndExtraBits, (short)EnumChunkRenderPass.Opaque);
                baseMesh.Scale(origin, MeshScale, MeshScale, MeshScale);
                meshByRock[stones[i]] = baseMesh;
            }

            float angle = GameMath.TWOPI * i / ringSize;
            MeshData mesh = baseMesh.Clone()
                // Yaw the stone's long axis (local X) tangent to the ring so its long face points at the fire.
                .Rotate(origin, 0f, angle + GameMath.PIHALF, 0f)
                .Translate(GameMath.Cos(angle) * RingRadius, 0f, GameMath.Sin(angle) * RingRadius);

            mesher.AddMeshData(mesh);
        }
    }

    // ITexPositionSource: resolve the stone item's textures into the block atlas (mirrors BlockEntityDisplay).
    // Only valid while nowTesselatingObj/nowTesselatingShape are set during AddRingMesh.
    public Size2i AtlasSize => capi!.BlockTextureAtlas.Size;

    public TextureAtlasPosition this[string textureCode]
    {
        get
        {
            IDictionary<string, CompositeTexture>? textures =
                nowTesselatingObj is Item it ? it.Textures : (nowTesselatingObj as Block)?.Textures;

            AssetLocation? path = null;
            if (textures != null && textures.TryGetValue(textureCode, out CompositeTexture? ct)) path = ct.Baked.BakedName;
            if (path == null && textures != null && textures.TryGetValue("all", out ct)) path = ct.Baked.BakedName;
            if (path == null) nowTesselatingShape?.Textures.TryGetValue(textureCode, out path);
            path ??= new AssetLocation(textureCode);

            TextureAtlasPosition? pos = capi!.BlockTextureAtlas[path];
            if (pos == null)
            {
                capi.BlockTextureAtlas.GetOrInsertTexture(path, out int _, out TextureAtlasPosition inserted);
                pos = inserted;
            }
            return pos ?? capi.BlockTextureAtlas.UnknownTexturePosition;
        }
    }

    private ItemStack? MakeStack(string rock)
    {
        Item? item = Api.World.GetItem(new AssetLocation("game", "stone-" + rock));
        return item == null ? null : new ItemStack(item);
    }

    private void PlayStoneSound()
    {
        // Same randomized sound ground storage uses for loose stones.
        Api.World.PlaySoundAt(new AssetLocation("game", "sounds/block/loosestone*"), Pos, 0);
    }
}
