using System;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Ehm93.VS.Shared;
using Ehm93.VS.Crops.Common;

namespace Ehm93.VS.Crops.Mulch;

public class BEBehaviorFarmlandMulch : BlockEntityBehavior, IOnBlockInteract, IMulchSource
{
    protected MeshData? mulchQuad;
    protected TextureAtlasPosition? mulchTexturePos;
    protected double lastMulchTotalHours = 0;
    protected double lastMulchTickTotalHours = 0;
    private readonly Random rand = new();
    private readonly Func<bool> IsExposedToRain;
    private double _mulchLevel = 0;

    public double MulchLevel
    {
        get => _mulchLevel;
        protected set
        {
            double clamped = Math.Clamp(value, 0, 100);
            if (_mulchLevel != clamped)
            {
                _mulchLevel = clamped;
                GenMulchQuad();
                FarmlandEntity.MarkDirty(redrawOnClient: true);
            }
        }
    }

    public BlockEntityFarmland FarmlandEntity => (BlockEntityFarmland)Blockentity;

    public BEBehaviorFarmlandMulch(BlockEntity blockEntity) : base(blockEntity)
    {
        if (blockEntity is not BlockEntityFarmland)
            throw new ArgumentException("FarmlandMulch behavior may only be used on farmland.");
        IsExposedToRain = FunctionUtils.MemoizeFor(TimeSpan.FromSeconds(60), DoIsExposedToRain);
    }

    public override void Initialize(ICoreAPI api, JsonObject properties)
    {
        base.Initialize(api, properties);
        if (GenMulchQuad()) FarmlandEntity.MarkDirty(redrawOnClient: true);
        if (api is ICoreServerAPI && api.World.Config.GetBool("processCrops", defaultValue: true))
        {
            FarmlandEntity.RegisterGameTickListener(Tick, 3900 + rand.Next(200));
        }
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetDouble("lastMulchTotalHours", lastMulchTotalHours);
        tree.SetDouble("lastMulchTickTotalHours", lastMulchTickTotalHours);
        tree.SetDouble("_mulchLevel", _mulchLevel);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        lastMulchTotalHours = tree.TryGetDouble("lastMulchTotalHours") ?? 0;
        lastMulchTickTotalHours = tree.TryGetDouble("lastMulchTickTotalHours") ?? 0;
        MulchLevel = tree.TryGetDouble("_mulchLevel") ?? 0;
    }

    public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tesselator)
    {
        if (mulchQuad != null) mesher.AddMeshData(mulchQuad);
        return base.OnTesselation(mesher, tesselator);
    }

    public virtual bool OnBlockInteract(IPlayer byPlayer)
    {
        var slot = byPlayer.InventoryManager.ActiveHotbarSlot;
        if (slot?.Itemstack == null) return false;
        if (slot.Itemstack.Collectible.Code.Path != "drygrass") return false;
        return OnBlockInteractWithDryGrass(byPlayer, slot);
    }

    public virtual void Tick(float df) => TickMulch();

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        base.GetBlockInfo(forPlayer, dsc);
        var rounded = Math.Round(MulchLevel);
        if (0 < rounded) dsc.AppendLine(Lang.Get("Mulch: {0}%", (int)rounded));
    }

    protected virtual void TickMulch()
    {
        if (MulchLevel == 0 || lastMulchTickTotalHours == 0)
        {
            lastMulchTickTotalHours = Api.World.Calendar.TotalHours;
            return;
        }

        var diffTotalDays = (Api.World.Calendar.TotalHours - lastMulchTotalHours) / 24.0;
        var diffLastTickTotalDays = (Api.World.Calendar.TotalHours - lastMulchTickTotalHours) / 24.0;

        var decayConst = 5;
        var decayCoef = diffTotalDays switch
        {
            < 1 => 0,
            < 6 => 1,
            < 12 => 1.5,
            _ => 2,
        };

        if (IsExposedToRain() && Api.World.BlockAccessor.GetClimateAt(Pos).Rainfall > 0.1)
        {
            decayCoef *= 1.5;
        }

        var decay = decayCoef * decayConst * diffLastTickTotalDays;
        MulchLevel -= decay;
        lastMulchTickTotalHours = Api.World.Calendar.TotalHours;
    }

    protected virtual bool OnBlockInteractWithDryGrass(IPlayer byPlayer, ItemSlot slot)
    {
        if (MulchLevel >= 100) return false;

        MulchLevel += 35;
        lastMulchTotalHours = Api.World.Calendar.TotalHours;
        if (!byPlayer.WorldData.CurrentGameMode.HasFlag(EnumGameMode.Creative))
        {
            slot.TakeOut(1);
            slot.MarkDirty();
        }

        Api.World.PlaySoundAt(
            new AssetLocation("game:sounds/block/dirt1"),
            Pos.X + 0.5, Pos.Y + 0.5, Pos.Z + 0.5,
            byPlayer,
            randomizePitch: true,
            range: 8
        );

        FarmlandEntity.MarkDirty();
        return true;
    }

    protected virtual AssetLocation MulchTextureLocation()
    {
        string mulchTexture = MulchLevel switch
        {
            <= 34 => "low",
            <= 67 => "med",
            <= 100 => "high",
            _ => "low",
        };
        return new AssetLocation($"cropsmulch:block/soil/farmland/mulch-{mulchTexture}");
    }

    protected virtual AssetLocation MulchShapeLocation() =>
        new("cropsmulch:shapes/block/soil/farmland/mulch.json");

    private bool GenMulchQuad()
    {
        if (Api is not ICoreClientAPI capi) return false;

        if (MulchLevel == 0)
        {
            if (mulchQuad != null) { mulchQuad = null; return true; }
            return false;
        }

        Shape? shape = capi.Assets.Get(MulchShapeLocation())?.ToObject<Shape>();
        if (shape == null) return false;

        capi.BlockTextureAtlas.GetOrInsertTexture(MulchTextureLocation(), out _, out mulchTexturePos);
        var texMap = new Dictionary<string, TextureAtlasPosition> { { "side", mulchTexturePos } };
        var texSource = new DictTexSource(texMap, capi.BlockTextureAtlas.Size);

        capi.Tesselator.TesselateShape(
            new TesselationMetaData
            {
                TypeForLogging = "farmland mulch quad",
                TexSource = texSource,
            },
            shape,
            out mulchQuad
        );
        return true;
    }

    private bool DoIsExposedToRain()
    {
        int rainHeight = Api.World.BlockAccessor.GetRainMapHeightAt(Pos.X, Pos.Z);
        return rainHeight <= Pos.Y;
    }
}
