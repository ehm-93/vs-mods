using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Util;

namespace Ehm93.VS.Primitive.Pemmican;

// One drying/smoking recipe, loaded from assets/<domain>/recipes/drying/*.json. The fields mirror VS
// grid-recipe conventions so anyone who has written a grid recipe recognizes them: an input `code` with
// a `*` wildcard, an optional `name` that captures what the `*` matched, and an output `code` that
// interpolates that capture with `{name}`. `allowedVariants`/`skipVariants` are BARE variant names
// (e.g. "blueberry"), exactly as in grid recipes. Matching uses VS's own WildcardUtil so behavior and
// case-sensitivity match the engine; keep codes lower-case. One recipe like
//   input game:fruit-*  ->  output expandedfoods:dryfruit-{type}
// therefore covers every fruit variant at once.
public class DryingRecipe
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
}

// A resolved match for an input stack: the output item plus how much/how long. A struct so Match()
// can be called freely in the tick loop without per-call heap allocation.
public readonly struct DryingResult
{
    public readonly Item Output;
    public readonly int Quantity;
    public readonly double Hours;
    public DryingResult(Item output, int quantity, double hours) { Output = output; Quantity = quantity; Hours = hours; }
}
