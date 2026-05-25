using System;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;
using Ehm93.VS.Shared;
using Ehm93.VS.Crops.Common;

namespace Ehm93.VS.Crops.Aging;

public class BEBehaviorFarmlandAging : BlockEntityBehavior
{
    private const float DefaultK = 0.0001f;
    private const float DefaultMaxNutrientsFromAging = 65f;

    private float k = DefaultK;
    private float maxNutrientsFromAging = DefaultMaxNutrientsFromAging;
    private double lastCheckTotalHours = 0;
    private float[] nutrientRemainders = new float[3].Fill(0);

    public BlockEntityFarmland FarmlandEntity => (BlockEntityFarmland)Blockentity;

    public BEBehaviorFarmlandAging(BlockEntity blockentity) : base(blockentity)
    {
        if (blockentity is not BlockEntityFarmland)
            throw new ArgumentException($"FarmlandAging may only be used on BlockEntityFarmland, found {blockentity.GetType().Name}");
    }

    public override void Initialize(ICoreAPI api, JsonObject properties)
    {
        base.Initialize(api, properties);

        k = (float)(properties["agingRate"].AsDouble(DefaultK));
        maxNutrientsFromAging = (float)(properties["agingMax"].AsDouble(DefaultMaxNutrientsFromAging));

        if (api is ICoreServerAPI && api.World.Config.GetBool("processCrops", defaultValue: true))
        {
            Blockentity.RegisterGameTickListener(ServerTick, 25_000 + api.World.Rand.Next(10_000));
        }
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetDouble("lastCheckTotalHours", lastCheckTotalHours);
        tree.SetFloat("nR0", nutrientRemainders[0]);
        tree.SetFloat("nR1", nutrientRemainders[1]);
        tree.SetFloat("nR2", nutrientRemainders[2]);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        lastCheckTotalHours = tree.TryGetDouble("lastCheckTotalHours") ?? 0;
        nutrientRemainders[0] = tree.GetFloat("nR0");
        nutrientRemainders[1] = tree.GetFloat("nR1");
        nutrientRemainders[2] = tree.GetFloat("nR2");
    }

    protected virtual void ServerTick(float df)
    {
        var prev = lastCheckTotalHours;
        var now = Api.World.Calendar.TotalHours;
        var deltaHours = now - prev;
        lastCheckTotalHours = now;

        if (HasCrop() || IsBlighted() || prev == 0) return;

        for (int i = 0; i < 3; i++)
        {
            float current = FarmlandEntity.OriginalFertility[i] + nutrientRemainders[i];
            float updated = ComputeNutrients(current, (float)deltaHours);
            int floored = Math.Clamp((int)Math.Floor(updated), 0, 100);
            nutrientRemainders[i] = updated - floored;
            FarmlandEntity.OriginalFertility[i] = floored;
        }

        FarmlandEntity.MarkDirty();
    }

    protected virtual double BoostCoef()
    {
        var moisture = MoistureCoef(FarmlandEntity.MoistureLevel);
        var temp = TempCoef(Api.World.BlockAccessor.GetClimateAt(Pos).Temperature + (InGreenhouse() ? 5 : 0));
        var mulchLevel = FarmlandEntity.Behaviors.OfType<IMulchSource>().FirstOrDefault()?.MulchLevel ?? 0;
        var mulch = MulchCoef(mulchLevel / 100);
        return moisture * temp * mulch;
    }

    protected virtual bool HasCrop() => Api.World.BlockAccessor.GetBlock(Pos.UpCopy()) is BlockCrop;

    protected virtual bool IsBlighted() =>
        (FarmlandEntity.Behaviors.OfType<ISporeSource>().FirstOrDefault()?.SporeLevel ?? 0) > 0;

    protected virtual float ComputeNutrients(float current, float deltaHours)
    {
        double effectiveK = k * BoostCoef();
        return maxNutrientsFromAging - (maxNutrientsFromAging - current) * (float)Math.Exp(-effectiveK * deltaHours);
    }

    protected virtual double MoistureCoef(double m)
    {
        const double steepness = 12.0;
        const double midpoint = 0.65;
        const double max = 2.0;
        return max * FunctionUtils.Sigmoid(midpoint, steepness)(m);
    }

    protected virtual double TempCoef(double tempC)
    {
        if (tempC < 0) return 0.2;
        if (tempC > 35) return 0.4;
        return 0.2 + 0.8 * Math.Exp(-Math.Pow((tempC - 20) / 10.0, 2));
    }

    protected virtual double MulchCoef(double mulchiness) => 1.0 + 0.5 * mulchiness;

    protected virtual bool InGreenhouse() => GreenhouseUtil.IsGreenhouse(Api, Pos);
}
