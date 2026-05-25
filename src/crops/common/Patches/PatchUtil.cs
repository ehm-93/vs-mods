using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace Ehm93.VS.Crops.Common;

public static class PatchUtil
{
    public static void PatchAllOverrides(Harmony harmony, Type baseType, string methodName, HarmonyMethod? prefix = null, HarmonyMethod? postfix = null)
    {
        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        var typesToPatch = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a =>
            {
                try { return a.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t != null)!; }
            })
            .Where(t => baseType.IsAssignableFrom(t))
            .Select(t => (type: t, method: t.GetMethod(methodName, flags)))
            .Where(x => x.method != null || x.type == baseType)
            .Select(x => x.method ?? baseType.GetMethod(methodName, flags & ~BindingFlags.DeclaredOnly));

        foreach (var method in typesToPatch.Distinct())
        {
            if (method == null) continue;
            harmony.Patch(method, prefix, postfix);
        }
    }
}
