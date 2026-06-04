using System.IO;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Util;

namespace Ehm93.VS.Primitive.Pemmican;

// One drying/smoking recipe, loaded server-side from assets/<domain>/recipes/drying/*.json into a
// RecipeRegistryGeneric and synced to clients (which never read recipes/ assets). The fields mirror VS
// grid-recipe conventions so anyone who has written a grid recipe recognizes them: an input `code` with
// a `*` wildcard, an optional `name` that captures what the `*` matched, and an output `code` that
// interpolates that capture with `{name}`. `allowedVariants`/`skipVariants` are BARE variant names
// (e.g. "blueberry"), exactly as in grid recipes. Matching uses VS's own WildcardUtil so behavior and
// case-sensitivity match the engine; keep codes lower-case. One recipe like
//   input game:fruit-*  ->  output expandedfoods:dryfruit-{type}
// therefore covers every fruit variant at once.
public class DryingRecipe : IByteSerializable
{
    // Optional identifier — handy for debugging, the handbook, or patches that target a single recipe.
    [JsonProperty("code")] public string? Code;

    // Set false to disable a recipe without deleting it (a patch can flip this off).
    [JsonProperty("enabled")] public bool Enabled = true;

    // Vanilla JSON-patch dependency form: the recipe loads only when every entry is satisfied. A plain
    // { "modid": "expandedfoods" } requires that mod; add "invert": true to require it to be ABSENT.
    [JsonProperty("dependsOn")] public ModDependence[]? DependsOn;

    [JsonProperty("input")] public DryingInput Input = new();
    [JsonProperty("output")] public DryingOutput Output = new();

    // In-game hours on a lit rack to finish. A mixed rack takes as long as its slowest piece.
    [JsonProperty("hours")] public double Hours = 24.0;

    // If true this only finishes over active fire/smoke and never air-dries. Honored by the standalone
    // drying rack; the smoking firepit always provides fire, so it ignores this.
    [JsonProperty("requiresFire")] public bool RequiresFire;

    // The input wildcard parsed once at load (e.g. "game:fruit-*" -> domain "game", path "fruit-*").
    AssetLocation pattern = new("game", "*");

    public void Init() => pattern = new AssetLocation(Input.Code.Contains(':') ? Input.Code : "game:" + Input.Code);

    // True when every dependsOn entry holds: the mod is present (or absent, if invert). Mirrors how
    // vanilla JSON patches gate on mods.
    public bool DependenciesSatisfied(IModLoader loader)
    {
        if (DependsOn == null) return true;
        foreach (ModDependence dep in DependsOn)
            if (loader.IsModEnabled(dep.ModId) == dep.Invert) return false;
        return true;
    }

    // The output code for a matching input, or null if this recipe doesn't apply to it.
    public AssetLocation? OutputFor(AssetLocation code)
    {
        // Gate: domain + wildcard glob, optionally restricted to allowedVariants (bare variant names).
        if (!WildcardUtil.Match(pattern, code, Input.AllowedVariants)) return null;
        // Blacklist: reject when the matched variant is one of skipVariants (same bare-name semantics).
        if (Input.SkipVariants != null && WildcardUtil.Match(pattern, code, Input.SkipVariants)) return null;

        // Substitute the captured variant into the output's {name} token (grid convention); otherwise
        // the output is a fixed code and the variant is ignored (e.g. all redmeat-* -> jerky-redmeat).
        string token = "{" + (Input.Name ?? "variant") + "}";
        if (Output.Code.Contains(token))
        {
            string? variant = WildcardUtil.GetWildcardValue(pattern, code);
            if (string.IsNullOrEmpty(variant)) return null; // need a real capture to fill the token
            return new AssetLocation(Output.Code.Replace(token, variant));
        }
        return new AssetLocation(Output.Code);
    }

    // The render override for a hung stack: the input's when the stack is a drying input, the output's when
    // it is this recipe's dried result, else null (so the rack falls back to its default scale/position).
    public RenderTransform? RenderFor(AssetLocation code)
    {
        if (OutputFor(code) != null) return Input.Render;
        if (MatchesOutput(code)) return Output.Render;
        return null;
    }

    bool MatchesOutput(AssetLocation code)
    {
        string outCode = Output.Code.Contains(':') ? Output.Code : "game:" + Output.Code;
        string token = "{" + (Input.Name ?? "variant") + "}";
        if (outCode.Contains(token))
            return WildcardUtil.Match(new AssetLocation(outCode.Replace(token, "*")), code);
        return new AssetLocation(outCode).Equals(code);
    }

    // --- IByteSerializable: the server loads recipes from JSON; the registry serializes them to each
    // client on connect (clients never read recipes/ assets). DependsOn is intentionally NOT serialized —
    // recipes are filtered by it server-side before being added to the registry, so only satisfied
    // recipes ever reach a client. Init() rebuilds the (non-serialized) wildcard pattern after a read.
    public void ToBytes(BinaryWriter writer)
    {
        writer.Write(Code ?? "");
        writer.Write(Enabled);
        writer.Write(Input.Code ?? "");
        WriteOpt(writer, Input.Name);
        WriteArr(writer, Input.AllowedVariants);
        WriteArr(writer, Input.SkipVariants);
        WriteRender(writer, Input.Render);
        writer.Write(Output.Code ?? "");
        writer.Write(Output.Quantity);
        WriteRender(writer, Output.Render);
        writer.Write(Hours);
        writer.Write(RequiresFire);
    }

    public void FromBytes(BinaryReader reader, IWorldAccessor resolver)
    {
        Code = reader.ReadString();
        Enabled = reader.ReadBoolean();
        Input = new DryingInput
        {
            Code = reader.ReadString(),
            Name = ReadOpt(reader),
            AllowedVariants = ReadArr(reader),
            SkipVariants = ReadArr(reader),
            Render = ReadRender(reader),
        };
        Output = new DryingOutput
        {
            Code = reader.ReadString(),
            Quantity = reader.ReadInt32(),
            Render = ReadRender(reader),
        };
        Hours = reader.ReadDouble();
        RequiresFire = reader.ReadBoolean();
        Init();
    }

    static void WriteOpt(BinaryWriter w, string? s) { w.Write(s != null); if (s != null) w.Write(s); }
    static string? ReadOpt(BinaryReader r) => r.ReadBoolean() ? r.ReadString() : null;

    static void WriteArr(BinaryWriter w, string[]? a)
    {
        w.Write(a?.Length ?? -1);
        if (a != null) foreach (string s in a) w.Write(s);
    }
    static string[]? ReadArr(BinaryReader r)
    {
        int n = r.ReadInt32();
        if (n < 0) return null;
        var a = new string[n];
        for (int i = 0; i < n; i++) a[i] = r.ReadString();
        return a;
    }

    static void WriteRender(BinaryWriter w, RenderTransform? rt)
    {
        w.Write(rt != null);
        if (rt == null) return;
        w.Write(rt.Scale.HasValue);
        if (rt.Scale.HasValue) w.Write(rt.Scale.Value);
        w.Write(rt.Translation?.Length ?? -1);
        if (rt.Translation != null) foreach (float f in rt.Translation) w.Write(f);
    }
    static RenderTransform? ReadRender(BinaryReader r)
    {
        if (!r.ReadBoolean()) return null;
        var rt = new RenderTransform();
        if (r.ReadBoolean()) rt.Scale = r.ReadSingle();
        int n = r.ReadInt32();
        if (n >= 0) { rt.Translation = new float[n]; for (int i = 0; i < n; i++) rt.Translation[i] = r.ReadSingle(); }
        return rt;
    }
}

// Per-recipe override for how a hung piece renders on the drying rack: an absolute item `scale` and/or a
// `translation` [x, y, z] offset (block units, 0..1) from the piece's default hang position. Either may be
// omitted, in which case the rack's default is used for that part.
public class RenderTransform
{
    [JsonProperty("scale")] public float? Scale;
    [JsonProperty("translation")] public float[]? Translation;
}

public class DryingInput
{
    // Item/block code with a `*` wildcard, e.g. "game:fruit-*" or "butchering:primemeat-*". The `*` need
    // not be trailing ("agedmeat-*-normal" is valid); use a single `*` for predictable variant capture.
    [JsonProperty("code")] public string Code = "";

    // Names the `*` capture so the output can reference it as {name}. Required if the output uses a token.
    [JsonProperty("name")] public string? Name;

    // Whitelist: if set, only these bare variant names match (e.g. ["blueberry","cranberry"]).
    [JsonProperty("allowedVariants")] public string[]? AllowedVariants;

    // Blacklist: these bare variant names are excluded (e.g. ["healing","curedhealing"]).
    [JsonProperty("skipVariants")] public string[]? SkipVariants;

    // Optional override for how a hung INPUT (raw) piece renders on the rack.
    [JsonProperty("render")] public RenderTransform? Render;
}

// Mirrors the vanilla JSON-patch dependency object ({ "modid": "...", "invert": false }).
public class ModDependence
{
    [JsonProperty("modid")] public string ModId = "";
    [JsonProperty("invert")] public bool Invert;
}

public class DryingOutput
{
    // The result item code. May embed the input's capture as a {name} token: "expandedfoods:dryfruit-{type}".
    [JsonProperty("code")] public string Code = "";

    // Output stack size per piece dried (default 1).
    [JsonProperty("quantity")] public int Quantity = 1;

    // Optional override for how the dried OUTPUT piece renders on the rack.
    [JsonProperty("render")] public RenderTransform? Render;
}

// A resolved match for an input stack: the output item plus how much/how long. A struct so Match()
// can be called freely in the tick loop without per-call heap allocation.
public readonly struct DryingResult
{
    public readonly Item Output;
    public readonly int Quantity;
    public readonly double Hours;
    // When true the recipe only finishes over active fire/smoke; the standalone air-drying rack skips it.
    public readonly bool RequiresFire;
    public DryingResult(Item output, int quantity, double hours, bool requiresFire)
    { Output = output; Quantity = quantity; Hours = hours; RequiresFire = requiresFire; }
}
