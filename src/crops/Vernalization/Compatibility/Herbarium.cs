using System;

namespace Ehm93.VS.Crops.Vernalization;

internal static class Herbarium
{
    public static bool IsLoaded() => Type.GetType("herbarium.Herbarium, Herbarium") != null;
}
