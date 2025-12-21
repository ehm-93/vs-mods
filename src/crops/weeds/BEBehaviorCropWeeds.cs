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

namespace Ehm93.VS.Crops.Weeds;

// TODO: color of weeds seems wrong?

public class BEBehaviorCropWeeds : BlockEntityBehavior
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

    public BlockEntityFarmland? FarmlandEntity {
        get {
            var entity = Api.World.BlockAccessor.GetBlockEntity(Pos.DownCopy());
            if (entity is not BlockEntityFarmland farmland) return null;
            return farmland;
        }
    }

    public BEBehaviorCropWeeds(BlockEntity blockEntity)
        : base(blockEntity)
    {
        if (blockEntity is not BlockEntityFarmland) throw new ArgumentException("Configuration error! CropWeeds behavior may only be used on farmland.");
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
        if (weedMesh != null) mesher.AddMeshData(weedMesh);
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
        if (rounded > 0) dsc.AppendLine(Lang.Get("Weeds: {0}%", rounded));
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

        capi.Tesselator.TesselateShape(
            new TesselationMetaData {
                TypeForLogging = "farmland weed mesh",
                TexSource = texSource,
                ClimateColorMapId = 1,
                SeasonColorMapId = 1,
            },
            shape,
            out weedMesh
        );


        var rotateY = Math.PI * GetJitterOffset(Pos, 0);
        weedMesh.Rotate(new Vec3f(0.5f, 0, 0.5f), 0, (float) rotateY, 0);

        var offsetX = GetJitterOffset(Pos, 1);
        var offsetZ = GetJitterOffset(Pos, 2);
        weedMesh.Translate(new Vec3f(offsetX, 0, offsetZ));

        return true;
    }

    protected virtual AssetLocation WeedShapeLocation()
    {
        return new AssetLocation("cropsv2:shapes/block/plant/weeds.json");
    }

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

    protected virtual void Tick(float df)
    {
        CheckGrowWeeds();
    }

    protected virtual void CheckGrowWeeds()
    {
        if (!(Api as ICoreServerAPI).World.IsFullyLoadedChunk(Pos)) return;
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
                double witherProb = 1 - Math.Pow(1 - 0.5, deltaDays);
                if (roll < witherProb) WeedLevel -= growth;
            }
            return;
        }
        else if (HasMulch())
        {
            // if crops are immature but the farmland is mulched then weeds will not grow
            return;
        }

        if (weedLevel == 0)
        {
            double sproutProb = 1 - Math.Pow(1 - WeedSproutChance(), deltaDays);
            if (roll < sproutProb) WeedLevel += growth;
        }
        else
        {
            double growProb = 1 - Math.Pow(1 - WeedGrowthChance(), deltaDays);
            if (roll < growProb) WeedLevel += growth;
        }
    }

    public double WeedSproutChance()
    {
        if (HasMulch() || FarmlandEntity == null) return 0;
        var growChance = WeedGrowthChance();
        if (FarmlandEntity.roomness > 0) growChance /= 2; // greenhouse
        var spreadChance = neighborPressure.Value;
        return Math.Clamp(1 - (1 - growChance) * (1 - spreadChance), minSproutChance, maxSproutChance);
    }

    public virtual double WeedGrowthChance()
    {
        if (HasMulch() || FarmlandEntity == null) return 0;
        var max = primaryPressure.Sum(i => i.Range.Max) / antiPressure.Range.Min;
        var pro = primaryPressure.Sum(i => i.Value);
        var anti = antiPressure.Value;
        const double a = 1.0;
        var b = max / 2;
        var growthChance = FunctionUtils.Sigmoid(pro / anti, b, a);
        return Math.Min(1, maxGrowChance * growthChance + minGrowChance);
    }

    private AssetLocation WeedNorthTextureLocation()
    {
        return new AssetLocation($"cropsv2:block/plant/weeds/{WeedLevelString()}-north");
    }

    private AssetLocation WeedSouthTextureLocation()
    {
        return new AssetLocation($"cropsv2:block/plant/weeds/{WeedLevelString()}-south");
    }

    private string WeedLevelString()
    {
        return WeedLevel switch {
            0 => "none",
            <20 => "veryshort",
            <40 => "short",
            <60 => "medium",
            <80 => "tall",
            _ => "verytall"
        };
    }

    private bool HasMulch()
    {
        var farmland = FarmlandEntity;
        if (farmland == null) return false;

        var behavior = farmland.GetBehavior<BEBehaviorFarmlandMulch>();
        if (behavior == null) return false;

        return 0 < behavior.MulchLevel;
    }

    private double CropMaturity()
    {
        return (double)CropStage() / CropFinalStage();
    }

    private int CropStage()
    {
        if (CropEntity?.Block is not BlockCrop crop) return 1;
        if (int.TryParse(crop.LastCodePart(), out var result)) return result;
        return 1;
    }

    private int CropFinalStage()
    {
        if (CropEntity?.Block is not BlockCrop crop) return 1;
        return crop.CropProps.GrowthStages;
    }

    private float GetJitterOffset(BlockPos pos, int seed)
    {
        int hash = (pos.X * 73856093) ^ (pos.Y * 19349663) ^ (pos.Z * 83492791) ^ seed;
        Random rand = new Random(hash);
        return (float) (rand.NextDouble() - 0.5f) * 0.5f;
    }

    private interface PressureProvider
    {
        public double Value { get; }
        public Range Range { get;}
    }

    private class TemperaturePressureProvider : PressureProvider
    {
        private readonly ICoreAPI api;
        private readonly Func<BlockEntityFarmland> FarmlandEntity;

        // Tunable parameters
        private const double tempWeight = 1;
        private const double LowThreshold = 12.0;
        private const double HighThreshold = 30.0;
        private const double KLow = 0.35;
        private const double KHigh = 0.6;

        private static readonly System.Func<double, double> CalculatePressure = FunctionUtils.MemoizeStepBounded(1, -40, 60, x =>
                tempWeight * FunctionUtils.Sigmoid(x, LowThreshold, KLow) * (1.0 - FunctionUtils.Sigmoid(x, HighThreshold, KHigh)));

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
                if (farmland.roomness > 0) temp += 5; // greenhouse
                return CalculatePressure(temp);
            }
        }

        public Range Range => new Range(0, tempWeight);
    }

    private class MoisturePressureProvider : PressureProvider
    {
        private const double moistureWeight = 1;

        // Tuned so that at 15% moisture, pressure = 50%
        private const double a = 0.25;
        private const double b = 0.3;

        private readonly static System.Func<double, double> CalculatePressure =
            FunctionUtils.MemoizeStepBounded(0.05, 0, 1, x => moistureWeight * FunctionUtils.Sigmoid(x, b, a));

        private readonly Func<BlockEntityFarmland> FarmlandEntity;

        public MoisturePressureProvider(Func<BlockEntityFarmland> FarmlandEntity)
        {
            this.FarmlandEntity = FarmlandEntity;
        }

        public double Value
        {
            get
            {
                double? moisture = FarmlandEntity()?.MoistureLevel;
                if (moisture == null) return 0;
                return CalculatePressure((double)moisture);
            }
        }

        public Range Range => new Range(0, moistureWeight);
    }

    private class NutrientPressureProvider : PressureProvider
    {
        private const double nutrientWeight = 2;

        // Gentle sigmoid centered around 120 nutrients (x = 0.6)
        private const double a = 8.0;   // steepness
        private const double b = 0.5;   // midpoint

        private readonly static System.Func<double, double> CalculatePressure = FunctionUtils.MemoizeStepBounded(1, 0, 240, x =>
            nutrientWeight * FunctionUtils.Sigmoid(Math.Clamp(x / 240.0, 0, 1), b, a)
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

        public Range Range => new Range(0, nutrientWeight);
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
            x == 0 ? 0 : neighborWeight * FunctionUtils.Sigmoid(x, b, a)
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
            Weediness = FunctionUtils.MemoizeFor(TimeSpan.FromSeconds(10),
                () => Math.Clamp(neighborPositions.Sum(GetWeedLevel) / 800, 0, 1));
        }

        public double Value
        {
            get
            {
                return CalculatePressure(Weediness());
            }
        }

        public Range Range => new Range(0, neighborWeight);

        private double GetWeedLevel(BlockPos pos)
        {
            if (Api.World.BlockAccessor.GetBlockEntity(pos) is not BlockEntityCropV2 entity) return 0;

            var weeds = entity.GetBehavior<BEBehaviorCropWeeds>();
            if (weeds == null) return 0;

            return weeds.WeedLevel;
        }
    }

    private class MaturityPressureProvider : PressureProvider
    {
        private const double minCropMaturityPressure = 0.25;
        private const double cropMaturityWeight = 2;
        private const double a = 6;  // steepness
        private const double b = 0.66; // midpoint

        private readonly Func<double> CropMaturity;

        private static readonly System.Func<double, double> CalculatePressure = FunctionUtils.MemoizeStepBounded(0.05, 0, 1, x =>
            Math.Max(0.5, cropMaturityWeight * FunctionUtils.Sigmoid(x, b, a))
        );

        public MaturityPressureProvider(Func<double> CropMaturity)
        {
            this.CropMaturity = CropMaturity;
        }

        public double Value => CalculatePressure(CropMaturity());

        public Range Range => new Range(minCropMaturityPressure, cropMaturityWeight);
    }

    private readonly record struct Range(double Min, double Max);
}
