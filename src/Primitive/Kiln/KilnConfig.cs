using ProtoBuf;

namespace Ehm93.VS.Primitive.Kiln;

// User-tunable settings, written to ModConfig/primitivekiln.json on first run (LoadModConfig/StoreModConfig).
// Also a network packet: the server pushes its config to each client on join (KilnModSystem) so the clamp
// hover text shows the server's numbers. ImplicitFields serializes every public field by declaration order —
// keep new fields appended to the END so existing clients stay wire-compatible.
[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class KilnConfig
{
    // Largest bounding-cube edge (in blocks) a single clamp may span; the flood fill stops growing past this.
    public int MaxSize = 5;

    // Burn-seconds of fuel required per rawbrick ground-storage pile before the clamp can be lit. A fuel item
    // contributes its own combustible BurnDuration (firewood 24, peat 25, charcoal/coke higher). ~4 firewood/pile.
    public float FuelSecondsPerPile = 96f;

    // Firing time once lit AND sealed, in in-game (calendar) hours — a flat 4 in-game days regardless of clamp
    // size. The clamp's tradeoff vs the pit kiln (~20h, tiny batches) is bulk and efficiency in exchange for a
    // slow firing and some brick loss; this is deliberately far slower than the pit kiln.
    public float FireHours = 96f;

    // Fraction of each pile's bricks lost when it fires — a rough clamp wastes more than a proper kiln.
    public float BrickLossMin = 0.10f;
    public float BrickLossMax = 0.20f;

    // Copy values in place (not a reference swap) so anything holding this instance sees the synced values.
    public void CopyFrom(KilnConfig o)
    {
        MaxSize = o.MaxSize;
        FuelSecondsPerPile = o.FuelSecondsPerPile;
        FireHours = o.FireHours;
        BrickLossMin = o.BrickLossMin;
        BrickLossMax = o.BrickLossMax;
    }
}
