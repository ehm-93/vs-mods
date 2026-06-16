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

namespace Ehm93.VS.Cooking.Beer;

public enum EnumHopBineState { Empty, Growing, Ripe, Dormant }

// A planted hop crown. A non-colliding 3 m pole is rendered automatically as soon as the crown is planted;
// the bine climbs that pole while the weather is warm. Growth is a true crop: it only advances on tilled
// farmland and DRAWS DOWN the farmland's nutrient (hops are heavy feeders) — starve or dry it out and the
// bine stalls and won't ripen. Perennial and TEMPERATURE-DRIVEN: it climbs and ripens through the warm
// months, a hard freeze (temp <= dormantTemp) makes it die back dormant (maturing the crown if it fruited),
// and the next thaw (temp >= coldStallTemp) regrows it from scratch. Yield scales with crown maturity.
public class BlockEntityHopPlant : BlockEntity
{
    // The pole the bine climbs is purely visual (rendered by this BE, no collision). Yield/render scale to it.
    private const int PoleHeight = 2;

    // ---- persisted growth state ----
    private EnumHopBineState state = EnumHopBineState.Empty;
    private float growthProgress;
    private int maturityYears;
    private bool producedThisYear;
    private double lastCheckTotalHours;

    // ---- config (block attributes) ----
    private float conesPerBlock = 1.2f;
    private float[] maturityMul = { 0.4f, 0.7f, 1.0f };
    private float ripeThreshold = 0.92f;       // growthProgress at which the bine is ripe to harvest
    private float growSeasonFraction = 0.4f;   // fraction of a year of warm, fertile growth to fully climb
    private float coldStallTemp = 5f;          // growth (and waking from dormancy) needs temp >= this
    private float dormantTemp = 0f;            // at/below this a freeze ends the season and the bine dies back
    private EnumSoilNutrient requiredNutrient = EnumSoilNutrient.N;
    private float nutrientPerSeason = 40f;      // N drawn over a full grow season (progress 0->1)
    private Item? hopcone;

    // ---- client render cache ----
    private MeshData? renderMesh;

    public bool IsRipe => state == EnumHopBineState.Ripe;

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        ReadConfig();
        hopcone = api.World.GetItem(new AssetLocation("beer:hopcone"));

        if (api is ICoreServerAPI && api.World.Config.GetBool("processCrops", defaultValue: true))
            RegisterGameTickListener(ServerTick, 25_000 + api.World.Rand.Next(10_000));
        if (api is ICoreClientAPI) GenMesh();
    }

    private void ReadConfig()
    {
        JsonObject? attr = Block?.Attributes;
        if (attr == null) return;
        conesPerBlock = attr["conesPerBlock"].AsFloat(conesPerBlock);
        if (attr["maturityMultipliers"].Exists) maturityMul = attr["maturityMultipliers"].AsArray<float>(maturityMul) ?? maturityMul;
        JsonObject gs = attr["growSeason"];
        if (gs.Exists)
        {
            ripeThreshold = gs["ripeThreshold"].AsFloat(ripeThreshold);
            growSeasonFraction = gs["growSeasonFraction"].AsFloat(growSeasonFraction);
        }
        coldStallTemp = attr["coldStallTemp"].AsFloat(coldStallTemp);
        dormantTemp = attr["dormantTemp"].AsFloat(dormantTemp);
        nutrientPerSeason = attr["nutrientPerSeason"].AsFloat(nutrientPerSeason);
        if (Enum.TryParse(attr["requiredNutrient"].AsString("N"), out EnumSoilNutrient n)) requiredNutrient = n;
    }

    // The farmland the crown sits on (or null if it isn't on farmland).
    private BlockEntityFarmland? Farmland()
        => Api?.World.BlockAccessor.GetBlockEntity(Pos.DownCopy()) as BlockEntityFarmland;

    public int ConeYield()
    {
        int idx = Math.Clamp(maturityYears, 0, maturityMul.Length - 1);
        return Math.Max(1, (int)Math.Round(PoleHeight * conesPerBlock * maturityMul[idx]));
    }

    public void Harvest(IPlayer byPlayer)
    {
        if (state != EnumHopBineState.Ripe) return;
        int n = ConeYield();
        if (n > 0 && hopcone != null)
            Api.World.SpawnItemEntity(new ItemStack(hopcone, n), Pos.ToVec3d().Add(0.5, 0.5, 0.5));
        state = EnumHopBineState.Growing;
        growthProgress = 0f;
        MarkDirty(true);
    }

    private void ServerTick(float dt)
    {
        double now = Api.World.Calendar.TotalHours;
        if (lastCheckTotalHours == 0) { lastCheckTotalHours = now; return; }
        double deltaHours = now - lastCheckTotalHours;
        lastCheckTotalHours = now;
        if (deltaHours <= 0) return;

        EnumHopBineState prevState = state;
        float prevProgress = growthProgress;

        double temp = Api.World.BlockAccessor.GetClimateAt(Pos, EnumGetClimateMode.NowValues).Temperature;

        if (temp <= dormantTemp)
        {
            // A hard freeze ends the season: the bine dies back. A crown that fruited this year matures, then
            // everything resets so the next thaw regrows from scratch. (dormantTemp..coldStallTemp is a dead
            // band of hysteresis, so a single cold night can't flip the bine in and out of dormancy.)
            if (state != EnumHopBineState.Dormant)
            {
                if (producedThisYear) maturityYears = Math.Min(maturityYears + 1, maturityMul.Length - 1);
                growthProgress = 0f;
                producedThisYear = false;
                state = EnumHopBineState.Dormant;
            }
        }
        else if (temp >= coldStallTemp)
        {
            // Warm enough to climb. A thaw wakes a dormant (or freshly planted) crown.
            if (state is EnumHopBineState.Dormant or EnumHopBineState.Empty)
                state = EnumHopBineState.Growing;

            // Resources gate: the bine only advances on fertile, watered farmland, and consumes its nutrient
            // as it grows. GetGrowthRate folds in both moisture and nutrient level (0 when starved).
            BlockEntityFarmland? fl = Farmland();
            float rate = fl?.GetGrowthRate(requiredNutrient) ?? 0f;
            if (rate > 0f && growthProgress < 1f)
            {
                double warmSeasonHours = Math.Max(1.0,
                    growSeasonFraction * Api.World.Calendar.DaysPerYear * Api.World.Calendar.HoursPerDay);
                float inc = (float)Math.Min(1.0 - growthProgress, deltaHours / warmSeasonHours * rate);
                if (inc > 0f)
                {
                    growthProgress += inc;
                    fl!.ConsumeNutrients(requiredNutrient, nutrientPerSeason * inc);
                }
            }

            if (state != EnumHopBineState.Ripe && !producedThisYear && growthProgress >= ripeThreshold)
            {
                state = EnumHopBineState.Ripe;
                producedThisYear = true;
            }
        }
        // else: temperate dead band (dormantTemp < temp < coldStallTemp) — hold state, don't grow or reset.

        if (state != prevState || ProgressStage(growthProgress) != ProgressStage(prevProgress)) MarkDirty(true);
    }

    // Which growth model (1..4) a given progress shows — also the client re-mesh trigger, so the bine swaps to
    // the next stage shape as it grows.
    private static int ProgressStage(float progress) => progress < 0.3f ? 1 : progress < 0.6f ? 2 : progress < 0.85f ? 3 : 4;

    // ---- persistence ----
    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetInt("state", (int)state);
        tree.SetFloat("growthProgress", growthProgress);
        tree.SetInt("maturityYears", maturityYears);
        tree.SetBool("producedThisYear", producedThisYear);
        tree.SetDouble("lastCheckTotalHours", lastCheckTotalHours);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        state = (EnumHopBineState)tree.GetInt("state");
        growthProgress = tree.GetFloat("growthProgress");
        maturityYears = tree.GetInt("maturityYears");
        producedThisYear = tree.GetBool("producedThisYear");
        lastCheckTotalHours = tree.TryGetDouble("lastCheckTotalHours") ?? 0;
        if (Api is ICoreClientAPI) GenMesh();
    }

    // ---- rendering ----
    // We draw the whole plant (the pole + the spiraling leaf bine + ripe cones) and return true to SKIP the
    // block's JSON shape — that shape is the rhizome-textured crown mound, which we don't want on the ground.
    public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
    {
        if (renderMesh != null) mesher.AddMeshData(renderMesh);
        return true;
    }

    private bool GenMesh()
    {
        if (Api is not ICoreClientAPI capi) return false;
        bool dormant = state == EnumHopBineState.Dormant;
        // Each state maps to a hand-authored stage shape (2 m pole + spiral vine + leaves, cones at stage 4/5).
        // Dormant reuses the full-leaf shape but swaps the leaf texture to its browned version.
        string shapePath = state switch
        {
            EnumHopBineState.Dormant => "shapes/block/hops/dormant.json",
            EnumHopBineState.Ripe    => "shapes/block/hops/stage5.json",
            EnumHopBineState.Empty   => "shapes/block/hops/stage1.json",
            _ => "shapes/block/hops/stage" + ProgressStage(growthProgress) + ".json"
        };
        renderMesh = TesselateStage(capi, shapePath, dormant);
        return true;
    }

    private MeshData? TesselateStage(ICoreClientAPI capi, string shapePath, bool dormant)
    {
        Shape? asset = capi.Assets.TryGet(new AssetLocation("beer", shapePath))?.ToObject<Shape>();
        if (asset == null) return null;
        Shape shape = asset.Clone();
        shape.Textures = null; // we resolve every texture code ourselves so the leaf can swap when dormant
        var map = new Dictionary<string, TextureAtlasPosition>();
        AddTex(capi, map, "wood", new AssetLocation("game", "block/wood/debarked/aged"));
        AddTex(capi, map, "stem", new AssetLocation("beer", "block/plant/crop/hops/stem"));
        AddTex(capi, map, "leaf", new AssetLocation("beer", "block/plant/crop/hops/" + (dormant ? "leafdormant" : "leaf")));
        AddTex(capi, map, "cone", new AssetLocation("beer", "block/plant/crop/hops/cone"));
        map["0"] = map["leaf"]; // fallback for any unmapped face code
        var src = new HopTexSource(map, capi.BlockTextureAtlas.Size);
        capi.Tesselator.TesselateShape(
            new TesselationMetaData { TexSource = src, TypeForLogging = "hop bine" }, shape, out MeshData? m);
        if (m != null) Array.Fill(m.RenderPassesAndExtraBits, (short)EnumChunkRenderPass.OpaqueNoCull);
        return m;
    }

    private static void AddTex(ICoreClientAPI capi, Dictionary<string, TextureAtlasPosition> map, string code, AssetLocation loc)
    {
        capi.BlockTextureAtlas.GetOrInsertTexture(loc, out _, out TextureAtlasPosition pos);
        map[code] = pos;
    }

    // ---- tooltip ----
    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        base.GetBlockInfo(forPlayer, dsc);
        if (Farmland() == null) { dsc.AppendLine(Lang.Get("beer:hopplant-nofarmland")); return; }
        dsc.AppendLine(Lang.Get("beer:hopplant-maturity", maturityYears));
        switch (state)
        {
            case EnumHopBineState.Ripe: dsc.AppendLine(Lang.Get("beer:hopplant-ripe", ConeYield())); break;
            case EnumHopBineState.Dormant: dsc.AppendLine(Lang.Get("beer:hopplant-dormant")); break;
            default: dsc.AppendLine(Lang.Get("beer:hopplant-growing", (int)Math.Round(growthProgress * 100))); break;
        }
    }

    private sealed class HopTexSource : ITexPositionSource
    {
        private readonly Dictionary<string, TextureAtlasPosition> map;
        public HopTexSource(Dictionary<string, TextureAtlasPosition> map, Size2i atlasSize) { this.map = map; AtlasSize = atlasSize; }
        public Size2i AtlasSize { get; }
        public TextureAtlasPosition this[string textureCode] => map.TryGetValue(textureCode, out var p) ? p : map["0"];
    }
}
