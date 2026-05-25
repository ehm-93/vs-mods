using System;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Ehm93.VS.Shared;
using Ehm93.VS.Crops.Common;

namespace Ehm93.VS.Crops.Vernalization;

public class BEBehaviorBerryChilling : BlockEntityBehavior, ICheckGrow, IOnExchanged
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

    public bool Chilling
    {
        get => ChillProgress < 1;
        set
        {
            if (Chilling == value) return;
            if (!Chilling) chilledHours = 0;
            else chilledHours = chilledHoursRequired;
            Blockentity.MarkDirty(true);
        }
    }

    public double ChillProgress => chilledHoursRequired == 0 ? 1 : Math.Clamp(chilledHours / chilledHoursRequired, 0, 1);

    public BEBehaviorBerryChilling(BlockEntity blockentity) : base(blockentity)
    {
        InGreenhouse = FunctionUtils.MemoizeFor(
            TimeSpan.FromMinutes(2),
            () =>
            {
                var rooms = Api.ModLoader.GetModSystem<RoomRegistry>();
                var room = rooms.GetRoomForPosition(Pos);
                return room != null && room.SkylightCount > room.NonSkylightCount && room.ExitCount == 0;
            }
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

        if (Block.Variant?["state"] == "ripe") Chilling = false;

        if (Api is ICoreServerAPI) Blockentity.RegisterGameTickListener(ServerTick, 4500 + Api.World.Rand.Next(1000));
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
        if (Chilling)
        {
            dsc.Clear();
            dsc.AppendLine(Lang.Get("Dormant"));
            dsc.AppendLine(Lang.Get("Vernalized below: {0}°C", chillTemp));
            dsc.AppendLine(Lang.Get("Vernalization progress: {0}%", Math.Round(ChillProgress * 100)));
        }
    }

    public virtual void OnExchanged(Block block)
    {
        if (Block == block) return;
        if (block?.Variant?["state"] == "empty") Chilling = true;
    }

    public virtual bool CheckGrow() => !Chilling || ChillProgress >= 1;

    protected virtual void ServerTick(float df) => CheckChill();

    protected virtual void CheckChill()
    {
        const double intervalHours = 2.0;
        var now = Api.World.Calendar.TotalHours;

        if (!Chilling || lastCheckTotalHours == 0)
        {
            lastCheckTotalHours = now;
            return;
        }

        double progressBefore = ChillProgress;
        double checkTime = lastCheckTotalHours;

        while (checkTime + intervalHours <= now)
        {
            checkTime += intervalHours;
            var temp = Api.World.BlockAccessor.GetClimateAt(
                Pos,
                EnumGetClimateMode.ForSuppliedDate_TemperatureOnly,
                checkTime / Api.World.Calendar.HoursPerDay
            ).Temperature;
            temp += InGreenhouse() ? 5 : 0;
            if (temp <= chillTemp) chilledHours += intervalHours;
            else if (temp > forceDevernalizationTemperature) chilledHours *= Math.Pow(forceDevernalizationFactor, intervalHours);
            else if (temp > devernalizationTemperature && ChillProgress < devernalizationThreshold)
                chilledHours *= Math.Pow(devernalizationFactor, intervalHours);
        }

        var tempNow = Api.World.BlockAccessor.GetClimateAt(Pos).Temperature + (InGreenhouse() ? 5 : 0);
        var remainingHours = now - checkTime;
        if (tempNow <= chillTemp) chilledHours += remainingHours;
        else if (tempNow > forceDevernalizationTemperature) chilledHours *= Math.Pow(forceDevernalizationFactor, remainingHours);
        else if (tempNow > devernalizationTemperature && ChillProgress < devernalizationThreshold)
            chilledHours *= Math.Pow(devernalizationFactor, remainingHours);

        lastCheckTotalHours = now;
        if (progressBefore != ChillProgress) Blockentity.MarkDirty(true);
    }
}
