using System;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace Ehm93.VS.Primitive.Kiln;

// A brick clamp is a contiguous blob of rawbrick ground-storage piles, covered in soil and fired like a
// charcoal mound (see the vanilla BlockEntityCharcoalPit it's modelled on). This behavior is patched onto
// every ground-storage block but stays dormant unless its pile holds rawbricks and a player activates it.
//
// One pile in the blob is elected the "controller": it holds the pooled fuel and the firing countdown, runs
// the server tick, and on completion walks the whole blob swapping each pile's rawbrick-* for the smelted
// burnedbrick-* in place. Other piles stay dormant.
//
// Flow (mirrors the charcoal mound): stack rawbrick piles -> right-click with fuel to pool burn-seconds ->
// right-click with a torch to light -> cover everything in soil. Firing only advances while sealed.
public class BEBehaviorBrickClamp : BlockEntityBehavior
{
    // Fuel items consumed per Ctrl+click (matches the rawbrick GroundStorable bulkTransferQuantity).
    private const int CtrlClickFuelItems = 4;

    private bool isController;
    private float fuelSeconds;
    private string fuelCode = "";   // code path of the loaded fuel (propagated to the render hints below)
    private float fuelTarget;       // required fuel at load time

    // Exactly what was loaded (full item code -> count), so removing bricks from an unlit clamp can refund the
    // fuel as the same items. Cleared when the clamp fires (the fuel is spent).
    private readonly Dictionary<string, int> fuelItems = new();
    private bool lit;
    private double firingFinishHours;  // calendar-hour deadline; fires when TotalHours reaches it (advances even
                                       // while the chunk is unloaded, so a multi-day firing doesn't need babysitting)
    private double lastTickHours;      // bookmark, used only to push the deadline back while unsealed (pause)
    private long tickListenerId;

    // Per-pile render hints the controller pushes to every pile in the blob, so all piles show fuel (the logic
    // fields above live only on the controller). renderLevel 0 == nothing drawn; renderLit == firing (glowing
    // ember texture + smoke).
    private string renderFuelCode = "";
    private float renderLevel;
    private bool renderLit;
    private long clientTickId;   // client smoke-particle tick, alive only while renderLit

    // Smoke that seeps from a firing clamp, like a charcoal mound. Shared instance; MinPos is set per emit.
    private static readonly SimpleParticleProperties SmokeParticles = new(
        1, 1, ColorUtil.ToRgba(150, 80, 80, 80),
        new Vec3d(), new Vec3d(),
        new Vec3f(-0.05f, 0.2f, -0.05f), new Vec3f(0.05f, 0.5f, 0.05f),
        2.5f, -0.02f, 0.4f, 0.9f, EnumParticleModel.Quad)
    {
        SelfPropelled = true,
        AddPos = new Vec3d(0.4, 0.0, 0.4),
        OpacityEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, -180f),
        SizeEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, 1.5f)
    };

    public BEBehaviorBrickClamp(BlockEntity blockentity) : base(blockentity) { }

    private KilnConfig Config => Api.ModLoader.GetModSystem<KilnModSystem>().Config;

    private BlockEntityGroundStorage? Storage => Blockentity as BlockEntityGroundStorage;

    private ItemStack? StoredStack => Storage?.Inventory?[0]?.Itemstack;

    private bool HoldsRawBrick => IsRawBrick(StoredStack);

    private static bool IsRawBrick(ItemStack? stack) =>
        stack?.Collectible?.Code?.Path?.StartsWith("rawbrick-") == true;

    // ---------------- interaction (called from the Harmony prefix) ----------------

    // Returns false to consume the click (we handled it), true to let vanilla put/take run.
    public bool OnInteract(IPlayer player, BlockSelection bs, ref bool __result)
    {
        if (!HoldsRawBrick) return true;

        ItemSlot hand = player.InventoryManager.ActiveHotbarSlot;
        if (hand == null || hand.Empty) return true;

        if (IsIgniter(hand.Itemstack))
        {
            if (Api.Side == EnumAppSide.Server) TryIgnite(player);
            __result = true;
            return false;
        }

        if (IsFuel(hand.Itemstack))
        {
            if (Api.Side == EnumAppSide.Server) TryLoadFuel(hand, player);
            __result = true;
            return false;
        }

        // A combustible that isn't an accepted clamp fuel: warn instead of silently doing nothing.
        if (FuelSecondsOf(hand.Itemstack) > 0)
        {
            if (Api.Side == EnumAppSide.Server) Tell(player, "primitivekiln:clamp-badfuel");
            __result = true;
            return false;
        }

        return true; // anything else (more rawbricks, a non-fuel item) -> vanilla
    }

    private static bool IsIgniter(ItemStack stack)
    {
        string path = stack.Collectible?.Code?.Path ?? "";
        return path.Contains("torch") || path.Contains("firestarter");
    }

    // A fuel item contributes its own combustible burn duration (in seconds). Rawbricks have combustible
    // props too (a melting point, no burn duration), so they correctly read as 0 and fall through to vanilla.
    // Internal: the updraft kiln (BlockEntityKilnBase) shares the clamp's fuel rules.
    internal static float FuelSecondsOf(ItemStack? stack)
    {
        CombustibleProperties? cp = stack?.Collectible?.CombustibleProps;
        return cp != null && cp.BurnDuration > 0 && cp.BurnTemperature > 0 ? cp.BurnDuration : 0f;
    }

    // The clamp only accepts a fixed set of sensible firing fuels — wood, charcoal, the coal family, coke and
    // peat — rather than every combustible item (chairs, saplings, planks, …). Matched by code substring, so
    // "firewood" also covers "agedfirewood", "coal" covers "coal-contaminated", etc.
    private static readonly string[] FuelCodes =
        { "firewood", "charcoal", "coke", "coal", "anthracite", "lignite", "bituminous", "peat" };

    internal static bool IsFuel(ItemStack? stack)
    {
        if (FuelSecondsOf(stack) <= 0) return false;
        string code = stack!.Collectible.Code?.Path ?? "";
        foreach (string f in FuelCodes) if (code.Contains(f)) return true;
        return false;
    }

    // Each fuel's characteristic place sound lives in attributes.placeSound (charcoal/coke/coal/ore ->
    // block/charcoal, peat -> block/dirt, firewood -> block/planks); GroundStorable's placeRemoveSound is
    // only a fallback since most fuels don't declare it. The sounds/ prefix is required or the asset
    // silently fails to resolve.
    internal static AssetLocation FuelPlaceSound(ItemStack? stack)
    {
        if (stack == null) return new AssetLocation("game", "sounds/player/build");
        string? attrSound = stack.Collectible.Attributes?["placeSound"].AsString();
        return (attrSound != null ? AssetLocation.Create(attrSound, stack.Collectible.Code.Domain)
                : stack.Collectible.GetCollectibleBehavior<CollectibleBehaviorGroundStorable>(true)?.StorageProps?.PlaceRemoveSound
                ?? new AssetLocation("game", "sounds/player/build"))
            .WithPathPrefixOnce("sounds/");
    }

    // ---------------- fuel & ignition (server) ----------------

    private void TryLoadFuel(ItemSlot hand, IPlayer player)
    {
        List<BlockPos> cells = FloodFillCells();
        if (!AllPilesFull(cells))
        {
            Tell(player, "primitivekiln:clamp-notfull");
            return;
        }

        BEBehaviorBrickClamp controller = FindOrElectController(cells);

        float required = cells.Count * Config.FuelSecondsPerPile;
        float deficit = required - controller.fuelSeconds;
        if (deficit <= 0)
        {
            Tell(player, "primitivekiln:clamp-full", (int)controller.fuelSeconds, (int)required);
            return;
        }

        // One fuel item per click, or a batch with Ctrl held — never more than tops off the requirement.
        float per = FuelSecondsOf(hand.Itemstack);
        int batch = player.Entity.Controls.CtrlKey ? CtrlClickFuelItems : 1;
        int needed = (int)Math.Ceiling(deficit / per);
        int take = Math.Min(batch, Math.Min(needed, hand.StackSize));
        if (take <= 0) return;

        controller.fuelSeconds += take * per;
        controller.fuelCode = hand.Itemstack!.Collectible.Code.Path;
        controller.fuelTarget = required;
        string itemCode = hand.Itemstack.Collectible.Code.ToShortString();
        controller.fuelItems.TryGetValue(itemCode, out int already);
        controller.fuelItems[itemCode] = already + take;

        // Captured before TakeOut — the slot may empty.
        AssetLocation sound = FuelPlaceSound(hand.Itemstack);

        if (player.WorldData.CurrentGameMode != EnumGameMode.Creative)
        {
            hand.TakeOut(take);
            hand.MarkDirty();
        }

        Api.World.PlaySoundAt(sound, Pos, 0);
        float level = required > 0 ? GameMath.Clamp(controller.fuelSeconds / required, 0f, 1f) : 0.5f;
        controller.PropagateRender(cells, controller.fuelCode, level, false); // marks every pile dirty, incl. controller
        Tell(player, "primitivekiln:clamp-loaded", (int)controller.fuelSeconds, (int)required);
    }

    private void TryIgnite(IPlayer player)
    {
        List<BlockPos> cells = FloodFillCells();
        BEBehaviorBrickClamp controller = FindOrElectController(cells);
        if (controller != this) { controller.TryIgnite(player); return; }
        if (lit) return;

        if (!AllPilesFull(cells))
        {
            Tell(player, "primitivekiln:clamp-notfull");
            return;
        }

        float required = cells.Count * Config.FuelSecondsPerPile;
        if (fuelSeconds < required)
        {
            Tell(player, "primitivekiln:clamp-needfuel");
            return;
        }

        lit = true;
        isController = true;
        lastTickHours = Api.World.Calendar.TotalHours;
        firingFinishHours = lastTickHours + Config.FireHours; // flat firing time regardless of clamp size
        RegisterTick();

        // Switch every pile to the glowing ember texture and start the smoke — no sound; the ember glow and
        // smoke are the lighting feedback.
        float level = fuelTarget > 0 ? GameMath.Clamp(fuelSeconds / fuelTarget, 0.12f, 1f) : 1f;
        PropagateRender(cells, fuelCode, level, true);
        Blockentity.MarkDirty(true);
    }

    // ---------------- firing ----------------

    private void OnServerTick(float dt)
    {
        if (!lit) return;

        double now = Api.World.Calendar.TotalHours;
        double elapsed = now - lastTickHours;
        lastTickHours = now;

        List<BlockPos> cells = FloodFillCells();
        if (!IsSealed(cells))
        {
            // Unsealed: pause by pushing the deadline back by the elapsed (loaded) time.
            firingFinishHours += Math.Max(0, elapsed);
            Blockentity.MarkDirty();
            return;
        }

        if (now >= firingFinishHours) Convert(cells);
        else Blockentity.MarkDirty();
    }

    // Walk the blob and swap each rawbrick pile for the smelted burnedbrick stack in place.
    private void Convert(List<BlockPos> cells)
    {
        foreach (BlockPos pos in cells)
        {
            var be = Api.World.BlockAccessor.GetBlockEntity(pos) as BlockEntityGroundStorage;
            ItemSlot? slot = be?.Inventory?[0];
            ItemStack? stack = slot?.Itemstack;
            if (!IsRawBrick(stack)) continue;

            CombustibleProperties? cp = stack!.Collectible.CombustibleProps;
            ItemStack? smelted = cp?.SmeltedStack?.ResolvedItemstack;
            if (smelted == null) continue;

            int ratio = Math.Max(1, cp!.SmeltedRatio);
            int outCount = stack.StackSize / ratio;
            // A rough clamp wastes some bricks (vs a proper kiln) — lose a random 10-20% of each pile.
            float lossFrac = Config.BrickLossMin + (float)Api.World.Rand.NextDouble() * (Config.BrickLossMax - Config.BrickLossMin);
            outCount -= (int)Math.Round(outCount * lossFrac);

            if (outCount <= 0)
            {
                slot!.Itemstack = null; // whole (small) pile lost
            }
            else
            {
                ItemStack outStack = smelted.Clone();
                outStack.StackSize = outCount;
                slot!.Itemstack = outStack;
            }
            slot.MarkDirty();
            be!.MarkDirty(true);
        }

        PropagateRender(cells, "", 0f, false); // clear the fuel meshes + ember/smoke from every pile
        lit = false;
        isController = false;
        fuelSeconds = 0;
        fuelCode = "";
        fuelTarget = 0;
        fuelItems.Clear(); // spent, not refundable

        firingFinishHours = 0;
        UnregisterTick();
        Blockentity.MarkDirty(true);
    }

    // ---------------- rendering ----------------

    // Insert the fuel textures into the block atlas on the MAIN thread. Most (coke, charcoal, ember, …) aren't
    // used by any block, so they aren't pre-loaded; GetOrInsertTexture from the tesselation thread (where
    // OnTesselation runs) can't insert a new texture and returns the magenta unknown. Doing it here means the
    // tess-thread lookup in OnTesselation finds them. Called from KilnModSystem on BlockTexturesLoaded.
    public static void PreloadFuelTextures(ICoreClientAPI capi)
    {
        foreach (string path in new[] { "block/soil/peat", "block/coal/charcoal", "block/coal/coke",
                                        "block/coal/bituminous", "block/wood/firewood/side", "block/coal/ember",
                                        "block/coal/orecoalmix" })
        {
            capi.BlockTextureAtlas.GetOrInsertTexture(new AssetLocation("game", path), out int _, out TextureAtlasPosition _);
        }
    }

    // BlockEntityGroundStorage.OnTesselation DOES call base, so this behavior override fires. Draw the fuel as
    // a representative cube poking out the top of the pile (a full rawbrick pile fills the cell, so a cube
    // inside would be hidden). renderLevel/renderFuelCode are pushed to every pile by the controller, so all
    // piles in the clamp show fuel, not just the one holding the pooled total.
    public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tesselator)
    {
        bool baseResult = base.OnTesselation(mesher, tesselator);
        if (Api is not ICoreClientAPI capi || renderLevel <= 0) return baseResult;

        // While firing, all piles glow as ember; otherwise the loaded fuel's own texture.
        string texPath = renderLit ? "block/coal/ember" : FuelTexturePath(renderFuelCode);
        AssetLocation loc = new AssetLocation("game", texPath);
        TextureAtlasPosition? texPos = capi.BlockTextureAtlas[loc];
        if (texPos == null)
        {
            capi.BlockTextureAtlas.GetOrInsertTexture(loc, out int _, out TextureAtlasPosition inserted);
            texPos = inserted;
        }
        if (texPos == null) return baseResult;

        // Tesselate a solid block to get a COMPLETE, correctly-lit terrain mesh — a hand-built CubeMeshUtil cube
        // is missing the per-face/light data the terrain pool needs and silently emits nothing (confirmed via the
        // decompiled mesher). Then retexture every face to the fuel and shrink it to an inset cube that shows
        // through the gaps between the bricks.
        Block? baseBlock = capi.World.GetBlock(new AssetLocation("game", "rock-granite"));
        if (baseBlock == null) return baseResult;
        tesselator.TesselateBlock(baseBlock, out MeshData cube);
        cube.SetTexPos(texPos);

        float fill = GameMath.Clamp(renderLevel, 0.12f, 1f);
        float height = 0.05f + fill * 0.85f;   // fills upward from the floor as more fuel is loaded
        cube.Scale(new Vec3f(0.5f, 0f, 0.5f), 0.84f, height, 0.84f); // ~0.08 inset on X/Z, scaled to fill height
        cube.Translate(0f, 0.05f, 0f);
        mesher.AddMeshData(cube);
        return baseResult;
    }

    // Push the fuel render state to every pile in the blob (and mark each for retesselation) so all of them
    // show fuel, not just the controller. Empty code / level 0 clears it.
    private void PropagateRender(List<BlockPos> cells, string code, float level, bool litState)
    {
        foreach (BlockPos p in cells)
        {
            BEBehaviorBrickClamp? b = GetClampAt(p);
            if (b == null) continue;
            b.renderFuelCode = code;
            b.renderLevel = level;
            b.renderLit = litState;
            b.Blockentity.MarkDirty(true);
        }
    }

    // Representative block-atlas texture per fuel. Order matters: "charcoal" contains "coal", so it must be
    // tested before the coal family. Everything else — firewood, agedfirewood, logs, planks, sticks, drygrass,
    // furniture, … — is organic, so a plain wood texture is the catch-all (it also blends with the bricks).
    // Internal: the updraft kiln renders its combustion-chamber fuel with the same mapping (and relies on the
    // same PreloadFuelTextures call).
    internal static string FuelTexturePath(string code)
    {
        if (code.Contains("peat")) return "block/soil/peat";
        if (code.Contains("charcoal")) return "block/coal/charcoal";
        if (code.Contains("coke")) return "block/coal/coke";
        if (code.Contains("coal") || code.Contains("lignite") || code.Contains("bituminous") || code.Contains("anthracite"))
            return "block/coal/bituminous";
        return "block/wood/firewood/side";
    }

    // ---------------- structure scan ----------------

    // 6-connected BFS over contiguous rawbrick piles, capped to a MaxSize bounding cube (like WalkPit/InCube
    // on the charcoal mound). Returns every pile position in this blob.
    private List<BlockPos> FloodFillCells()
    {
        var visited = new HashSet<BlockPos>();
        var queue = new Queue<BlockPos>();
        var cells = new List<BlockPos>();
        int maxSize = Math.Max(1, Config.MaxSize);

        BlockPos start = Pos.Copy();
        visited.Add(start);
        queue.Enqueue(start);
        int minX = start.X, minY = start.Y, minZ = start.Z;
        int maxX = start.X, maxY = start.Y, maxZ = start.Z;

        while (queue.Count > 0)
        {
            BlockPos pos = queue.Dequeue();
            cells.Add(pos);

            foreach (BlockFacing f in BlockFacing.ALLFACES)
            {
                BlockPos np = pos.AddCopy(f);
                if (visited.Contains(np) || !IsRawBrickPile(np)) continue;

                int nMinX = Math.Min(minX, np.X), nMaxX = Math.Max(maxX, np.X);
                int nMinY = Math.Min(minY, np.Y), nMaxY = Math.Max(maxY, np.Y);
                int nMinZ = Math.Min(minZ, np.Z), nMaxZ = Math.Max(maxZ, np.Z);
                if (nMaxX - nMinX + 1 > maxSize || nMaxY - nMinY + 1 > maxSize || nMaxZ - nMinZ + 1 > maxSize)
                    continue;

                minX = nMinX; maxX = nMaxX; minY = nMinY; maxY = nMaxY; minZ = nMinZ; maxZ = nMaxZ;
                visited.Add(np);
                queue.Enqueue(np);
            }
        }
        return cells;
    }

    private bool IsRawBrickPile(BlockPos pos)
    {
        var be = Api.World.BlockAccessor.GetBlockEntity(pos) as BlockEntityGroundStorage;
        return IsRawBrick(be?.Inventory?[0]?.Itemstack);
    }

    // Every pile in the blob must hold a full stack (24 bricks) before fuel goes in — half-built clamps
    // would fire half-empty cells for the same fuel.
    private bool AllPilesFull(List<BlockPos> cells)
    {
        foreach (BlockPos p in cells)
        {
            if (Api.World.BlockAccessor.GetBlockEntity(p) is not BlockEntityGroundStorage be) return false;
            ItemSlot? slot = be.Inventory?[0];
            if (slot?.Itemstack == null || slot.StackSize < be.Capacity) return false;
        }
        return true;
    }

    // Called when bricks leave any pile in the blob (vanilla take, or a pile broken). An unlit clamp's piles
    // are no longer all-full, so its pooled fuel no longer matches a fireable clamp — give the fuel back.
    // A lit clamp keeps its fuel: it's already burning.
    public void OnBricksRemoved()
    {
        if (Api.Side != EnumAppSide.Server) return;
        List<BlockPos> cells = FloodFillCells();
        BEBehaviorBrickClamp? controller = FindControllerAmong(cells) ?? (isController ? this : null);
        if (controller == null || controller.lit || controller.fuelSeconds <= 0) return;
        controller.RefundFuel(cells);
    }

    private void RefundFuel(List<BlockPos> cells)
    {
        foreach (KeyValuePair<string, int> kv in fuelItems)
        {
            var loc = new AssetLocation(kv.Key);
            CollectibleObject? col = (CollectibleObject?)Api.World.GetItem(loc) ?? Api.World.GetBlock(loc);
            if (col == null) continue;

            int remaining = kv.Value;
            while (remaining > 0)
            {
                int n = Math.Min(remaining, col.MaxStackSize);
                remaining -= n;
                Api.World.SpawnItemEntity(new ItemStack(col, n), Pos.ToVec3d().Add(0.5, 0.7, 0.5));
            }
        }

        fuelItems.Clear();
        fuelSeconds = 0;
        fuelCode = "";
        fuelTarget = 0;
        isController = false;
        PropagateRender(cells, "", 0f, false);
        Blockentity.MarkDirty(true);
    }

    public override void OnBlockBroken(IPlayer? byPlayer = null)
    {
        base.OnBlockBroken(byPlayer);
        if (Api?.Side == EnumAppSide.Server && HoldsRawBrick) OnBricksRemoved();
    }

    private BEBehaviorBrickClamp? GetClampAt(BlockPos pos) =>
        (Api.World.BlockAccessor.GetBlockEntity(pos) as BlockEntityGroundStorage)?.GetBehavior<BEBehaviorBrickClamp>();

    // The blob's controller (the pile holding the pooled fuel / firing state), or null if none elected yet.
    private BEBehaviorBrickClamp? FindControllerAmong(List<BlockPos> cells)
    {
        foreach (BlockPos p in cells)
        {
            BEBehaviorBrickClamp? b = GetClampAt(p);
            if (b != null && b.isController) return b;
        }
        return null;
    }

    // Use an existing controller in the blob if there is one (so fuel/ignition route to the same place),
    // otherwise elect this pile.
    private BEBehaviorBrickClamp FindOrElectController(List<BlockPos> cells)
    {
        BEBehaviorBrickClamp? existing = FindControllerAmong(cells);
        if (existing != null) return existing;
        isController = true;
        return this;
    }

    // Every exposed face of a brick pile (one not touching another pile in the blob) must be fully covered by
    // a non-combustible solid (soil/sand/gravel/stone). Any air, gap, or flammable cover is a leak.
    private bool IsSealed(List<BlockPos> cells)
    {
        var cellSet = new HashSet<BlockPos>(cells);
        foreach (BlockPos pos in cells)
            foreach (BlockFacing f in BlockFacing.ALLFACES)
            {
                BlockPos np = pos.AddCopy(f);
                if (cellSet.Contains(np)) continue;
                if (!SealsSide(np, f)) return false;
            }
        return true;
    }

    private bool SealsSide(BlockPos np, BlockFacing f)
    {
        Block nb = Api.World.BlockAccessor.GetBlock(np);
        if (nb.Id == 0) return false;                                              // air
        if (nb.CombustibleProps != null && nb.CombustibleProps.BurnDuration > 0) return false; // flammable
        return nb.GetLiquidBarrierHeightOnSide(f.Opposite, np) >= 1f;              // fully blocks the side
    }

    // ---------------- ticking / lifecycle ----------------

    public override void Initialize(ICoreAPI api, JsonObject properties)
    {
        base.Initialize(api, properties);
        if (lit && api.Side == EnumAppSide.Server)
        {
            lastTickHours = api.World.Calendar.TotalHours;
            RegisterTick();
        }
        UpdateSmokeTick();
    }

    private void RegisterTick()
    {
        if (tickListenerId == 0 && Api.Side == EnumAppSide.Server)
            tickListenerId = Blockentity.RegisterGameTickListener(OnServerTick, 1000);
    }

    private void UnregisterTick()
    {
        if (tickListenerId != 0)
        {
            Blockentity.UnregisterGameTickListener(tickListenerId);
            tickListenerId = 0;
        }
        if (clientTickId != 0)
        {
            Blockentity.UnregisterGameTickListener(clientTickId);
            clientTickId = 0;
        }
    }

    // Client-only smoke tick, alive only while this pile is part of a firing clamp (renderLit). Driven off the
    // synced render state, so it starts/stops as the controller propagates ignite/convert.
    private void UpdateSmokeTick()
    {
        if (Api?.Side != EnumAppSide.Client) return;
        if (renderLit && clientTickId == 0)
            clientTickId = Blockentity.RegisterGameTickListener(OnClientSmoke, 150);
        else if (!renderLit && clientTickId != 0)
        {
            Blockentity.UnregisterGameTickListener(clientTickId);
            clientTickId = 0;
        }
    }

    private void OnClientSmoke(float dt)
    {
        if (!renderLit || Api.World.Rand.NextDouble() > 0.25) return;
        if (IsRawBrickPile(Pos.UpCopy())) return; // a pile sits directly on top — let that one smoke
        SmokeParticles.MinPos.Set(Pos.X + 0.3, Pos.Y + 1.0, Pos.Z + 0.3);
        Api.World.SpawnParticles(SmokeParticles);
    }

    public override void OnBlockRemoved()
    {
        base.OnBlockRemoved();
        UnregisterTick();
    }

    public override void OnBlockUnloaded()
    {
        base.OnBlockUnloaded();
        UnregisterTick();
    }

    private static void Tell(IPlayer player, string langKey, params object[] args) =>
        (player as IServerPlayer)?.SendIngameError("primitivekiln", Lang.Get(langKey, args));

    // ---------------- persistence & info ----------------

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        if (!isController && !lit && fuelSeconds == 0 && renderLevel <= 0 && !renderLit) return; // dormant pile

        tree.SetBool("clampCtrl", isController);
        tree.SetBool("clampLit", lit);
        tree.SetFloat("clampFuel", fuelSeconds);
        tree.SetString("clampFuelCode", fuelCode);
        tree.SetFloat("clampFuelTarget", fuelTarget);
        var parts = new List<string>(fuelItems.Count);
        foreach (KeyValuePair<string, int> kv in fuelItems) parts.Add(kv.Key + "=" + kv.Value);
        tree.SetString("clampFuelItems", string.Join(",", parts));
        tree.SetDouble("clampFinish", firingFinishHours);
        tree.SetString("clampRenderCode", renderFuelCode);
        tree.SetFloat("clampRenderLevel", renderLevel);
        tree.SetBool("clampRenderLit", renderLit);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolve)
    {
        base.FromTreeAttributes(tree, worldForResolve);
        isController = tree.GetBool("clampCtrl", false);
        lit = tree.GetBool("clampLit", false);
        fuelSeconds = tree.GetFloat("clampFuel", 0f);
        fuelCode = tree.GetString("clampFuelCode", "") ?? "";
        fuelTarget = tree.GetFloat("clampFuelTarget", 0f);
        fuelItems.Clear();
        string joined = tree.GetString("clampFuelItems", "") ?? "";
        foreach (string part in joined.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = part.LastIndexOf('=');
            if (eq > 0 && int.TryParse(part[(eq + 1)..], out int n)) fuelItems[part[..eq]] = n;
        }
        firingFinishHours = tree.GetDouble("clampFinish", 0.0);
        renderFuelCode = tree.GetString("clampRenderCode", "") ?? "";
        renderLevel = tree.GetFloat("clampRenderLevel", 0f);
        renderLit = tree.GetBool("clampRenderLit", false);
        UpdateSmokeTick(); // start/stop the client smoke tick as firing state syncs in
    }

    // Shown on any rawbrick pile (the fuel/firing state lives on the blob's controller, so look it up rather
    // than only reporting on the controller cell itself). Called from a Harmony postfix on
    // BlockEntityGroundStorage.GetBlockInfo, which does NOT call base — so this behavior's own GetBlockInfo
    // override would never fire.
    public void AppendClampInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        if (!HoldsRawBrick) return;

        List<BlockPos> cells = FloodFillCells();
        BEBehaviorBrickClamp? controller = FindControllerAmong(cells);

        if (controller != null && controller.lit)
        {
            double remaining = Math.Max(0, controller.firingFinishHours - Api.World.Calendar.TotalHours);
            dsc.AppendLine(Lang.Get("primitivekiln:clamp-firing", FormatHours(remaining)));
            if (!IsSealed(cells)) dsc.AppendLine(Lang.Get("primitivekiln:clamp-unsealed"));
            return;
        }

        int required = (int)Math.Round(cells.Count * Config.FuelSecondsPerPile);
        int fuel = (int)Math.Round(controller?.fuelSeconds ?? 0f);
        if (fuel == 0 && !AllPilesFull(cells))
            dsc.AppendLine(Lang.Get("primitivekiln:clamp-fillfirst"));
        else if (fuel >= required)
            dsc.AppendLine(Lang.Get("primitivekiln:clamp-ready", cells.Count));
        else
            dsc.AppendLine(Lang.Get("primitivekiln:clamp-fuel", fuel, required, cells.Count));
    }

    private static string FormatHours(double hours)
    {
        if (hours >= 24) return $"{hours / 24:0.0} days";
        if (hours >= 1) return $"{hours:0.0}h";
        return $"{(int)Math.Ceiling(hours * 60)}m";
    }
}
