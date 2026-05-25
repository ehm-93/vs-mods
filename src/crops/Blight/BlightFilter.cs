using System;
using SkiaSharp;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace Ehm93.VS.Crops.Blight;

// Procedurally derives a blighted crop texture from the base texture: darken, tint toward brown,
// and stipple dark necrotic spots. Strength scales with tier (1-5). Deterministic per (base, tier).
internal static class BlightFilter
{
    public static IBitmap? Apply(ICoreClientAPI capi, AssetLocation baseTextureLoc, int tier)
    {
        var assetLoc = new AssetLocation(baseTextureLoc.Domain, "textures/" + baseTextureLoc.Path + ".png");
        var asset = capi.Assets.TryGet(assetLoc);
        if (asset == null) return null;

        SKBitmap? src;
        try
        {
            src = SKBitmap.Decode(asset.Data);
        }
        catch
        {
            return null;
        }
        if (src == null) return null;

        var dst = new SKBitmap(src.Width, src.Height, src.ColorType, src.AlphaType);
        var t = Math.Clamp(tier, 1, 5);
        var darken = 1f - 0.08f * t;       // tier1→0.92, tier5→0.60
        var tintR = t * 4;                 // shift toward warmer
        var tintG = t * 6;                 // sap chlorophyll
        var tintB = t * 8;                 // and blue
        var spotDensity = 0.03 * t;        // 3%→15% pixels stippled darker
        var rand = new Random(unchecked(baseTextureLoc.ToShortString().GetHashCode() * 31 + t));

        for (int y = 0; y < src.Height; y++)
        {
            for (int x = 0; x < src.Width; x++)
            {
                var c = src.GetPixel(x, y);
                if (c.Alpha == 0)
                {
                    dst.SetPixel(x, y, c);
                    continue;
                }

                int r = (int)(c.Red * darken) + tintR;
                int g = (int)(c.Green * darken) - tintG;
                int b = (int)(c.Blue * darken) - tintB;

                if (rand.NextDouble() < spotDensity)
                {
                    r = r * 2 / 5;
                    g = g * 2 / 5;
                    b = b * 2 / 5;
                }

                dst.SetPixel(x, y, new SKColor(
                    (byte)Math.Clamp(r, 0, 255),
                    (byte)Math.Clamp(g, 0, 255),
                    (byte)Math.Clamp(b, 0, 255),
                    c.Alpha));
            }
        }

        src.Dispose();
        return new BitmapExternal(dst);
    }
}
