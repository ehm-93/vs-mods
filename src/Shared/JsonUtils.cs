using Vintagestory.API.Datastructures;

namespace Ehm93.VS.Shared;

public static class JsonUtils
{
    public static double AsDoubleOrDefault(this JsonObject obj, double defaultValue)
    {
        if (obj.Token == null) return defaultValue;
        return obj.AsDouble();
    }

    public static int AsIntOrDefault(this JsonObject obj, int defaultValue)
    {
        if (obj.Token == null) return defaultValue;
        return obj.AsInt();
    }

    public static bool AsBoolOrDefault(this JsonObject obj, bool defaultValue)
    {
        if (obj.Token == null) return defaultValue;
        return obj.AsBool();
    }

    public static string AsStringOrDefault(this JsonObject obj, string defaultValue)
    {
        if (obj.Token == null) return defaultValue;
        return obj.AsString();
    }
}
