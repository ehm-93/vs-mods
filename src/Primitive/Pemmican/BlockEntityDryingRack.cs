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

// A standalone, fireless rack that air-dries its contents based on local climate. Unlike the smoking
// firepit (fast, reliable), this is slow and conditional: drying speed rises with sunlight, warmth and a
// dry climate; rain (when open to the sky) makes progress backslide; and meanwhile perishables keep
// rotting at the room-aware rate (a cellar slows rot but offers little sun, so it's the slow-but-safe
// option, while arid open air dries fast but races the rot clock). Built on the shared BlockEntityRack.
public class BlockEntityDryingRack : BlockEntityRack
{
    // --- drying-rate model (starter constants; tune in-game) ---
    const float AirBaseRate = 0.35f;   // fraction of fire-rack speed in perfect conditions (~1/3)
    const float BackslideRate = 0.20f; // progress lost per hour while being rained/snowed on
    const float TempMin = 0f;          // <= this many deg C contributes no warmth
    const float TempMax = 30f;         // >= this is full warmth
    const float RainThreshold = 0.04f; // live precipitation above this counts as "raining"

    InWorldContainer perishContainer;
    // Per-slot drying credit in in-game hours (index by slot 1..RackSlots; 0 unused). Persisted + synced.
    float[] dryProgress = new float[RackSlots + 1];

    protected override string InventoryId => "dryingrack";

    public BlockEntityDryingRack()
    {
        perishContainer = new InWorldContainer(() => Inventory, "dryrackinv");
    }

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        // Room-aware perish: this wires inventory.OnAcquireTransitionSpeed + drives transitions on OnTick.
        perishContainer.Init(Api, () => Pos, () => MarkDirty(true));
        if (api.Side == EnumAppSide.Server) RegisterGameTickListener(OnRackTick, 10000);
    }

    void OnRackTick(float dt)
    {
        // 1) Perish: room-aware, self-tracked off the calendar clock (server-only internally). A cellar
        //    slows rot; warm open air rots fast. Rots in place when fresh-hours run out.
        perishContainer.OnTick(dt);

        // 2) Drying: advance per-slot progress over the elapsed in-game hours.
        double now = Api.World.Calendar.TotalHours;
        double elapsed = now - lastCalendarHours;
        if (elapsed <= 0) return;

        ClimateCondition? cc = Api.World.BlockAccessor.GetClimateAt(Pos, EnumGetClimateMode.NowValues);
        if (cc == null) return; // climate unavailable (unloaded) — retry next tick without consuming the window

        lastCalendarHours = now; // commit only once we can actually process

        bool openToSky = Pos.Y >= Api.World.BlockAccessor.GetRainMapHeightAt(Pos.X, Pos.Z);
        int sun = Api.World.BlockAccessor.GetLightLevel(Pos, EnumLightLevelType.OnlySunLight);
        float rate = ComputeDryingRate(cc, openToSky, sun); // hours-credit per elapsed hour; <0 = backslide

        bool dirty = false;
        if (rate < 0f)
        {
            // Backslide only floors at 0 and never triggers a conversion, so apply the whole window in one
            // shot — stepping a long rainy offline gap would just re-clamp zeros for nothing.
            float delta = (float)(rate * elapsed);
            for (int i = 1; i <= RackSlots; i++)
            {
                if (Match(inventory[i].Itemstack) is not DryingResult m || m.RequiresFire) continue;
                float p = dryProgress[i] + delta;
                dryProgress[i] = p < 0f ? 0f : p;
            }
        }
        else if (rate > 0f)
        {
            // Resolve each slot's recipe once (Match isn't free), then step the window so a piece converts
            // the moment it's ready. A converted slot's entry is cleared so it's skipped from then on.
            DryingResult?[] matches = new DryingResult?[RackSlots + 1];
            for (int i = 1; i <= RackSlots; i++)
                if (Match(inventory[i].Itemstack) is DryingResult m && !m.RequiresFire) matches[i] = m;

            const double step = 1.0;
            double remaining = elapsed;
            int safety = 200_000;
            while (remaining > 1e-6 && safety-- > 0)
            {
                double chunk = Math.Min(step, remaining);
                remaining -= chunk;
                float delta = (float)(rate * chunk);

                for (int i = 1; i <= RackSlots; i++)
                {
                    if (matches[i] is not DryingResult m) continue;
                    float p = dryProgress[i] + delta;
                    if (p >= m.Hours)
                    {
                        inventory[i].Itemstack = new ItemStack(m.Output, m.Quantity);
                        inventory[i].MarkDirty();
                        dryProgress[i] = 0f;
                        matches[i] = null; // converted; skip for the rest of the window
                        dirty = true;
                    }
                    else dryProgress[i] = p;
                }
            }
        }

        if (dirty) MarkDirty(true);
        // While anything is on the rack, resync each tick so the progress/spoilage tooltip stays live
        // (10s cadence; MarkDirty() syncs tree attributes to clients without a remesh).
        else if (HasRackable()) MarkDirty();
    }

    // Drying credit earned per elapsed in-game hour. Negative when rained on (rehydration).
    float ComputeDryingRate(ClimateCondition cc, bool openToSky, int sunLight)
    {
        if (openToSky && cc.Rainfall > RainThreshold) return -BackslideRate;

        float sun01 = sunLight / 32f;                                                  // 0..32 -> 0..1
        float temp01 = GameMath.Clamp((cc.Temperature - TempMin) / (TempMax - TempMin), 0f, 1f);
        float dry01 = 1f - GameMath.Clamp(cc.WorldgenRainfall, 0f, 1f);                 // climate dryness, not live rain
        return AirBaseRate * (sun01 + temp01 + dry01) / 3f;
    }

    protected override void OnRackSlotChanged(int slot)
    {
        if (slot >= 1 && slot <= RackSlots) dryProgress[slot] = 0f;
    }

    // ---------------- rendering ----------------

    // Always-present frame: just the three cross rungs. The side rails and the rope lashings are
    // conditional — they move onto the centred seam rail when a rack abuts E/W; legs shift onto seams too.
    static readonly string[] FrameElements = { "bar-front", "bar-mid", "bar-back" };

    public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
    {
        if (capi == null) return false;

        AssetLocation? shapeBase = Block?.Shape?.Base;
        Shape? shape = shapeBase == null ? null : capi.TesselatorManager.GetCachedShape(shapeBase);
        if (shape == null) { RenderRackItems(mesher); return false; } // fall back to the default shape

        // Adjacent drying racks merge: we draw the frame ourselves and decide each leg from the neighbours.
        bool n = IsRackNeighbour(BlockFacing.NORTH);
        bool e = IsRackNeighbour(BlockFacing.EAST);
        bool s = IsRackNeighbour(BlockFacing.SOUTH);
        bool w = IsRackNeighbour(BlockFacing.WEST);
        bool up = IsRackNeighbour(BlockFacing.UP); // a rack stacked directly above

        List<string> frameElements = new(FrameElements);

        // Side rails: each inner rail is dropped toward an E/W neighbour and replaced by ONE rail centred
        // on the seam (rail-seam-e, owned by the west rack) so it lines up with the merged legs instead of
        // two doubled, off-centre beams. The rung-bridges below meet that centred rail.
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
            // At a grid junction (also a N/S neighbour) extend the seam rail across the N/S seam so it
            // reaches the shared central post instead of stopping short of it.
            if (n) frameElements.Add("rail-seam-e-n");
            if (s) frameElements.Add("rail-seam-e-s");
        }

        // Rung-to-rail lashings follow the rail: at the outer rail when that side is free, or moved onto the
        // centred seam rail when a rack abuts that side (so they don't float where the dropped rail was).
        if (!w) { frameElements.Add("lash-fl"); frameElements.Add("lash-ml"); frameElements.Add("lash-bl"); }
        else { frameElements.Add("lash-fl-w"); frameElements.Add("lash-ml-w"); frameElements.Add("lash-bl-w"); }
        if (!e) { frameElements.Add("lash-fr"); frameElements.Add("lash-mr"); frameElements.Add("lash-br"); }
        else { frameElements.Add("lash-fr-e"); frameElements.Add("lash-mr-e"); frameElements.Add("lash-br-e"); }

        // South-seam rung (with its rope lashings) keeps the cross-rung spacing even across a N/S join.
        // Purely visual, owned by the north rack — no extra hang spot.
        if (s)
        {
            frameElements.Add("bar-seam-s");
            // The seam rung's lashings follow the same rule as the cross-rung lashings: at the outer rail
            // when that side is free, or moved onto the centred seam rail when a rack abuts that side.
            if (!w) frameElements.Add("lash-seam-l"); else frameElements.Add("lash-seam-l-w");
            if (!e) frameElements.Add("lash-seam-r"); else frameElements.Add("lash-seam-r-e");
            // At a grid junction (also an E/W neighbour) extend the seam rung toward the centred seam rail
            // so the central post is reached from the east/west too.
            if (e) frameElements.Add("bar-seam-s-e");
            if (w) frameElements.Add("bar-seam-s-w");
        }

        // Rungs bridge E/W seams (the west bridges are widened to fill the dropped left rail).
        if (w) { frameElements.Add("bar-front-w"); frameElements.Add("bar-mid-w"); frameElements.Add("bar-back-w"); }
        if (e) { frameElements.Add("bar-front-e"); frameElements.Add("bar-mid-e"); frameElements.Add("bar-back-e"); }

        capi.Tesselator.TesselateShape(Block, shape, out MeshData frame, null, null, frameElements.ToArray());
        frame.RenderPassesAndExtraBits.Fill((short)EnumChunkRenderPass.OpaqueNoCull); // match the block's renderpass
        mesher.AddMeshData(frame);

        // Ownership: a neighbour on the lower-coord side (west/north) owns the shared leg, so we drop ours;
        // a neighbour on the higher-coord side (east/south) means we keep ours but slide it +3/16 onto the
        // block border so the two racks share one leg centred on the seam instead of two offset legs.
        const float seam = 3f / 16f;
        // post-nw's node is normally back-filled by the NW-diagonal rack's post-se. At a concave inner
        // corner (W and N neighbours but NO NW-diagonal) nothing covers it, so render it shifted onto the
        // corner; otherwise drop it toward a W/N neighbour (or keep it inset when there's neither).
        bool nwDiag = Api.World.BlockAccessor.GetBlock(Pos.AddCopy(-1, 0, -1)) is BlockSmokeRack;
        if (w && n && !nwDiag) AddPost(mesher, shape, "post-nw", true, -seam, -seam, up);
        else AddPost(mesher, shape, "post-nw", !(w || n), 0f, 0f, up);
        AddPost(mesher, shape, "post-ne", !n, e ? seam : 0f, 0f, up);
        AddPost(mesher, shape, "post-sw", !w, 0f, s ? seam : 0f, up);
        AddPost(mesher, shape, "post-se", true, e ? seam : 0f, s ? seam : 0f, up);

        RenderRackItems(mesher);
        return true; // we now own the frame; suppress the default shape
    }

    // Renders a leg (with its optional X/Z seam-slide). When `up` is set there's a rack stacked above, so we
    // also draw that leg's upward extension (Y10-16, filling the empty top half) which meets the upper rack's
    // leg at the block border — turning the two stacked racks into one continuous post instead of leaving the
    // upper legs hanging over this rack's tabletop. The extension shares the leg's slide so seams stay aligned.
    void AddPost(ITerrainMeshPool mesher, Shape shape, string element, bool render, float dx, float dz, bool up)
    {
        if (!render || capi == null) return;
        string[] elements = up ? new[] { element, element + "-up" } : new[] { element };
        capi.Tesselator.TesselateShape(Block, shape, out MeshData mesh, null, null, elements);
        mesh.RenderPassesAndExtraBits.Fill((short)EnumChunkRenderPass.OpaqueNoCull);
        if (dx != 0f || dz != 0f) mesh.Translate(dx, 0f, dz);
        mesher.AddMeshData(mesh);
    }

    bool IsRackNeighbour(BlockFacing face) => Api.World.BlockAccessor.GetBlock(Pos.AddCopy(face)) is BlockSmokeRack;

    // ---------------- info / persistence ----------------

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder sb)
    {
        base.GetBlockInfo(forPlayer, sb);

        // Box 1..RackSlots = a specific hang spot; box 0 (frame) or none = the rack as a whole.
        int box = forPlayer.CurrentBlockSelection?.SelectionBoxIndex ?? 0;
        int slot = box >= 1 && box <= RackSlots ? box : 0;

        // Looking right at one occupied spot: show that exact piece's drying + spoilage.
        if (slot > 0 && !inventory[slot].Empty)
        {
            ItemStack stack = inventory[slot].Itemstack;
            sb.AppendLine(stack.GetName());
            if (Match(stack) is DryingResult m && !m.RequiresFire && m.Hours > 0)
                sb.AppendLine(Lang.Get("pemmican:dryingrack-drying", (int)(GameMath.Clamp(dryProgress[slot] / (float)m.Hours, 0f, 1f) * 100)));
            float spoil = SpoilLevel(inventory[slot]);
            if (spoil > 0f) sb.AppendLine(Lang.Get("pemmican:dryingrack-spoilage", (int)(spoil * 100)));
            return;
        }

        // Otherwise: contents plus rack-wide averages.
        int racked = CountRacked();
        sb.AppendLine(racked == 0
            ? Lang.Get("pemmican:smokerack-empty")
            : Lang.Get("pemmican:smokerack-contents", racked, RackSlots));

        double drySum = 0, spoilSum = 0;
        int dryN = 0, spoilN = 0;
        for (int i = 1; i <= RackSlots; i++)
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

    // 0..1 spoilage (Perish transition) of a slot's piece, for the tooltip. UpdateAndGetTransitionState
    // does advance the stack's transition over elapsed calendar time, but on the CLIENT that only nudges
    // the local copy — actual rotting is server-gated and re-synced — so the displayed value stays right.
    // This is the same call vanilla uses for perishable tooltips.
    float SpoilLevel(ItemSlot slot)
    {
        TransitionState? ts = slot.Itemstack?.Collectible.UpdateAndGetTransitionState(Api.World, slot, EnumTransitionType.Perish);
        return ts?.TransitionLevel ?? 0f;
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree["dryProgress"] = new FloatArrayAttribute(dryProgress);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        if (tree["dryProgress"] is FloatArrayAttribute arr && arr.value?.Length == dryProgress.Length)
            dryProgress = arr.value;
    }
}
