using ProtoBuf;

namespace Ehm93.VS.Primitive.DryFuels;

// User-tunable settings, written to ModConfig/dryfuels.json on first run via LoadModConfig/StoreModConfig
// (the same pattern ACulinaryArtillery and Butchering use). Burn properties are applied to the seasoned
// fuels in DryFuelsModSystem.AssetsFinalize; the seasoning timings are read by BEBehaviorFuelSeasoning.
//
// Also a network packet: the server pushes its config to each client on join (DryFuelsModSystem) so burn
// tooltips and seasoning progress match the server. ImplicitFields serializes every public field by
// declaration order — keep new fields appended to the END so existing clients stay wire-compatible.
[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class DryFuelsConfig
{
    // Burn properties of the seasoned (cured) fuels this mod adds. Fresh firewood/peat keep their vanilla
    // values — these are the longer-burning variants that fresh fuel seasons INTO.
    public float FirewoodBurnDuration = 120f;
    public int FirewoodBurnTemperature = 700;
    public float PeatBrickBurnDuration = 125f;
    public int PeatBrickBurnTemperature = 900;

    // In-game hours of ideal (temperate, sheltered) seasoning needed to fully cure a pile. Local climate
    // scales the real time around this — warm/dry/indoors cures faster, cold/wet/exposed slower.
    public double SeasonHours = 2160;

    // Extra seasoning-rate multiplier when the fuel pile sits in a fully enclosed room (a woodshed).
    public double RoomDryingBonus = 2.0;

    // Copy values in place (not a reference swap) so anything holding this instance sees the synced values.
    public void CopyFrom(DryFuelsConfig o)
    {
        FirewoodBurnDuration = o.FirewoodBurnDuration;
        FirewoodBurnTemperature = o.FirewoodBurnTemperature;
        PeatBrickBurnDuration = o.PeatBrickBurnDuration;
        PeatBrickBurnTemperature = o.PeatBrickBurnTemperature;
        SeasonHours = o.SeasonHours;
        RoomDryingBonus = o.RoomDryingBonus;
    }
}
