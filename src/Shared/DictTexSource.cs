using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace Ehm93.VS.Shared;

public class DictTexSource : ITexPositionSource
{
    private readonly Dictionary<string, TextureAtlasPosition> texMap;
    private readonly Size2i atlasSize;

    public DictTexSource(Dictionary<string, TextureAtlasPosition> texMap, Size2i atlasSize)
    {
        this.texMap = texMap;
        this.atlasSize = atlasSize;
    }

    public Size2i AtlasSize => atlasSize;

    public TextureAtlasPosition this[string textureCode] =>
            texMap.TryGetValue(textureCode, out var pos) ? pos : null;
}
