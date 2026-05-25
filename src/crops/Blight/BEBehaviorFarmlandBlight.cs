using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Ehm93.VS.Shared;
using Ehm93.VS.Crops.Common;

namespace Ehm93.VS.Crops.Blight;

// Consolidated blight + spore + history behavior, attached to BlockEntityFarmland.
// Blight level applies to the currently-growing crop above; resets when crop changes.
// Spore level, treatment, and crop-history are persistent soil state.
public class BEBehaviorFarmlandBlight : BlockEntityBehavior, IOnBlockInteract, ISporeSource, IBlightSource
{
    protected List<CropHistoryEntry> history = new();
    protected double blightLevel = 0;
    protected string? blightedCropCode;
    protected double sporeLevel = 0;
    protected double sporeTreatment = 0;
    protected double lastCheckTotalHours = 0;
    protected SimpleParticleProperties? blightParticles;
    protected (double Weight, IPressureProvider Pressure)[] inoculumPressure = Array.Empty<(double, IPressureProvider)>();
    protected (double Weight, IPressureProvider Pressure)[] susceptibilityPressure = Array.Empty<(double, IPressureProvider)>();

    // Visual blight overlay rendered at crop position (one block up from farmland).
    protected MeshData? blightMesh;
    protected Block? lastMeshedCrop;
    protected int lastMeshedTier = -1;

    public BlockEntityFarmland FarmlandEntity => (BlockEntityFarmland)Blockentity;

    public double SporeLevel
    {
        get => sporeLevel;
        set
        {
            var clamped = Math.Clamp(value, 0, 100);
            if (clamped != sporeLevel)
            {
                sporeLevel = clamped;
                FarmlandEntity.MarkDirty(true);
            }
        }
    }

    public double SporeTreatment
    {
        get => sporeTreatment;
        set
        {
            var clamped = Math.Clamp(value, 0, 100);
            if (clamped != sporeTreatment)
            {
                sporeTreatment = clamped;
                FarmlandEntity.MarkDirty(true);
            }
        }
    }

    public double BlightLevel
    {
        get => blightLevel;
        set
        {
            var clamped = Math.Clamp(value, 0, 100);
            if (blightLevel == clamped) return;
            var tierBefore = BlightTier;
            blightLevel = clamped;
            if (BlightTier != tierBefore) GenBlightMesh();
            FarmlandEntity.MarkDirty(true);
        }
    }

    public int BlightTier => (int)Math.Clamp(Math.Ceiling(blightLevel / 20), 0, 5);

    public BEBehaviorFarmlandBlight(BlockEntity blockentity) : base(blockentity)
    {
        if (blockentity is not BlockEntityFarmland)
            throw new ArgumentException("FarmlandBlight behavior may only be assigned to farmland.");
    }

    public override void Initialize(ICoreAPI api, JsonObject properties)
    {
        base.Initialize(api, properties);

        InitParticles();
        InitPressureProviders();
        GenBlightMesh();

        if (api is ICoreServerAPI && api.World.Config.GetBool("processCrops", defaultValue: true))
        {
            FarmlandEntity.RegisterGameTickListener(ServerTick, 3900 + api.World.Rand.Next(200));
        }
        if (api is ICoreClientAPI)
        {
            FarmlandEntity.RegisterGameTickListener(ClientTick, 400 + api.World.Rand.Next(200));
        }
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetString("history", JsonConvert.SerializeObject(history));
        tree.SetDouble("blightLevel", blightLevel);
        if (blightedCropCode != null) tree.SetString("blightedCropCode", blightedCropCode);
        tree.SetDouble("sporeLevel", sporeLevel);
        tree.SetDouble("sporeTreatment", sporeTreatment);
        tree.SetDouble("lastCheckTotalHours", lastCheckTotalHours);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        history = JsonConvert.DeserializeObject<List<CropHistoryEntry>>(tree.GetString("history") ?? "[]") ?? new();
        blightLevel = tree.TryGetDouble("blightLevel") ?? 0;
        blightedCropCode = tree.GetString("blightedCropCode");
        sporeLevel = tree.TryGetDouble("sporeLevel") ?? 0;
        sporeTreatment = tree.TryGetDouble("sporeTreatment") ?? 0;
        lastCheckTotalHours = tree.TryGetDouble("lastCheckTotalHours") ?? 0;
        GenBlightMesh();
    }

    public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
    {
        if (blightMesh != null) mesher.AddMeshData(blightMesh);
        return base.OnTesselation(mesher, tessThreadTesselator);
    }

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        base.GetBlockInfo(forPlayer, dsc);

        var spores = Math.Round(sporeLevel);
        if (0 < spores) dsc.AppendLine(Lang.Get("Blight spores: {0}%", spores));

        var treatment = Math.Round(sporeTreatment);
        if (0 < treatment) dsc.AppendLine(Lang.Get("Spore treatment: {0}%", treatment));

        var blight = Math.Round(blightLevel);
        if (0 < blight)
        {
            dsc.AppendLine(Lang.Get("Blight: {0}%", blight));
        }
        else if (CropAbove() != null)
        {
            var chance = Math.Round(OutbreakChance() * 100);
            if (0 < chance) dsc.AppendLine(Lang.Get("Blight risk: {0}%", chance));
        }
    }

    public bool OnBlockInteract(IPlayer byPlayer)
    {
        if (sporeLevel == 0) return false;
        var slot = byPlayer.InventoryManager.ActiveHotbarSlot;
        if (slot?.Itemstack?.Item?.Code?.Path != "lime") return false;
        if (sporeTreatment >= 100) return false;

        SporeTreatment += 50;

        if (!byPlayer.WorldData.CurrentGameMode.HasFlag(EnumGameMode.Creative))
        {
            slot.TakeOut(1);
            slot.MarkDirty();
        }

        Api.World.PlaySoundAt(
            Api.World.BlockAccessor.GetBlock(Pos).Sounds.Hit.Location,
            Pos.X + 0.5, Pos.InternalY + 0.75, Pos.Z + 0.5,
            byPlayer,
            randomizePitch: true,
            12f
        );

        return true;
    }

    public virtual double DeathChance()
    {
        if (blightLevel < 33) return 0;
        return FunctionUtils.Sigmoid(0.50, 12)((blightLevel - 33) / 67);
    }

    public virtual double OutbreakChance()
    {
        if (inoculumPressure.Length == 0 || susceptibilityPressure.Length == 0) return 0;
        var inoculumWeight = inoculumPressure.Sum(i => i.Weight);
        var inoculumRisk = inoculumPressure.Sum(i => i.Weight * i.Pressure.Value) / inoculumWeight;
        var susceptibilityRisk = susceptibilityPressure
            .Select(i => Math.Pow(i.Pressure.Value, i.Weight))
            .Aggregate((a, b) => a * b);
        var totalRisk = Math.Pow(inoculumRisk, 0.8) * Math.Pow(susceptibilityRisk, 0.2);
        var coef = InGreenhouse() ? 2 : 1;
        return totalRisk == 0 ? 0 : FunctionUtils.Sigmoid(0.33, 12)(coef * totalRisk);
    }

    public virtual double ExposureHours(string cropCode)
    {
        var now = Api.World.Calendar.TotalHours;
        var entries = history.Where(h => h.CropCode == cropCode);
        return entries.Sum(e => (e.EndTimeHours ?? now) - e.StartTimeHours);
    }

    protected virtual void ServerTick(float df)
    {
        CheckHistory();
        ResetBlightIfCropChanged();
        CheckBlightDeath();
        CheckOutbreak();
        ReduceSpores();
    }

    protected virtual void ClientTick(float df)
    {
        // re-mesh when the crop block above changes (e.g. growth stage) without a farmland-side notification
        if (BlightTier > 0 && CropAbove() != lastMeshedCrop)
        {
            if (GenBlightMesh()) FarmlandEntity.MarkDirty(redrawOnClient: true);
        }

        if (blightParticles == null) return;
        if (blightLevel == 0 && sporeLevel == 0) return;

        var amount = Math.Max(blightLevel, sporeLevel);
        blightParticles.MinPos = Pos.ToVec3d().Add(0, blightLevel > 0 ? 1 : 0, 0);
        blightParticles.AddPos.Set(1, 0.0, 1);
        blightParticles.MinQuantity = (float)Math.Ceiling(amount / 20);
        blightParticles.AddQuantity = (float)Math.Ceiling(amount / 50);
        Api.World.SpawnParticles(blightParticles);
    }

    protected virtual void ResetBlightIfCropChanged()
    {
        var current = CropAbove()?.CodeWithoutParts(1);
        if (current != blightedCropCode)
        {
            blightLevel = 0;
            blightedCropCode = current;
            GenBlightMesh();
            FarmlandEntity.MarkDirty(true);
        }
    }

    protected virtual void CheckBlightDeath()
    {
        if (blightLevel < 100) return;
        var roll = Api.World.Rand.NextDouble();
        if (roll < DeathChance()) Die();
    }

    protected virtual void Die()
    {
        var deadCrop = Api.World.GetBlock(new AssetLocation("game:deadcrop"));
        if (deadCrop == null) return;
        Api.World.BlockAccessor.SetBlock(deadCrop.Id, Pos.UpCopy());
        Api.World.SpawnParticles(new SimpleParticleProperties(
            20, 30,
            ColorUtil.ToRgba(150, 30, 20, 10),
            Pos.ToVec3d().Add(0.5, 1.5, 0.5),
            new Vec3d(0.5, 0.5, 0.5),
            new Vec3f(-0.1f, 0.2f, -0.1f),
            new Vec3f(0.1f, 0.6f, 0.1f),
            1.2f, 0.3f, 0.4f, 0.8f
        )
        {
            GravityEffect = 0,
            WindAffected = true,
            SizeEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, -0.05f),
            OpacityEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, -0.2f),
            ParticleModel = EnumParticleModel.Cube
        });
        Api.World.PlaySoundAt(
            new AssetLocation("sounds/block/plant"),
            Pos.X + 0.5, Pos.Y + 1.5, Pos.Z + 0.5
        );
    }

    protected virtual void CheckOutbreak()
    {
        if (CropAbove() == null) return;
        if (lastCheckTotalHours == 0)
        {
            lastCheckTotalHours = Api.World.Calendar.TotalHours;
            return;
        }

        var now = Api.World.Calendar.TotalHours;
        var deltaDays = (now - lastCheckTotalHours) / Api.World.Calendar.HoursPerDay;
        lastCheckTotalHours = now;

        var chance = 1 - Math.Pow(1 - OutbreakChance(), deltaDays);
        if (Api.World.Rand.NextDouble() < chance)
        {
            BlightLevel += 10 + BlightLevel / 2;
        }
    }

    protected virtual void ReduceSpores()
    {
        if (sporeTreatment > 0) TreatSpores();
        else DecaySpores(0.02);
    }

    protected virtual void TreatSpores()
    {
        var normalized = sporeTreatment / 100;
        DecaySpores(Math.Max(normalized * 0.2, 0.04));

        var now = Api.World.Calendar.TotalHours;
        var deltaDays = (now - lastCheckTotalHours) / Api.World.Calendar.HoursPerDay;

        if (sporeTreatment < 0.5) SporeTreatment = 0;
        else SporeTreatment *= Math.Pow(1 - 0.05, deltaDays);

        FarmlandEntity.Nutrients[0] *= (float)Math.Pow(1 - 0.1, deltaDays);
        FarmlandEntity.Nutrients[1] *= (float)Math.Pow(1 - 0.1, deltaDays);
        FarmlandEntity.Nutrients[2] *= (float)Math.Pow(1 - 0.1, deltaDays);
    }

    protected virtual void DecaySpores(double decayRate)
    {
        var now = Api.World.Calendar.TotalHours;
        var deltaDays = (now - lastCheckTotalHours) / Api.World.Calendar.HoursPerDay;
        if (sporeLevel < 0.5) SporeLevel = 0;
        else SporeLevel *= Math.Pow(1 - decayRate, deltaDays);
    }

    protected virtual void CheckHistory()
    {
        var changed = PruneHistory();
        changed |= CheckAppendHistory();
        if (changed) FarmlandEntity.MarkDirty(true);
    }

    protected virtual bool PruneHistory()
    {
        var now = Api.World.Calendar.TotalHours;
        var threeYears = 3 * Api.World.Calendar.DaysPerYear * Api.World.Calendar.HoursPerDay;
        var threeYearsAgo = now - threeYears;
        for (var i = 0; i < history.Count; i++)
        {
            var h = history[i];
            if (h.EndTimeHours != null && h.EndTimeHours > threeYearsAgo)
            {
                if (i > 0)
                {
                    history = history.GetRange(i, history.Count - i);
                    return true;
                }
                return false;
            }
        }
        return false;
    }

    protected virtual bool CheckAppendHistory()
    {
        var now = Api.World.Calendar.TotalHours;
        var current = CropAbove();
        if (history.Count == 0 && current == null) return false;

        var latest = history.LastOrDefault();
        if (current == null)
        {
            if (latest != null && latest.EndTimeHours == null)
            {
                latest.EndTimeHours = now;
                return true;
            }
            return false;
        }

        var currentCode = current.CodeWithoutParts(1);
        if (latest != null && latest.EndTimeHours == null && latest.CropCode == currentCode) return false;

        if (latest != null && latest.EndTimeHours == null) latest.EndTimeHours = now;
        history.Add(new CropHistoryEntry
        {
            CropCode = currentCode,
            StartTimeHours = now,
            EndTimeHours = null,
        });
        return true;
    }

    protected virtual Block? CropAbove() => Api.World.BlockAccessor.GetBlock(Pos.UpCopy()) is BlockCrop c ? c : null;

    // Tesselates the current crop's shape with procedurally-blighted textures (BlightFilter) and
    // translates the mesh +1Y so it renders at the crop's position. Returns true if blightMesh changed.
    private bool GenBlightMesh()
    {
        if (Api is not ICoreClientAPI capi) return false;

        var crop = CropAbove();
        var tier = BlightTier;

        if (tier == 0 || crop == null)
        {
            if (blightMesh == null && lastMeshedCrop == null && lastMeshedTier == 0) return false;
            blightMesh = null;
            lastMeshedCrop = crop;
            lastMeshedTier = 0;
            return true;
        }

        if (ReferenceEquals(crop, lastMeshedCrop) && tier == lastMeshedTier && blightMesh != null) return false;
        lastMeshedCrop = crop;
        lastMeshedTier = tier;

        if (crop.Shape?.Base == null) { blightMesh = null; return true; }

        var shapeAsset = capi.Assets.TryGet(crop.Shape.Base)?.ToObject<Shape>()
            ?? capi.Assets.TryGet(new AssetLocation($"{crop.Shape.Base.Domain}:shapes/{crop.Shape.Base.Path}.json"))?.ToObject<Shape>();
        if (shapeAsset == null) { blightMesh = null; return true; }

        var shape = shapeAsset.Clone();
        var texMap = new Dictionary<string, TextureAtlasPosition>();
        if (shape.Textures != null)
        {
            foreach (var pair in shape.Textures)
            {
                var baseLoc = pair.Value;
                // Virtual atlas key — atlas caches by AssetLocation, so the filter only runs once per (base, tier).
                var blightLoc = new AssetLocation("cropsblight", $"dynamic/{baseLoc.Domain}/{baseLoc.Path}-blight{tier}");
                capi.BlockTextureAtlas.GetOrInsertTexture(
                    blightLoc,
                    out _,
                    out var pos,
                    () => BlightFilter.Apply(capi, baseLoc, tier)
                );
                texMap[pair.Key] = pos;
            }
        }
        shape.Textures = null;

        var texSource = new DictTexSource(texMap, capi.BlockTextureAtlas.Size);

        capi.Tesselator.TesselateShape(
            new TesselationMetaData
            {
                TexSource = texSource,
                TypeForLogging = "blighted crop",
                ClimateColorMapId = 1,
                SeasonColorMapId = 1,
            },
            shape,
            out blightMesh
        );

        blightMesh?.Translate(new Vec3f(0, 1, 0));
        return true;
    }

    private void InitPressureProviders()
    {
        inoculumPressure = new (double, IPressureProvider)[]
        {
            (0.4, new NeighborPressureProvider(Api, Pos, this)),
            (0.4, new HistoryPressureProvider(this)),
            (0.2, new SporePressureProvider(this)),
        };
        susceptibilityPressure = new (double, IPressureProvider)[]
        {
            (0.15, new TemperaturePressureProvider(Api.World, Pos)),
            (0.15, new MoisturePressureProvider(FarmlandEntity)),
            (0.05, new MulchPressureProvider(FarmlandEntity)),
            (0.6, new GenerationPressureProvider(FarmlandEntity)),
            (0.05, new WeedPressureProvider(FarmlandEntity)),
        };
    }

    private bool InGreenhouse() => GreenhouseUtil.IsGreenhouse(Api, Pos);

    private void InitParticles()
    {
        if (Api is not ICoreClientAPI) return;
        blightParticles = new SimpleParticleProperties(
            1f, 1f,
            ColorUtil.ToRgba(90, 120, 40, 20),
            new Vec3d(), new Vec3d(),
            new Vec3f(-0.03f, 0.05f, -0.03f),
            new Vec3f(0.03f, 1f, 0.03f),
            1.5f, 0.01f, 0.1f, 0.25f
        )
        {
            MinSize = 0.3f,
            MaxSize = 0.5f,
            GravityEffect = 0.01f,
            WindAffected = true,
            WindAffectednes = 0.6f,
            ShouldDieInAir = false,
            ShouldDieInLiquid = true,
            SelfPropelled = false,
            SizeEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, -0.05f),
            OpacityEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, -0.2f),
            ParticleModel = EnumParticleModel.Cube,
        };
    }

    public class CropHistoryEntry
    {
        public string CropCode { get; set; } = "";
        public double StartTimeHours { get; set; }
        public double? EndTimeHours { get; set; }
    }

    public interface IPressureProvider { double Value { get; } }

    private class HistoryPressureProvider : IPressureProvider
    {
        private const double MaxExposureYears = 3.0;
        private const double SigmoidDeadZone = 0.25;
        private const double SigmoidMidpoint = 0.5;
        private const double SigmoidSharpness = 12;

        private readonly BEBehaviorFarmlandBlight blight;

        private static readonly System.Func<double, double> PressureCurve = FunctionUtils.MemoizeStepBounded(
            0.01, 0, 1,
            x => x < SigmoidDeadZone ? 0 : FunctionUtils.Sigmoid(SigmoidMidpoint, SigmoidSharpness)(x)
        );

        public HistoryPressureProvider(BEBehaviorFarmlandBlight blight) { this.blight = blight; }

        public double Value
        {
            get
            {
                var crop = blight.CropAbove();
                if (crop == null) return 0;
                double exposure = blight.ExposureHours(crop.CodeWithoutParts(1));
                double maxExposure = MaxExposureYears * blight.Api.World.Calendar.DaysPerYear * blight.Api.World.Calendar.HoursPerDay;
                double normalized = GameMath.Clamp(exposure / maxExposure, 0, 1);
                return PressureCurve(normalized);
            }
        }
    }

    private class NeighborPressureProvider : IPressureProvider
    {
        private const double a = 16;
        private const double b = 0.08;
        private readonly ICoreAPI api;
        private readonly IEnumerable<BlockPos> neighborPositions;
        private readonly Func<double> Blightness;
        private readonly string targetCrop;

        private static readonly System.Func<double, double> CalculatePressure = FunctionUtils.MemoizeStepBounded(
            0.01, 0, 1, x => x == 0 ? 0 : FunctionUtils.Sigmoid(b, a)(x));

        public NeighborPressureProvider(ICoreAPI api, BlockPos pos, BEBehaviorFarmlandBlight self, int d = 2)
        {
            this.api = api;
            targetCrop = self.CropAbove()?.CodeWithoutParts(1) ?? "";

            var offsets = new List<BlockPos>();
            for (int dx = -d; dx <= d; dx++)
            {
                for (int dz = -d; dz <= d; dz++)
                {
                    if (dx == 0 && dz == 0) continue;
                    offsets.Add(pos.AddCopy(dx, 0, dz));
                }
            }
            neighborPositions = offsets;

            Blightness = FunctionUtils.MemoizeFor(TimeSpan.FromSeconds(30), () =>
            {
                double totalBlight = 0;
                double totalWeight = 0;
                foreach (var p in neighborPositions)
                {
                    double dist = p.DistanceTo(pos);
                    double weight = 1 / Math.Max(dist, 1);
                    var info = GetNeighborBlight(p);
                    var coef = info.code == targetCrop ? 1 : 0.1;
                    totalBlight += coef * info.blightLevel * weight;
                    totalWeight += weight;
                }
                return totalWeight == 0 ? 0 : totalBlight / (totalWeight * 100);
            });
        }

        public double Value => CalculatePressure(Blightness());

        private (string code, double blightLevel) GetNeighborBlight(BlockPos pos)
        {
            if (api.World.BlockAccessor.GetBlockEntity(pos) is not BlockEntityFarmland entity) return ("", 0);
            var behavior = entity.GetBehavior<BEBehaviorFarmlandBlight>();
            if (behavior == null) return ("", 0);
            var crop = api.World.BlockAccessor.GetBlock(pos.UpCopy()) as BlockCrop;
            return (crop?.CodeWithoutParts(1) ?? "", behavior.BlightLevel);
        }
    }

    private class SporePressureProvider : IPressureProvider
    {
        private const double SporeDeadzone = 0.5;
        private const double MaxSpores = 100.0;
        private const double Midpoint = 0.33;
        private const double Steepness = 10;

        private readonly BEBehaviorFarmlandBlight blight;

        private static readonly System.Func<double, double> SporeToPressure = FunctionUtils.MemoizeStepBounded(
            1, 0, MaxSpores,
            s => s <= SporeDeadzone ? 0 : FunctionUtils.Sigmoid(Midpoint, Steepness)(s / MaxSpores)
        );

        public SporePressureProvider(BEBehaviorFarmlandBlight blight) { this.blight = blight; }

        public double Value => SporeToPressure(blight.SporeLevel);
    }

    private class WeedPressureProvider : IPressureProvider
    {
        private const double maxWeedLevel = 100.0;
        private const double midpoint = 0.5;
        private const double steepness = 8;

        private readonly BlockEntityFarmland farmland;

        private static readonly System.Func<double, double> WeedToPressure = FunctionUtils.MemoizeStepBounded(
            1, 0, maxWeedLevel,
            x => FunctionUtils.Sigmoid(midpoint, steepness)(x / maxWeedLevel)
        );

        public WeedPressureProvider(BlockEntityFarmland farmland) { this.farmland = farmland; }

        public double Value
        {
            get
            {
                var weeds = farmland.Behaviors.OfType<IWeedSource>().FirstOrDefault();
                return weeds == null ? 0 : WeedToPressure(weeds.WeedLevel);
            }
        }
    }

    private class MoisturePressureProvider : IPressureProvider
    {
        private readonly BlockEntityFarmland farmland;
        public MoisturePressureProvider(BlockEntityFarmland farmland) { this.farmland = farmland; }

        public double Value
        {
            get
            {
                double moisture = farmland.MoistureLevel;
                double ideal = 0.4;
                double range = 0.3;
                double deviation = (moisture - ideal) / range;
                return Math.Clamp(deviation * deviation, 0, 1);
            }
        }
    }

    private class TemperaturePressureProvider : IPressureProvider
    {
        private readonly IWorldAccessor world;
        private readonly BlockPos pos;
        public TemperaturePressureProvider(IWorldAccessor world, BlockPos pos) { this.world = world; this.pos = pos; }

        public double Value
        {
            get
            {
                double temp = world.BlockAccessor.GetClimateAt(pos, EnumGetClimateMode.NowValues).Temperature;
                double ideal = 25.0;
                double spread = 10.0;
                double deviation = (temp - ideal) / spread;
                return Math.Clamp(Math.Exp(-deviation * deviation), 0, 1);
            }
        }
    }

    private class MulchPressureProvider : IPressureProvider
    {
        private readonly BlockEntityFarmland farmland;
        public MulchPressureProvider(BlockEntityFarmland farmland) { this.farmland = farmland; }

        public double Value
        {
            get
            {
                var mulch = farmland.Behaviors.OfType<IMulchSource>().FirstOrDefault();
                double mulchLevel = Math.Max(0.1, mulch?.MulchLevel ?? 0);
                double norm = Math.Clamp(mulchLevel / 100.0, 0, 1);
                return norm * norm * 0.35;
            }
        }
    }

    private class GenerationPressureProvider : IPressureProvider
    {
        private readonly BlockEntityFarmland farmland;

        private static readonly System.Func<double, double> PressureCurve = FunctionUtils.MemoizeStepBounded(
            0.05, 0, 1, x => FunctionUtils.Sigmoid(0.6, 12)(x)
        );

        public GenerationPressureProvider(BlockEntityFarmland farmland) { this.farmland = farmland; }

        public double Value
        {
            get
            {
                var gen = farmland.Behaviors.OfType<IGenerationSource>().FirstOrDefault()?.Generation ?? 0;
                if (gen <= 0) return 0;
                return PressureCurve(Math.Clamp(gen / 10.0, 0, 1));
            }
        }
    }
}
