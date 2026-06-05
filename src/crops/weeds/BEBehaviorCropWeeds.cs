using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Ehm93.VS.Shared;
using Ehm93.VS.Crops.Common;

namespace Ehm93.VS.Crops.Weeds;

// TODO: color of weeds seems wrong?

public class BEBehaviorCropWeeds : BlockEntityBehavior, IWeedSource
{
    readonly private double minSproutChance = 0.001;
    readonly private double maxSproutChance = 0.5;
    readonly private double maxGrowChance = 1;
    readonly private double minGrowChance = 0.01;
    readonly private double growth = 10;
    private PressureProvider[]? primaryPressure;
    private NeighborPressureProvider? neighborPressure;
    private PressureProvider? antiPressure;
    protected double weedLevel;
    protected double lastCheckTotalHours = 0;
    protected MeshData? weedMesh;
    // Grass-equivalent climate/season color-map data (indices + this spot's temperature/rainfall) so the
    // weed mesh tints exactly like nearby grass. A farmland-BE mesh carries no climate data on its own.
    protected ColorMapData weedColorMap;

    public double WeedLevel
    {
        get => weedLevel;
        set
        {
            var clamped = Math.Clamp(value, 0, 100);
            if (weedLevel != clamped)
            {
                weedLevel = clamped;
                if (!GenWeedMesh()) FarmlandEntity?.MarkDirty(redrawOnClient: true);
            }
        }
    }

    public BlockEntityFarmland? FarmlandEntity => Blockentity as BlockEntityFarmland;

    public BEBehaviorCropWeeds(BlockEntity blockEntity)
        : base(blockEntity)
    {
        if (blockEntity is not BlockEntityFarmland) throw new ArgumentException("Configuration error! CropWeeds behavior may only be assigned to farmland.");
    }

    public override void Initialize(ICoreAPI api, JsonObject properties)
    {
        base.Initialize(api, properties);

        if (api is ICoreServerAPI)
        {
            if (Api.World.Config.GetBool("processCrops", defaultValue: true))
            {
                FarmlandEntity?.RegisterGameTickListener(Tick, 3900 + api.World.Rand.Next(200));
            }
        }

        primaryPressure = [
            new TemperaturePressureProvider(api, () => FarmlandEntity),
            new MoisturePressureProvider(() => FarmlandEntity),
            new NutrientPressureProvider(() => FarmlandEntity),
        ];
        neighborPressure = new NeighborPressureProvider(api, Blockentity.Pos);
        antiPressure = new MaturityPressureProvider(FunctionUtils.MemoizeFor(TimeSpan.FromMinutes(1), CropMaturity));

        GenWeedMesh();
    }

    public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
    {
        if (weedMesh != null) mesher.AddMeshData(weedMesh, weedColorMap);
        return base.OnTesselation(mesher, tessThreadTesselator);
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetDouble("weedLevel", weedLevel);
        tree.SetDouble("lastCheckTotalHours", lastCheckTotalHours);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        weedLevel = tree.TryGetDouble("weedLevel") ?? 0;
        lastCheckTotalHours = tree.TryGetDouble("lastCheckTotalHours") ?? 0;

        GenWeedMesh();
    }

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        base.GetBlockInfo(forPlayer, dsc);
        var rounded = Math.Round(weedLevel);
        if (rounded > 0)
        {
            dsc.AppendLine(Lang.Get("Weeds: {0}%", rounded));
            var growthChance = Math.Round(WeedGrowthChance() * 100);
            if (growthChance != 0) dsc.AppendLine(Lang.Get("Weed risk: {0}%", growthChance));
        }
        else
        {
            var sproutChance = Math.Round(WeedSproutChance() * 100);
            if (sproutChance != 0) dsc.AppendLine(Lang.Get("Weed risk: {0}%", sproutChance));
        }
    }

    protected virtual bool GenWeedMesh()
    {
        if (Api is not ICoreClientAPI capi) return false;

        if (WeedLevel == 0)
        {
            if (weedMesh != null)
            {
                weedMesh = null;
                return true;
            }
            else
            {
                return false;
            }
        }

        var shape = capi.Assets.Get(WeedShapeLocation()).ToObject<Shape>();

        var texSource = WeedTextureSource(capi);

        // Tint like vanilla grass (climatePlantTint + seasonalGrass). Capture the grass block's full
        // ColorMapData at this spot — the map indices AND this position's temperature/rainfall — and feed
        // it via the AddMeshData overload in OnTesselation. Setting only the map ids here isn't enough:
        // climate sampling needs the per-vertex temp/rainfall, which a farmland-BE mesh doesn't carry, so
        // the climate map would otherwise sample at the default (dry → golden).
        var grass = capi.World.GetBlock(new AssetLocation("game:tallgrass-medium-free"));
        weedColorMap = grass == null ? default : capi.World.GetColorMapData(grass, Pos.X, Pos.Y, Pos.Z);

        capi.Tesselator.TesselateShape(
            new TesselationMetaData
            {
                TypeForLogging = "farmland weed mesh",
                TexSource = texSource,
                ClimateColorMapId = weedColorMap.ClimateMapIndex,
                SeasonColorMapId = weedColorMap.SeasonMapIndex,
            },
            shape,
            out weedMesh
        );

        // Crops/weeds render in OpaqueNoCull (two-sided billboard); match it or the overlay is
        // backface-culled and only renders from one side.
        Array.Fill(weedMesh.RenderPassesAndExtraBits, (short)EnumChunkRenderPass.OpaqueNoCull);

        var rotateY = Math.PI * GetJitterOffset(Pos, 0);
        weedMesh.Rotate(new Vec3f(0.5f, 0, 0.5f), 0, (float)rotateY, 0);

        var offsetX = GetJitterOffset(Pos, 1);
        var offsetZ = GetJitterOffset(Pos, 2);
        // The behavior lives on the farmland, but weeds grow at the crop (soil surface) one block up —
        // lift the mesh +1Y or it renders buried inside the farmland block.
        weedMesh.Translate(new Vec3f(offsetX, 1, offsetZ));

        return true;
    }

    protected virtual AssetLocation WeedShapeLocation() => new("cropsweeds:shapes/block/plant/weeds.json");

    protected virtual ITexPositionSource WeedTextureSource(ICoreClientAPI capi)
    {
        TextureAtlasPosition northTexturePos;
        capi.BlockTextureAtlas.GetOrInsertTexture(WeedNorthTextureLocation(), out _, out northTexturePos);

        TextureAtlasPosition southTexturePos;
        capi.BlockTextureAtlas.GetOrInsertTexture(WeedSouthTextureLocation(), out _, out southTexturePos);

        var texMap = new Dictionary<string, TextureAtlasPosition> {
            { "north", northTexturePos },
            { "south", southTexturePos },
        };

        return new DictTexSource(texMap, capi.BlockTextureAtlas.Size);
    }

    protected virtual void Tick(float df) => CheckGrowWeeds();

    protected virtual void CheckGrowWeeds()
    {
        if (Api is not ICoreServerAPI sapi) return;
        if (!sapi.World.IsFullyLoadedChunk(Pos)) return;

        double now = Api.World.Calendar.TotalHours;
        double roll = Api.World.Rand.NextDouble();
        double deltaDays = (now - lastCheckTotalHours) / 24.0;

        if (lastCheckTotalHours == 0)
        {
            // first check, just record timestamp and exit
            lastCheckTotalHours = now;
            return;
        }

        lastCheckTotalHours = now;
        if (0.66 < CropMaturity())
        {
            // if crops are mature they outcompete weeds and the weeds slowly die back
            if (0 < weedLevel)
            {
                var witherProb = 1 - Math.Pow(1 - 0.5, deltaDays);
                if (roll < witherProb) WeedLevel -= growth;
            }
            return;
        }

        if (weedLevel == 0)
        {
            var sproutProb = 1 - Math.Pow(1 - WeedSproutChance(), deltaDays);
            if (roll < sproutProb) WeedLevel += growth;
        }
        else
        {
            var growProb = 1 - Math.Pow(1 - WeedGrowthChance(), deltaDays);
            if (roll < growProb) WeedLevel += growth;
        }
    }

    public double WeedSproutChance()
    {
        if (FarmlandEntity == null || neighborPressure == null) return 0;
        var growChance = WeedGrowthChance();
        if (GreenhouseUtil.IsGreenhouse(Api, Pos)) growChance /= 2; // greenhouse
        var spreadChance = neighborPressure.Value;
        return Math.Clamp(1 - (1 - growChance) * (1 - spreadChance), minSproutChance, maxSproutChance);
    }

    public virtual double WeedGrowthChance()
    {
        if (FarmlandEntity == null || primaryPressure == null || antiPressure == null) return 0;

        var antiMin = antiPressure.Range.Min;
        if (antiMin <= 0) return 0; // Guard against division by zero

        var max = primaryPressure.Sum(i => i.Range.Max) / antiMin;
        var pro = primaryPressure.Sum(i => i.Value);
        var anti = antiPressure.Value;
        const double a = 1.0;
        var b = max / 2;
        var growthChance = FunctionUtils.Sigmoid(b, a)(pro / anti);
        return Math.Min(1, maxGrowChance * growthChance + minGrowChance);
    }

    private AssetLocation WeedNorthTextureLocation()
    {
        return new AssetLocation($"cropsweeds:block/plant/weeds/{WeedLevelString()}-north");
    }

    private AssetLocation WeedSouthTextureLocation()
    {
        return new AssetLocation($"cropsweeds:block/plant/weeds/{WeedLevelString()}-south");
    }

    private string WeedLevelString()
    {
        return WeedLevel switch
        {
            0 => "none",
            < 20 => "veryshort",
            < 40 => "short",
            < 60 => "medium",
            < 80 => "tall",
            _ => "verytall"
        };
    }

    private double CropMaturity()
    {
        return (double)CropStage() / CropFinalStage();
    }

    private int CropStage()
    {
        var block = Api.World.BlockAccessor.GetBlock(Pos.UpCopy());
        if (block is not BlockCrop crop) return 1;
        if (int.TryParse(crop.LastCodePart(), out var result)) return result;
        return 1;
    }

    private int CropFinalStage()
    {
        var block = Api.World.BlockAccessor.GetBlock(Pos.UpCopy());
        if (block is not BlockCrop crop) return 1;
        return crop.CropProps.GrowthStages;
    }

    private float GetJitterOffset(BlockPos pos, int seed)
    {
        int hash = (pos.X * 73856093) ^ (pos.Y * 19349663) ^ (pos.Z * 83492791) ^ seed;
        Random rand = new Random(hash);
        return (float)(rand.NextDouble() - 0.5f) * 0.5f;
    }

    private interface PressureProvider
    {
        public double Value { get; }
        public Range Range { get; }
    }

    private class TemperaturePressureProvider : PressureProvider
    {
        private readonly ICoreAPI api;
        private readonly Func<BlockEntityFarmland?> FarmlandEntity;

        // Tunable parameters
        private const double tempWeight = 1;
        private const double LowThreshold = 12.0;
        private const double HighThreshold = 30.0;
        private const double KLow = 0.35;
        private const double KHigh = 0.6;

        private static readonly System.Func<double, double> CalculatePressure = FunctionUtils.MemoizeStepBounded(1, -40, 60, x =>
                tempWeight * FunctionUtils.Sigmoid(LowThreshold, KLow)(x) * (1.0 - FunctionUtils.Sigmoid(HighThreshold, KHigh)(x)));

        public TemperaturePressureProvider(ICoreAPI api, Func<BlockEntityFarmland?> FarmlandEntity)
        {
            this.api = api;
            this.FarmlandEntity = FarmlandEntity;
        }

        public double Value
        {
            get
            {
                var farmland = FarmlandEntity();
                if (farmland == null) return 0;
                var temp = api.World.BlockAccessor.GetClimateAt(farmland.Pos, EnumGetClimateMode.NowValues).Temperature;
                if (GreenhouseUtil.IsGreenhouse(api, farmland.Pos)) temp += 5; // greenhouse
                // Clamp into the memoized range; extreme climates would otherwise throw out-of-range.
                return CalculatePressure(Math.Clamp(temp, -40, 60));
            }
        }

        public Range Range => new(0, tempWeight);
    }

    private class MoisturePressureProvider(Func<BlockEntityFarmland?> FarmlandEntity) : PressureProvider
    {
        private const double moistureWeight = 1;

        // Tuned so that at 15% moisture, pressure = 50%
        private const double a = 0.25;
        private const double b = 0.3;

        private readonly static System.Func<double, double> CalculatePressure =
            FunctionUtils.MemoizeStepBounded(0.05, 0, 1, x => moistureWeight * FunctionUtils.Sigmoid(b, a)(x));

        private readonly Func<BlockEntityFarmland?> FarmlandEntity = FarmlandEntity;

        public double Value
        {
            get
            {
                var moisture = FarmlandEntity()?.MoistureLevel ?? 0;
                return CalculatePressure(moisture);
            }
        }

        public Range Range => new(0, moistureWeight);
    }

    private class NutrientPressureProvider : PressureProvider
    {
        private const double nutrientWeight = 2;

        // Gentle sigmoid centered around 120 nutrients (x = 0.6)
        private const double a = 8.0;   // steepness
        private const double b = 0.5;   // midpoint

        private readonly static System.Func<double, double> CalculatePressure = FunctionUtils.MemoizeStepBounded(1, 0, 240, x =>
            nutrientWeight * FunctionUtils.Sigmoid(b, a)(Math.Clamp(x / 240.0, 0, 1))
        );

        private Func<BlockEntityFarmland?> FarmlandEntity;

        public NutrientPressureProvider(Func<BlockEntityFarmland?> FarmlandEntity)
        {
            this.FarmlandEntity = FarmlandEntity;
        }

        public double Value
        {
            get
            {
                double nutrientSum = FarmlandEntity()?.Nutrients?.Sum() ?? 0;
                return CalculatePressure(Math.Clamp(nutrientSum, 0, 240));
            }
        }

        public Range Range => new(0, nutrientWeight);
    }

    private class NeighborPressureProvider : PressureProvider
    {
        // weight should stay 1 while this is used as a probability (see WeedSproutChance)
        private const double neighborWeight = 1;
        // Sigmoid: center at 0.5 (50%), steepness tuned for ramping between 0.25–0.50
        private const double a = 8;  // steepness
        private const double b = 0.33; // midpoint
        private readonly ICoreAPI Api;
        private readonly IEnumerable<BlockPos> neighborPositions;
        private readonly Func<double> Weediness;

        private readonly static System.Func<double, double> CalculatePressure = FunctionUtils.MemoizeStepBounded(0.05, 0, 1, x =>
            x == 0 ? 0 : neighborWeight * FunctionUtils.Sigmoid(b, a)(x)
        );

        public NeighborPressureProvider(ICoreAPI Api, BlockPos Pos)
        {
            this.Api = Api;
            neighborPositions =
            [
                Pos.NorthCopy(),
                Pos.NorthCopy().EastCopy(),
                Pos.EastCopy(),
                Pos.SouthCopy().EastCopy(),
                Pos.SouthCopy(),
                Pos.SouthCopy().WestCopy(),
                Pos.WestCopy(),
                Pos.NorthCopy().WestCopy(),
            ];
            // Weediness normalized by dividing by 800 (8 neighbors × 100 max weed level)
            Weediness = FunctionUtils.MemoizeFor(TimeSpan.FromSeconds(10),
                () => Math.Clamp(neighborPositions.Sum(GetWeedLevel) / 800, 0, 1));
        }

        public double Value => CalculatePressure(Weediness());

        public Range Range => new(0, neighborWeight);

        private double GetWeedLevel(BlockPos pos)
        {
            if (Api.World.BlockAccessor.GetBlockEntity(pos) is not BlockEntityFarmland entity) return 0;

            var weeds = entity.GetBehavior<BEBehaviorCropWeeds>();
            if (weeds == null) return 0;

            return weeds.WeedLevel;
        }
    }

    private class MaturityPressureProvider(Func<double> CropMaturity) : PressureProvider
    {
        private const double minCropMaturityPressure = 0.25;
        private const double cropMaturityWeight = 2;
        private const double a = 6;  // steepness
        private const double b = 0.66; // midpoint

        private readonly Func<double> CropMaturity = CropMaturity;

        private static readonly System.Func<double, double> CalculatePressure = FunctionUtils.MemoizeStepBounded(0.05, 0, 1, x =>
            Math.Max(0.5, cropMaturityWeight * FunctionUtils.Sigmoid(b, a)(x))
        );

        public double Value => CalculatePressure(CropMaturity());

        public Range Range => new(minCropMaturityPressure, cropMaturityWeight);
    }

    private readonly record struct Range(double Min, double Max);
}
