using System;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace Ehm93.VS.Primitive.Pemmican;

// A standalone, fireless drying rack built as a half-height SLAB: one block cell holds up to TWO independent
// half-racks — a BOTTOM (slots 0..7) and a TOP (slots 8..15), each an 8-piece rack — so racks pack two to a
// vertical metre. Each half air-dries by local climate (sun/warmth/dry climate speed it up, rain backslides,
// perish races it) exactly like the old full-height rack. Adjacent half-racks at the SAME level merge their
// legs/rails into one shared frame (connecting), and vertically-adjacent half-slots bridge their posts into
// continuous uprights. Self-contained (the firepit's rack still uses BlockEntityRack); slot 0-based here.
public class BlockEntityDryingRack : BlockEntity, ITexPositionSource
{
    public const int HalfSlots = 6;
    const int Slots = HalfSlots * 2; // 0..5 = bottom half, 6..11 = top half

    // --- drying-rate model (starter constants; tune in-game) ---
    const float AirBaseRate = 0.35f;
    const float BackslideRate = 0.20f;
    const float TempMin = 0f;
    const float TempMax = 30f;
    const float RainThreshold = 0.04f;

    readonly InventoryGeneric inventory = new InventoryGeneric(Slots, null, null, null);
    public bool HasBottom;
    public bool HasTop;

    float[] dryProgress = new float[Slots];
    readonly InWorldContainer perishContainer;

    double lastCalendarHours = -1;

    ICoreClientAPI? capi;
    CollectibleObject? nowTesselatingObj;
    Shape? nowTesselatingShape;
    PemmicanModSystem? recipes;

    // On-rack item layout (block-local 0..1 in X/Z) per half: a 3 / 2 / 3 arrangement on the three rungs.
    // Kept inboard of the corner posts (X ~0.13-0.25 / 0.75-0.88) so hung pieces don't intersect the legs or
    // the stacked post bridges. Index i -> half slot i.
    const float ItemScale = 0.68f;
    const float ItemY = 0.32f; // height within a half that pieces sit at; +0.5 for the top half
    public static readonly float[] PosX = { 0.36f, 0.64f, 0.36f, 0.64f, 0.36f, 0.64f }; // 2 per rung
    public static readonly float[] PosZ = { 0.25f, 0.25f, 0.5f, 0.5f, 0.75f, 0.75f }; // front / mid / back rungs

    public BlockEntityDryingRack()
    {
        perishContainer = new InWorldContainer(() => inventory, "dryrackinv");
    }

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        inventory.LateInitialize($"dryingrack-{Pos.X}/{Pos.Y}/{Pos.Z}", api);
        inventory.Pos = Pos;
        capi = api as ICoreClientAPI;
        perishContainer.Init(Api, () => Pos, () => MarkDirty(true));
        if (lastCalendarHours < 0) lastCalendarHours = api.World.Calendar.TotalHours;
        if (api.Side == EnumAppSide.Server) RegisterGameTickListener(OnRackTick, 10000);
    }

    public override void OnBlockPlaced(ItemStack? byItemStack = null)
    {
        base.OnBlockPlaced(byItemStack);
        if (!HasBottom && !HasTop) HasBottom = true; // a fresh placement is a single bottom slab
        RemeshNeighbours();
    }

    // ---------------- halves ----------------

    public bool HasHalf(int level) => level == 0 ? HasBottom : HasTop;
    static int Base(int level) => level == 0 ? 0 : HalfSlots;

    public void InstallHalf(int level)
    {
        if (level == 0) HasBottom = true; else HasTop = true;
        MarkDirty(true);
        RemeshNeighbours(); // a new half changes the neighbours' connecting frames
    }

    public bool AnyHalf => HasBottom || HasTop;

    // ---------------- rack lookup ----------------

    public DryingResult? Match(ItemStack? stack)
    {
        if (Api == null || stack == null) return null;
        recipes ??= Api.ModLoader.GetModSystem<PemmicanModSystem>();
        return recipes?.Match(Api.World, stack);
    }

    public bool IsRackable(ItemStack? stack) => Match(stack) != null;

    bool HasRackable()
    {
        for (int i = 0; i < Slots; i++) if (IsRackable(inventory[i].Itemstack)) return true;
        return false;
    }

    public int CountRacked(int level)
    {
        int b = Base(level), n = 0;
        for (int i = 0; i < HalfSlots; i++) if (!inventory[b + i].Empty) n++;
        return n;
    }

    // ---------------- hang / take ----------------

    public bool TryHang(int level, ItemSlot handSlot, bool all)
    {
        if (!HasHalf(level) || handSlot.Empty || !IsRackable(handSlot.Itemstack)) return false;
        int b = Base(level);
        bool added = false;
        for (int i = 0; i < HalfSlots; i++)
        {
            if (!inventory[b + i].Empty) continue;
            if (handSlot.Empty || !IsRackable(handSlot.Itemstack)) break;
            if (handSlot.TryPutInto(Api.World, inventory[b + i], 1) > 0) { added = true; dryProgress[b + i] = 0f; }
            if (!all) break;
        }
        if (added) { handSlot.MarkDirty(); MarkDirty(true); }
        return added;
    }

    public bool TryHangSlot(int level, int slot, ItemSlot handSlot)
    {
        if (!HasHalf(level) || slot < 1 || slot > HalfSlots) return false;
        int idx = Base(level) + slot - 1;
        if (handSlot.Empty || !inventory[idx].Empty || !IsRackable(handSlot.Itemstack)) return false;
        if (handSlot.TryPutInto(Api.World, inventory[idx], 1) <= 0) return false;
        dryProgress[idx] = 0f;
        handSlot.MarkDirty();
        MarkDirty(true);
        return true;
    }

    public bool TryTake(int level, IPlayer byPlayer, bool all)
    {
        if (!HasHalf(level)) return false;
        int b = Base(level);
        bool took = false;
        for (int i = HalfSlots - 1; i >= 0; i--)
        {
            if (inventory[b + i].Empty) continue;
            GivePiece(b + i, byPlayer, level);
            took = true;
            if (!all) break;
        }
        if (took) MarkDirty(true);
        return took;
    }

    public bool TryTakeSlot(int level, int slot, IPlayer byPlayer)
    {
        if (!HasHalf(level) || slot < 1 || slot > HalfSlots) return false;
        int idx = Base(level) + slot - 1;
        if (inventory[idx].Empty) return false;
        GivePiece(idx, byPlayer, level);
        MarkDirty(true);
        return true;
    }

    void GivePiece(int idx, IPlayer byPlayer, int level)
    {
        ItemStack stack = inventory[idx].TakeOutWhole();
        if (!byPlayer.InventoryManager.TryGiveItemstack(stack))
            Api.World.SpawnItemEntity(stack, Pos.ToVec3d().Add(0.5, 0.4 + 0.5 * level, 0.5));
        inventory[idx].MarkDirty();
        dryProgress[idx] = 0f;
    }

    public void DropContents() => inventory.DropAll(Pos.ToVec3d().Add(0.5, 0.5, 0.5));

    // ---------------- drying + perish ----------------

    void OnRackTick(float dt)
    {
        perishContainer.OnTick(dt);

        double now = Api.World.Calendar.TotalHours;
        double elapsed = now - lastCalendarHours;
        if (elapsed <= 0) return;

        ClimateCondition? cc = Api.World.BlockAccessor.GetClimateAt(Pos, EnumGetClimateMode.NowValues);
        if (cc == null) return;
        lastCalendarHours = now;

        bool openToSky = Pos.Y >= Api.World.BlockAccessor.GetRainMapHeightAt(Pos.X, Pos.Z);
        int sun = Api.World.BlockAccessor.GetLightLevel(Pos, EnumLightLevelType.OnlySunLight);
        float rate = ComputeDryingRate(cc, openToSky, sun);

        bool dirty = false;
        if (rate < 0f)
        {
            float delta = (float)(rate * elapsed);
            for (int i = 0; i < Slots; i++)
            {
                if (Match(inventory[i].Itemstack) is not DryingResult m || m.RequiresFire) continue;
                float p = dryProgress[i] + delta;
                dryProgress[i] = p < 0f ? 0f : p;
            }
        }
        else if (rate > 0f)
        {
            DryingResult?[] matches = new DryingResult?[Slots];
            for (int i = 0; i < Slots; i++)
                if (Match(inventory[i].Itemstack) is DryingResult m && !m.RequiresFire) matches[i] = m;

            const double step = 1.0;
            double remaining = elapsed;
            int safety = 200_000;
            while (remaining > 1e-6 && safety-- > 0)
            {
                double chunk = Math.Min(step, remaining);
                remaining -= chunk;
                float delta = (float)(rate * chunk);

                for (int i = 0; i < Slots; i++)
                {
                    if (matches[i] is not DryingResult m) continue;
                    float p = dryProgress[i] + delta;
                    if (p >= m.Hours)
                    {
                        inventory[i].Itemstack = new ItemStack(m.Output, m.Quantity);
                        inventory[i].MarkDirty();
                        dryProgress[i] = 0f;
                        matches[i] = null;
                        dirty = true;
                    }
                    else dryProgress[i] = p;
                }
            }
        }

        if (dirty) MarkDirty(true);
        else if (HasRackable()) MarkDirty();
    }

    float ComputeDryingRate(ClimateCondition cc, bool openToSky, int sunLight)
    {
        if (openToSky && cc.Rainfall > RainThreshold) return -BackslideRate;
        float sun01 = sunLight / 32f;
        float temp01 = GameMath.Clamp((cc.Temperature - TempMin) / (TempMax - TempMin), 0f, 1f);
        float dry01 = 1f - GameMath.Clamp(cc.WorldgenRainfall, 0f, 1f);
        return AirBaseRate * (sun01 + temp01 + dry01) / 3f;
    }

    // ---------------- rendering ----------------

    static readonly string[] FrameElements = { "bar-front", "bar-mid", "bar-back" };

    public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
    {
        if (capi == null) return false;
        AssetLocation? shapeBase = Block?.Shape?.Base;
        Shape? shape = shapeBase == null ? null : capi.TesselatorManager.GetCachedShape(shapeBase);
        if (shape == null) return false;

        if (HasBottom) RenderHalf(mesher, shape, 0);
        if (HasTop) RenderHalf(mesher, shape, 1);
        return true; // we own the mesh
    }

    void RenderHalf(ITerrainMeshPool mesher, Shape shape, int level)
    {
        float yOff = level * 0.5f;

        // Same-level horizontal neighbours decide the connecting frame; vertically-adjacent occupied
        // half-slots decide the upward post bridge.
        // Horizontal connections are gated on a CONVEX-RECTANGLE decomposition of this level's contiguous
        // group: a cell only merges with a neighbour that shares its rectangle, so concave corners land on
        // rectangle boundaries instead of trying to merge across them. The vertical post-bridge (`up`) is
        // computed independently and always on, so stacked racks ALWAYS read as connected regardless of the
        // horizontal grouping. (Experimental — comparing the feel vs the earlier all-adjacency merge.)
        var g = GroupMerges(level);
        bool n = g.n, e = g.e, s = g.s, w = g.w;
        bool nwDiag = g.nw, neDiag = g.ne, seDiag = g.se, swDiag = g.sw;
        // Vertical bridge to the next occupied half-slot above (always on — never gated by the horizontal
        // grouping, so stacks always read as connected). A bottom half continues into its own top half with a
        // half-height extension; a bottom half with NO top of its own but a rack directly above bridges the
        // whole empty block to it. A top half bridges the half block up to the cell above's bottom.
        bool rackAbove = HalfAt(0, 1, 0, 0);
        string? up = level == 0
            ? (HasTop ? "-up" : (rackAbove ? "-upfull" : null))
            : (rackAbove ? "-up" : null);

        List<string> frameElements = new(FrameElements);

        if (!w)
        {
            frameElements.Add("rail-left");
            if (n) frameElements.Add("rail-left-n");
            if (s) frameElements.Add("rail-left-s");
        }
        if (!e)
        {
            frameElements.Add("rail-right");
            if (n) frameElements.Add("rail-right-n");
            if (s) frameElements.Add("rail-right-s");
        }
        if (e)
        {
            frameElements.Add("rail-seam-e");
            if (n) frameElements.Add("rail-seam-e-n");
            if (s) frameElements.Add("rail-seam-e-s");
        }

        if (!w) { frameElements.Add("lash-fl"); frameElements.Add("lash-ml"); frameElements.Add("lash-bl"); }
        else { frameElements.Add("lash-fl-w"); frameElements.Add("lash-ml-w"); frameElements.Add("lash-bl-w"); }
        if (!e) { frameElements.Add("lash-fr"); frameElements.Add("lash-mr"); frameElements.Add("lash-br"); }
        else { frameElements.Add("lash-fr-e"); frameElements.Add("lash-mr-e"); frameElements.Add("lash-br-e"); }

        if (s)
        {
            frameElements.Add("bar-seam-s");
            if (!w) frameElements.Add("lash-seam-l"); else frameElements.Add("lash-seam-l-w");
            if (!e) frameElements.Add("lash-seam-r"); else frameElements.Add("lash-seam-r-e");
            if (e) frameElements.Add("bar-seam-s-e");
            if (w) frameElements.Add("bar-seam-s-w");
        }

        if (w) { frameElements.Add("bar-front-w"); frameElements.Add("bar-mid-w"); frameElements.Add("bar-back-w"); }
        if (e) { frameElements.Add("bar-front-e"); frameElements.Add("bar-mid-e"); frameElements.Add("bar-back-e"); }

        capi!.Tesselator.TesselateShape(Block, shape, out MeshData frame, null, null, frameElements.ToArray());
        HalfTransform(frame, yOff);
        frame.RenderPassesAndExtraBits.Fill((short)EnumChunkRenderPass.OpaqueNoCull);
        mesher.AddMeshData(frame);

        // Per-vertex leg ownership: each corner is drawn by exactly the most-north-west rack touching it, and
        // slid onto the shared boundary toward EVERY rack that meets there (orthogonal neighbours AND the
        // diagonal). One rule merges convex and concave corners alike — every shared vertex gets a single
        // centred post, with no gaps and no doubling.
        //   own:   NW only if nothing more-NW (no N/W/NW); NE unless an N neighbour; SW unless a W neighbour;
        //          SE always (this cell is the most-NW of its SE-vertex quad).
        //   slide: +x toward an east-side rack (E or the corner's diagonal), +z toward a south-side rack, etc.
        const float seam = 3f / 16f;
        AddPost(mesher, shape, "post-nw", !n && !w && !nwDiag, 0f, 0f, up, yOff);
        AddPost(mesher, shape, "post-ne", !n, (e || neDiag) ? seam : 0f, neDiag ? -seam : 0f, up, yOff);
        AddPost(mesher, shape, "post-sw", !w, swDiag ? -seam : 0f, (s || swDiag) ? seam : 0f, up, yOff);
        AddPost(mesher, shape, "post-se", true, (e || seDiag) ? seam : 0f, (s || seDiag) ? seam : 0f, up, yOff);

        RenderHalfItems(mesher, level, yOff);
    }

    // Squash a full-height frame mesh to half height and lift it onto the right half.
    static void HalfTransform(MeshData mesh, float yOff)
    {
        mesh.Scale(new Vec3f(0f, 0f, 0f), 1f, 0.5f, 1f);
        if (yOff != 0f) mesh.Translate(0f, yOff, 0f);
    }

    void AddPost(ITerrainMeshPool mesher, Shape shape, string element, bool render, float dx, float dz, string? upElem, float yOff)
    {
        if (!render || capi == null) return;
        // upElem (null / "-up" / "-upfull") is the upward post extension that bridges to the next occupied
        // half-slot above: half a block up to this cell's own top, or a full block up to the rack directly
        // above when this cell has no top of its own.
        string[] elements = upElem != null ? new[] { element, element + upElem } : new[] { element };
        capi.Tesselator.TesselateShape(Block, shape, out MeshData mesh, null, null, elements);
        mesh.Scale(new Vec3f(0f, 0f, 0f), 1f, 0.5f, 1f);
        mesh.Translate(dx, yOff, dz);
        mesh.RenderPassesAndExtraBits.Fill((short)EnumChunkRenderPass.OpaqueNoCull);
        mesher.AddMeshData(mesh);
    }

    void RenderHalfItems(ITerrainMeshPool mesher, int level, float yOff)
    {
        int b = Base(level);
        Vec3f center = new Vec3f(0.5f, 0f, 0.5f);
        for (int i = 0; i < HalfSlots; i++)
        {
            ItemStack? stack = inventory[b + i].Itemstack;
            if (stack?.Item == null) continue;

            nowTesselatingObj = stack.Item;
            nowTesselatingShape = stack.Item.Shape?.Base != null ? capi!.TesselatorManager.GetCachedShape(stack.Item.Shape.Base) : null;
            capi!.Tesselator.TesselateItem(stack.Item, out MeshData mesh, this);
            mesh.RenderPassesAndExtraBits.Fill((short)EnumChunkRenderPass.Opaque);

            // Per-recipe render override (scale / [x,y,z] offset), else the rack defaults.
            RenderTransform? rt = (recipes ??= Api.ModLoader.GetModSystem<PemmicanModSystem>())?.RenderFor(stack);
            float scale = rt?.Scale ?? ItemScale;
            float[]? t = rt?.Translation;
            float tx = t != null && t.Length > 0 ? t[0] : 0f;
            float ty = t != null && t.Length > 1 ? t[1] : 0f;
            float tz = t != null && t.Length > 2 ? t[2] : 0f;
            mesh.Scale(center, scale, scale, scale);
            mesh.Translate(PosX[i] - 0.5f + tx, yOff + ItemY + ty, PosZ[i] - 0.5f + tz);
            mesher.AddMeshData(mesh);
        }
    }

    bool HalfAt(int dx, int dy, int dz, int level)
        => Api.World.BlockAccessor.GetBlockEntity(Pos.AddCopy(dx, dy, dz)) is BlockEntityDryingRack be && be.HasHalf(level);

    bool HalfAtAbs(int x, int z, int level)
        => Api.World.BlockAccessor.GetBlockEntity(Pos.AddCopy(x - Pos.X, 0, z - Pos.Z)) is BlockEntityDryingRack be && be.HasHalf(level);

    // A signature of the rack column at (x,z): the occupied half-slots (none/bottom/top/both) at each Y in a
    // bounded window around this cell, by ABSOLUTE Y. Two columns share a signature iff they're stacked
    // identically — the test for whether two adjacent columns may merge horizontally. Cached per (x,z).
    string ColumnSig(int x, int z, Dictionary<long, string> cache)
    {
        long k = Key(x, z);
        if (cache.TryGetValue(k, out string? cached)) return cached;
        StringBuilder sb = new();
        for (int dy = -16; dy <= 16; dy++)
        {
            int bits = 0;
            if (Api.World.BlockAccessor.GetBlockEntity(Pos.AddCopy(x - Pos.X, dy, z - Pos.Z)) is BlockEntityDryingRack be)
                bits = (be.HasBottom ? 1 : 0) | (be.HasTop ? 2 : 0);
            sb.Append((char)('0' + bits));
        }
        string sig = sb.ToString();
        cache[k] = sig;
        return sig;
    }

    static long Key(int x, int z) => ((long)x << 32) | (uint)z;
    static (int x, int z) UnKey(long k) => ((int)(k >> 32), (int)(uint)k);

    // Which orthogonal/diagonal same-level neighbours this cell MERGES with, per a convex-rectangle
    // decomposition of the contiguous group: flood-fill the group, carve it into maximal rectangles in a
    // canonical top-left scan, then a neighbour "merges" only if it lands in the same rectangle as us. So a
    // straight run/rectangle stays one piece, while an L/T/+ splits at the concave corner onto a boundary.
    (bool n, bool e, bool s, bool w, bool ne, bool nw, bool se, bool sw) GroupMerges(int level)
    {
        int cx = Pos.X, cz = Pos.Z;

        // 1) flood-fill the contiguous group, but only across neighbours whose VERTICAL COLUMN PROFILE matches
        //    ours. So a height-2 stack never merges with an adjacent height-1 rack, and — because every column
        //    in a group then has the same profile — every Y-level of the group shares one footprint, keeping
        //    stacked legs aligned so the vertical bridges connect.
        Dictionary<long, string> sigCache = new();
        string startSig = ColumnSig(cx, cz, sigCache);
        HashSet<long> cells = new() { Key(cx, cz) };
        Queue<(int x, int z)> q = new();
        q.Enqueue((cx, cz));
        const int cap = 400;
        int[] ox = { 1, -1, 0, 0 }, oz = { 0, 0, 1, -1 };
        while (q.Count > 0 && cells.Count < cap)
        {
            var (x, z) = q.Dequeue();
            for (int i = 0; i < 4; i++)
            {
                int nx = x + ox[i], nz = z + oz[i];
                long k = Key(nx, nz);
                if (cells.Contains(k) || !HalfAtAbs(nx, nz, level)) continue;
                if (ColumnSig(nx, nz, sigCache) != startSig) continue; // only same-profile columns merge
                cells.Add(k);
                q.Enqueue((nx, nz));
            }
        }

        // 2) canonical maximal-rectangle decomposition (scan north-west to south-east; grow E then S).
        Dictionary<long, int> rectOf = new();
        List<(int x, int z)> sorted = new();
        foreach (long k in cells) sorted.Add(UnKey(k));
        sorted.Sort((a, b) => a.z != b.z ? a.z - b.z : a.x - b.x);
        int rid = 0;
        foreach (var (x0, z0) in sorted)
        {
            if (rectOf.ContainsKey(Key(x0, z0))) continue;
            int rw = 1;
            while (cells.Contains(Key(x0 + rw, z0)) && !rectOf.ContainsKey(Key(x0 + rw, z0))) rw++;
            int rh = 1;
            while (true)
            {
                int z = z0 + rh; bool ok = true;
                for (int i = 0; i < rw; i++)
                    if (!cells.Contains(Key(x0 + i, z)) || rectOf.ContainsKey(Key(x0 + i, z))) { ok = false; break; }
                if (!ok) break;
                rh++;
            }
            for (int dz = 0; dz < rh; dz++)
                for (int dx = 0; dx < rw; dx++)
                    rectOf[Key(x0 + dx, z0 + dz)] = rid;
            rid++;
        }

        // 3) a neighbour merges only when it's in our rectangle.
        int myRect = rectOf.TryGetValue(Key(cx, cz), out int r) ? r : -1;
        bool Same(int dx, int dz) => rectOf.TryGetValue(Key(cx + dx, cz + dz), out int rr) && rr == myRect;
        return (Same(0, -1), Same(1, 0), Same(0, 1), Same(-1, 0), Same(1, -1), Same(-1, -1), Same(1, 1), Same(-1, 1));
    }

    void RemeshNeighbours()
    {
        RemeshGroup(Api.World, Pos);
    }

    // Re-mesh every rack in the whole face-connected structure around `origin` (3D). A change to one column's
    // height alters its profile, which can make it merge/un-merge from horizontal neighbours at every level,
    // so the safe scope is the entire connected blob. Bounded for safety.
    public static void RemeshGroup(IWorldAccessor world, BlockPos origin)
    {
        IBlockAccessor ba = world.BlockAccessor;
        HashSet<string> seen = new();
        Queue<BlockPos> queue = new();
        void Visit(BlockPos p)
        {
            if (!seen.Add($"{p.X},{p.Y},{p.Z}")) return;
            if (ba.GetBlockEntity(p) is BlockEntityDryingRack be) { be.MarkDirty(true); queue.Enqueue(p); }
        }
        Visit(origin); // may be gone (break) — its neighbours seed the remaining structure
        foreach (BlockFacing f in BlockFacing.ALLFACES) Visit(origin.AddCopy(f));
        const int cap = 500;
        while (queue.Count > 0 && seen.Count < cap)
        {
            BlockPos p = queue.Dequeue();
            foreach (BlockFacing f in BlockFacing.ALLFACES) Visit(p.AddCopy(f));
        }
    }

    // ---------------- ITexPositionSource ----------------

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
            if (pos == null) { capi.BlockTextureAtlas.GetOrInsertTexture(path, out int _, out TextureAtlasPosition inserted); pos = inserted; }
            return pos ?? capi.BlockTextureAtlas.UnknownTexturePosition;
        }
    }

    // ---------------- info / persistence ----------------

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder sb)
    {
        base.GetBlockInfo(forPlayer, sb);

        // Selection index layout: half*9 + local, local 0 = that half's frame, local 1..8 = its hang spots.
        int box = forPlayer.CurrentBlockSelection?.SelectionBoxIndex ?? -1;
        int level = box >= 9 ? 1 : 0;
        int local = box >= 0 ? box % 9 : 0;
        int slot = local >= 1 && local <= HalfSlots ? local : 0;

        if (slot > 0 && HasHalf(level))
        {
            int idx = Base(level) + slot - 1;
            if (!inventory[idx].Empty)
            {
                ItemStack stack = inventory[idx].Itemstack;
                sb.AppendLine(stack.GetName());
                if (Match(stack) is DryingResult m && !m.RequiresFire && m.Hours > 0)
                    sb.AppendLine(Lang.Get("pemmican:dryingrack-drying", (int)(GameMath.Clamp(dryProgress[idx] / (float)m.Hours, 0f, 1f) * 100)));
                float sp = SpoilLevel(inventory[idx]);
                if (sp > 0f) sb.AppendLine(Lang.Get("pemmican:dryingrack-spoilage", (int)(sp * 100)));
                return;
            }
        }

        int racked = CountRacked(0) + CountRacked(1);
        int capacity = (HasBottom ? HalfSlots : 0) + (HasTop ? HalfSlots : 0);
        sb.AppendLine(racked == 0
            ? Lang.Get("pemmican:smokerack-empty")
            : Lang.Get("pemmican:smokerack-contents", racked, capacity));

        double drySum = 0, spoilSum = 0;
        int dryN = 0, spoilN = 0;
        for (int i = 0; i < Slots; i++)
        {
            if (inventory[i].Empty) continue;
            if (Match(inventory[i].Itemstack) is DryingResult m && !m.RequiresFire && m.Hours > 0)
            {
                drySum += GameMath.Clamp(dryProgress[i] / (float)m.Hours, 0f, 1f);
                dryN++;
            }
            spoilSum += SpoilLevel(inventory[i]);
            spoilN++;
        }
        if (dryN > 0) sb.AppendLine(Lang.Get("pemmican:dryingrack-drying-avg", (int)(drySum / dryN * 100)));
        if (spoilN > 0 && spoilSum > 0) sb.AppendLine(Lang.Get("pemmican:dryingrack-spoilage-avg", (int)(spoilSum / spoilN * 100)));
    }

    float SpoilLevel(ItemSlot slot)
    {
        TransitionState? ts = slot.Itemstack?.Collectible.UpdateAndGetTransitionState(Api.World, slot, EnumTransitionType.Perish);
        return ts?.TransitionLevel ?? 0f;
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        ITreeAttribute invTree = new TreeAttribute();
        inventory.ToTreeAttributes(invTree);
        tree["inventory"] = invTree;
        tree.SetDouble("lastCalendarHours", lastCalendarHours);
        tree.SetBool("hasBottom", HasBottom);
        tree.SetBool("hasTop", HasTop);
        tree["dryProgress"] = new FloatArrayAttribute(dryProgress);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        ITreeAttribute? invTree = tree.GetTreeAttribute("inventory");
        if (invTree != null) inventory.FromTreeAttributes(invTree);
        lastCalendarHours = tree.GetDouble("lastCalendarHours", -1);
        HasBottom = tree.GetBool("hasBottom");
        HasTop = tree.GetBool("hasTop");
        if (tree["dryProgress"] is FloatArrayAttribute arr && arr.value?.Length == dryProgress.Length)
            dryProgress = arr.value;
        if (Api?.Side == EnumAppSide.Client) MarkDirty(true);
    }
}
