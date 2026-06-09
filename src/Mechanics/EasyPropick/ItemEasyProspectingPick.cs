using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.ServerMods;

namespace Ehm93.VS.Mechanics.EasyPropick;

// Replaces the prospecting pick's tool modes. Patched onto the vanilla `prospectingpick` item via
// patches/itemtypes/tool/prospectingpick.json (class -> EasyProspectingPick). Subclasses Item directly
// rather than ItemProspectingPick so none of the vanilla density/node machinery (ProPickWorkSpace, the
// 3-sample triangulation, etc.) comes along — we keep only the tool-mode UI and the on-break action.
//
// All three modes run SERVER-SIDE in OnBlockBrokenWith, gated to "propickable" blocks (stone/rock) like
// vanilla, so ordinary mining doesn't trigger a reading. Ore blocks are identified the vanilla way:
// BlockMaterial == Ore and a "type" variant (the ore name); display name is Lang "ore-<type>".
public class ItemEasyProspectingPick : Item
{
    private const int ModeProximity = 0;
    private const int ModeProbability = 1;
    private const int ModeBore = 2;

    // Octant index from HorizontalDir(): 0=N, then clockwise.
    private static readonly string[] EightWinds = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

    // Chunk offsets for the 8 neighbours, in the same order as EightWinds (N = -Z, E = +X).
    private static readonly (int dcx, int dcz)[] NeighborOffsets =
        { (0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1) };

    private SkillItem[] modes = null!;

    private EasyPropickConfig Config =>
        api.ModLoader.GetModSystem<EasyPropickModSystem>()?.Config ?? new EasyPropickConfig();

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        var capi = api as ICoreClientAPI;
        modes = new[]
        {
            MakeMode(capi, "proximity",   "P"),
            MakeMode(capi, "probability", "%"),
            MakeMode(capi, "bore",        "B"),
        };
    }

    private static SkillItem MakeMode(ICoreClientAPI? capi, string code, string letter)
    {
        var si = new SkillItem
        {
            Code = new AssetLocation(code),
            Name = Lang.Get("mechanicseasypropick:mode-" + code),
        };
        if (capi != null) si.WithLetterIcon(capi, letter);
        return si;
    }

    public override void OnUnloaded(ICoreAPI api)
    {
        if (modes != null) foreach (var m in modes) m?.Dispose();
        base.OnUnloaded(api);
    }

    public override SkillItem[] GetToolModes(ItemSlot slot, IClientPlayer forPlayer, BlockSelection blockSel)
    {
        var cfg = Config;
        modes[ModeProximity].Enabled = cfg.EnableProximity;
        modes[ModeProbability].Enabled = cfg.EnableProbability;
        modes[ModeBore].Enabled = cfg.EnableBore;
        return modes;
    }

    public override int GetToolMode(ItemSlot slot, IPlayer byPlayer, BlockSelection blockSel)
        => GameMath.Clamp(slot.Itemstack?.Attributes.GetInt("toolMode", 0) ?? 0, 0, modes.Length - 1);

    public override void SetToolMode(ItemSlot slot, IPlayer byPlayer, BlockSelection blockSel, int toolMode)
        => slot.Itemstack?.Attributes.SetInt("toolMode", toolMode);

    public override bool OnBlockBrokenWith(IWorldAccessor world, Entity byEntity, ItemSlot itemslot, BlockSelection blockSel, float dropQuantityMultiplier = 1f)
    {
        // Prospect server-side, only when a stone/rock ("propickable") block is broken — same gate as
        // vanilla, so ordinary digging doesn't spam readings. Then let the base break the block + damage
        // the tool as usual.
        if (api.Side == EnumAppSide.Server && blockSel != null
            && byEntity is EntityPlayer ep && world.PlayerByUid(ep.PlayerUID) is IServerPlayer splr)
        {
            Block block = world.BlockAccessor.GetBlock(blockSel.Position);
            if (IsPropickable(block)) Prospect(world, splr, itemslot, blockSel.Position);
        }
        return base.OnBlockBrokenWith(world, byEntity, itemslot, blockSel, dropQuantityMultiplier);
    }

    private static bool IsPropickable(Block block)
        => block?.Attributes?["propickable"].AsBool(false) == true;

    private void Prospect(IWorldAccessor world, IServerPlayer splr, ItemSlot slot, BlockPos pos)
    {
        var cfg = Config;
        switch (GetToolMode(slot, splr, null!))
        {
            case ModeProximity:
                if (cfg.EnableProximity)
                {
                    // Scan radius scales with the pick's tool tier: copper(2)->8, bronze(3)->16, iron(4)->24, steel(5)->32.
                    int tier = slot.Itemstack?.Collectible?.GetToolTier(slot) ?? 2;
                    ProximityScan(world, splr, pos, Math.Max(1, tier - 1) * Math.Max(1, cfg.ProximityRangePerTier));
                }
                else Send(splr, "mechanicseasypropick:mode-disabled");
                break;
            case ModeProbability:
                if (cfg.EnableProbability) ProbabilityScan(world, splr, pos, Math.Max(1, cfg.ProbabilitySampleDistance));
                else Send(splr, "mechanicseasypropick:mode-disabled");
                break;
            case ModeBore:
                if (cfg.EnableBore) BoreScan(world, splr, pos, cfg.BoreMaxDepth);
                else Send(splr, "mechanicseasypropick:mode-disabled");
                break;
        }

        // Trailing blank line so consecutive readouts in the info log stay visually separated.
        splr.SendMessage(GlobalConstants.InfoLogChatGroup, "", EnumChatType.Notification);
    }

    // --- Mode 0: Proximity — the nearest occurrence of EACH ore type within a cube, as a compass
    //     direction. One line per ore type, sorted nearest-first. ----------------------------------------
    private void ProximityScan(IWorldAccessor world, IServerPlayer splr, BlockPos center, int range)
    {
        IBlockAccessor ba = world.BlockAccessor;
        int minX = center.X - range, maxX = center.X + range;
        int minZ = center.Z - range, maxZ = center.Z + range;
        int minY = Math.Max(1, center.Y - range), maxY = Math.Min(ba.MapSizeY - 1, center.Y + range);

        // ore type -> nearest occurrence found
        var nearest = new Dictionary<string, (long sq, int x, int y, int z)>();
        BlockPos cur = center.Copy();

        for (int x = minX; x <= maxX; x++)
        {
            long ddx = x - center.X;
            for (int z = minZ; z <= maxZ; z++)
            {
                long ddz = z - center.Z;
                for (int y = minY; y <= maxY; y++)
                {
                    cur.X = x; cur.Y = y; cur.Z = z;
                    Block b = ba.GetBlock(cur);
                    if (b.BlockMaterial != EnumBlockMaterial.Ore || !b.Variant.ContainsKey("type")) continue;
                    long ddy = y - center.Y;
                    long sq = ddx * ddx + ddy * ddy + ddz * ddz;
                    string type = b.Variant["type"];
                    if (!nearest.TryGetValue(type, out var prev) || sq < prev.sq)
                        nearest[type] = (sq, x, y, z);
                }
            }
        }

        if (nearest.Count == 0) { Send(splr, "mechanicseasypropick:proximity-none", range); return; }

        var lines = new List<string>();
        foreach (var kv in nearest.OrderBy(k => k.Value.sq))
        {
            var (_, x, y, z) = kv.Value;
            string dir = EightWinds[HorizontalDir(x - center.X, z - center.Z)];
            string vert = Lang.GetL(splr.LanguageCode, "mechanicseasypropick:vert-" + VerticalDir(y - center.Y));
            string ore = OreLabel(splr.LanguageCode, kv.Key, OrePage(kv.Key));
            lines.Add(Lang.GetL(splr.LanguageCode, "mechanicseasypropick:proximity-line", ore, dir, vert));
        }
        SendReadout(splr, Lang.GetL(splr.LanguageCode, "mechanicseasypropick:proximity-header", range), lines);
    }

    // --- Mode 1: Probability — reads the ore-map density gradient. For each ore present nearby, sample
    //     the 8 neighbouring chunks (at `sampleDist` chunks out) and compare to the chunk underfoot, then
    //     report which way the density is increasing — or that it peaks right here. Cheap; no rock column.
    private void ProbabilityScan(IWorldAccessor world, IServerPlayer splr, BlockPos center, int sampleDist)
    {
        var genDep = (api as ICoreServerAPI)?.ModLoader.GetModSystem<GenDeposits>();
        if (genDep?.Deposits == null) { Send(splr, "mechanicseasypropick:probability-none"); return; }

        int cx0 = (int)Math.Floor(center.X / 32.0);
        int cz0 = (int)Math.Floor(center.Z / 32.0);

        // Per ore code (deduped across deposit variants): density underfoot + at each of the 8 neighbours,
        // taking the max across variants so several deposits of one ore read as a single density field.
        var cells = new Dictionary<string, (string? page, float here, float[] near)>();
        foreach (DepositVariant dep in genDep.Deposits)
        {
            if (!dep.WithOreMap || dep.Code == null) continue;
            if (!cells.TryGetValue(dep.Code, out var c)) c = (dep.HandbookPageCode, 0f, new float[8]);
            c.here = Math.Max(c.here, dep.GetOreMapFactor(cx0, cz0));
            for (int i = 0; i < 8; i++)
                c.near[i] = Math.Max(c.near[i], dep.GetOreMapFactor(cx0 + NeighborOffsets[i].dcx * sampleDist, cz0 + NeighborOffsets[i].dcz * sampleDist));
            cells[dep.Code] = c;
        }

        // For each ore present in the neighbourhood, pick its strongest neighbour.
        var rows = new List<(string code, string? page, float strength, int bestIdx, bool rising)>();
        foreach (var kv in cells)
        {
            var (page, here, near) = kv.Value;
            int bestIdx = 0;
            for (int i = 1; i < 8; i++) if (near[i] > near[bestIdx]) bestIdx = i;
            float strength = Math.Max(here, near[bestIdx]);
            if (strength <= 0f) continue;
            rows.Add((kv.Key, page, strength, bestIdx, near[bestIdx] > here));
        }

        if (rows.Count == 0) { Send(splr, "mechanicseasypropick:probability-none"); return; }

        var lines = new List<string>();
        foreach (var r in rows.OrderByDescending(x => x.strength))
        {
            string oreLink = OreLabel(splr.LanguageCode, r.code, r.page);
            lines.Add(r.rising
                ? Lang.GetL(splr.LanguageCode, "mechanicseasypropick:probability-line", oreLink,
                    Lang.GetL(splr.LanguageCode, "mechanicseasypropick:compass-" + EightWinds[r.bestIdx].ToLowerInvariant()))
                : Lang.GetL(splr.LanguageCode, "mechanicseasypropick:probability-peak", oreLink));
        }
        SendReadout(splr, Lang.GetL(splr.LanguageCode, "mechanicseasypropick:probability-header"), lines);
    }

    // --- Mode 2: Bore — ores in a 3x3 column straight down. -------------------------------------------
    private void BoreScan(IWorldAccessor world, IServerPlayer splr, BlockPos center, int maxDepth)
    {
        IBlockAccessor ba = world.BlockAccessor;
        int topY = Math.Min(ba.MapSizeY - 1, center.Y);
        int bottomY = maxDepth > 0 ? Math.Max(1, topY - maxDepth) : 1;

        var found = new Dictionary<string, int>();
        BlockPos cur = center.Copy();
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int y = topY; y >= bottomY; y--)
                {
                    cur.X = center.X + dx; cur.Y = y; cur.Z = center.Z + dz;
                    Block b = ba.GetBlock(cur);
                    if (b.BlockMaterial == EnumBlockMaterial.Ore && b.Variant.ContainsKey("type"))
                    {
                        string ore = b.Variant["type"];
                        found.TryGetValue(ore, out int c);
                        found[ore] = c + 1;
                    }
                }
            }
        }

        if (found.Count == 0) { Send(splr, "mechanicseasypropick:bore-none"); return; }

        var lines = found.OrderByDescending(k => k.Value)
            .Select(kv => Lang.GetL(splr.LanguageCode, "mechanicseasypropick:bore-line", OreLabel(splr.LanguageCode, kv.Key, OrePage(kv.Key)), kv.Value))
            .ToList();
        SendReadout(splr, Lang.GetL(splr.LanguageCode, "mechanicseasypropick:bore-header"), lines);
    }

    // --- helpers --------------------------------------------------------------------------------------

    // Octant index (0=N, clockwise) for an offset. North = -Z, East = +X, so atan2(dx, -dz) is 0 at north.
    private static int HorizontalDir(int dx, int dz)
    {
        if (dx == 0 && dz == 0) return 0;
        double ang = Math.Atan2(dx, -dz) * (180.0 / Math.PI);
        return ((int)Math.Round(ang / 45.0) % 8 + 8) % 8;
    }

    private static string VerticalDir(int dy) => dy > 2 ? "above" : dy < -2 ? "below" : "level";

    // ore code -> handbook page (from the deposit configs), cached. Lets every mode print the same
    // clickable, consistently-formatted ore name.
    private Dictionary<string, string?>? orePages;
    private string? OrePage(string oreCode)
    {
        if (orePages == null)
        {
            orePages = new Dictionary<string, string?>();
            var deps = (api as ICoreServerAPI)?.ModLoader.GetModSystem<GenDeposits>()?.Deposits;
            if (deps != null)
                foreach (var d in deps)
                    if (d.Code != null) orePages[d.Code] = d.HandbookPageCode;
        }
        return orePages.TryGetValue(oreCode, out var p) ? p : null;
    }

    // Localised ore name, wrapped in a handbook link when the page is known — used by all three modes.
    private static string OreLabel(string langCode, string oreCode, string? page)
    {
        string name = Lang.GetL(langCode, "ore-" + oreCode);
        return page != null ? $"<a href=\"handbook://{page}\">{name}</a>" : name;
    }

    private static void Send(IServerPlayer splr, string langKey, params object[] args)
        => splr.SendMessage(GlobalConstants.InfoLogChatGroup, Lang.GetL(splr.LanguageCode, langKey, args), EnumChatType.Notification);

    // Emit a whole readout as ONE chat message. One SendMessage per ore = one network packet and one
    // chat-log entry each; dozens of those (Probability can list every nearby ore) is a real client-side
    // hitch. Collapsing to a single multi-line message removes it.
    private static void SendReadout(IServerPlayer splr, string header, List<string> lines)
    {
        var sb = new StringBuilder(header);
        foreach (var line in lines) sb.Append('\n').Append(line);
        splr.SendMessage(GlobalConstants.InfoLogChatGroup, sb.ToString(), EnumChatType.Notification);
    }
}
