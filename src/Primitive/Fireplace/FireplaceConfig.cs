using ProtoBuf;

namespace Ehm93.VS.Primitive.Fireplace;

// User-tunable settings, written to ModConfig/primitivefireplace.json on first run via
// LoadModConfig/StoreModConfig (same pattern as DryFuels).
//
// Also a network packet: the server pushes its config to each client on join (FireplaceModSystem) so
// the firepit hover text shows the server's numbers. ImplicitFields serializes every public field by
// declaration order — keep new fields appended to the END so existing clients stay wire-compatible.
[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class FireplaceConfig
{
    // Stones needed to complete the ring. Also sets the mesh positions (stones are spaced evenly
    // around the rim), so values much above 8 will look crowded.
    public int RingSize = 8;

    // Extra burn duration once the ring is complete. 1.0 means every piece of fuel lasts twice as long
    // (firewood's stock ~25s burn is brutally short, so a full stone ring doubles it).
    public float FuelBonus = 1.0f;

    // Copy values in place (not a reference swap) so anything holding this instance sees the synced values.
    public void CopyFrom(FireplaceConfig o)
    {
        RingSize = o.RingSize;
        FuelBonus = o.FuelBonus;
    }
}
