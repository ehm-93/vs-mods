using ProtoBuf;

namespace Ehm93.VS.Mechanics.EasyPropick;

// Server-owned settings, written to ModConfig/mechanicseasypropick.json on first run and pushed to each
// client on join (see EasyPropickModSystem) — the client needs them so the tool-mode selector greys out
// disabled modes. Also a network packet: ImplicitFields serializes every public field by declaration
// order, so APPEND new fields to the END to stay wire-compatible with older clients.
[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class EasyPropickConfig
{
    /// Individually enable/disable each prospecting mode.
    public bool EnableProximity = true;
    public bool EnableProbability = true;
    public bool EnableBore = true;

    /// Proximity: scan-radius blocks granted per prospecting-pick tool tier above stone. The scanned cube's
    /// half-extent is (toolTier - 1) * this, so with the default 8: copper 8, bronze 16, iron 24, steel 32.
    public int ProximityRangePerTier = 8;

    /// Probability: how many chunks out to sample the 8 neighbours when reading the density gradient
    /// (1 = the immediately adjacent chunks). Larger = a smoother, broader-scale trend.
    public int ProbabilitySampleDistance = 1;

    /// Bore: max blocks scanned downward per column. 0 = down to the world bottom.
    public int BoreMaxDepth = 0;

    // Copy in place (not a reference swap) so anything holding this instance sees the synced values.
    public void CopyFrom(EasyPropickConfig o)
    {
        EnableProximity = o.EnableProximity;
        EnableProbability = o.EnableProbability;
        EnableBore = o.EnableBore;
        ProximityRangePerTier = o.ProximityRangePerTier;
        ProbabilitySampleDistance = o.ProbabilitySampleDistance;
        BoreMaxDepth = o.BoreMaxDepth;
    }
}
