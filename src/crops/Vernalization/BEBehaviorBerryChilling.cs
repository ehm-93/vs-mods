using System;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Ehm93.VS.Shared;
using Ehm93.VS.Crops.Common;

namespace Ehm93.VS.Crops.Vernalization;

// Cold-dormancy ("vernalization") requirement for fruiting bushes. A bush must accumulate enough
// chill hours (temperature <= chillTemp) before it may begin a new fruiting cycle. The
// Mature -> Flowering transition is gated on Vernalized (see FruitingBushPatches); beginning to
// flower consumes the accumulated chill, so the bush needs a fresh cold period to fruit again.
public class BEBehaviorBerryChilling : BlockEntityBehavior
{
    protected readonly Func<bool> InGreenhouse;
    protected double chilledHours = 0;
    protected double lastCheckTotalHours = 0;
    protected double chillTemp = 0;
    protected double chilledHoursRequired = 0;
    protected double devernalizationThreshold = 0.50;
    protected double devernalizationTemperature = 0;
    protected double devernalizationFactor = 0.6667;
    protected double forceDevernalizationTemperature = 0;
    protected double forceDevernalizationFactor = 0;

    public double ChillProgress => chilledHoursRequired <= 0 ? 1 : Math.Clamp(chilledHours / chilledHoursRequired, 0, 1);

    public bool Vernalized => ChillProgress >= 1;

    public BEBehaviorBerryChilling(BlockEntity blockentity) : base(blockentity)
    {
        InGreenhouse = FunctionUtils.MemoizeFor(
            TimeSpan.FromMinutes(2),
            () => GreenhouseUtil.IsGreenhouse(Api, Pos)
        );
    }

    public override void Initialize(ICoreAPI api, JsonObject properties)
    {
        base.Initialize(api, properties);

        chillTemp = properties["chillTemp"].AsDouble(chillTemp);
        chilledHoursRequired = properties["chilledDaysRequired"].AsDouble(chilledHoursRequired / Api.World.Calendar.HoursPerDay) * Api.World.Calendar.HoursPerDay;
        devernalizationThreshold = properties["devernalizationThreshold"].AsDouble(devernalizationThreshold);
        devernalizationTemperature = properties["devernalizationTemperature"].AsDouble(chillTemp + 3);
        devernalizationFactor = properties["devernalizationFactor"].AsDouble(devernalizationFactor);
        forceDevernalizationTemperature = properties["forceDevernalizationTemperature"].AsDouble(devernalizationTemperature + 5);
        forceDevernalizationFactor = properties["forceDevernalizationFactor"].AsDouble(forceDevernalizationFactor);

        if (api is ICoreServerAPI) Blockentity.RegisterGameTickListener(ServerTick, 4500 + Api.World.Rand.Next(1000));
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetDouble("chilledHours", chilledHours);
        tree.SetDouble("lastCheckTotalHours", lastCheckTotalHours);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        chilledHours = tree.TryGetDouble("chilledHours") ?? 0;
        lastCheckTotalHours = tree.TryGetDouble("lastCheckTotalHours") ?? 0;
    }

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        base.GetBlockInfo(forPlayer, dsc);
        if (chilledHoursRequired <= 0 || Vernalized) return;
        dsc.AppendLine(Lang.Get("Vernalizing: {0}% (needs cold below {1}°C to fruit)", (int)Math.Round(ChillProgress * 100), chillTemp));
    }

    // Called by the growth gate when a bush successfully begins flowering: the cold period has been
    // "used up", so the bush must accumulate a fresh dormancy before its next fruiting cycle.
    public virtual void ConsumeVernalization()
    {
        if (chilledHours == 0) return;
        chilledHours = 0;
        Blockentity.MarkDirty(true);
    }

    protected virtual void ServerTick(float df) => CheckChill();

    protected virtual void CheckChill()
    {
        const double intervalHours = 2.0;
        var now = Api.World.Calendar.TotalHours;

        if (lastCheckTotalHours == 0)
        {
            lastCheckTotalHours = now;
            return;
        }

        var before = chilledHours;
        var checkTime = lastCheckTotalHours;

        // Catch up over any time the bush went unticked (e.g. while its chunk was unloaded),
        // sampling historical temperature in 2-hour steps.
        while (checkTime + intervalHours <= now)
        {
            checkTime += intervalHours;
            var temp = Api.World.BlockAccessor.GetClimateAt(
                Pos,
                EnumGetClimateMode.ForSuppliedDate_TemperatureOnly,
                checkTime / Api.World.Calendar.HoursPerDay
            ).Temperature;
            temp += InGreenhouse() ? 5 : 0;
            AccumulateChill(temp, intervalHours);
        }

        var tempNow = Api.World.BlockAccessor.GetClimateAt(Pos).Temperature + (InGreenhouse() ? 5 : 0);
        AccumulateChill(tempNow, now - checkTime);

        lastCheckTotalHours = now;
        chilledHours = Math.Clamp(chilledHours, 0, chilledHoursRequired);
        if (before != chilledHours) Blockentity.MarkDirty(true);
    }

    protected virtual void AccumulateChill(double temp, double hours)
    {
        if (hours <= 0) return;
        if (temp <= chillTemp) chilledHours += hours;
        else if (temp > forceDevernalizationTemperature) chilledHours *= Math.Pow(forceDevernalizationFactor, hours);
        else if (temp > devernalizationTemperature && ChillProgress < devernalizationThreshold)
            chilledHours *= Math.Pow(devernalizationFactor, hours);
    }
}
