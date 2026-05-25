namespace Ehm93.VS.Crops.Common;

// Tiny accessor interfaces implemented by each feature mod's farmland behavior, so
// other feature mods can soft-query cross-feature state by behavior-name lookup
// without taking a typed dependency on the other mod's assembly.

public interface IMulchSource
{
    double MulchLevel { get; }
}

public interface IWeedSource
{
    double WeedLevel { get; }
}

public interface ISporeSource
{
    double SporeLevel { get; }
}

public interface IBlightSource
{
    double BlightLevel { get; }
}

public interface IGenerationSource
{
    int Generation { get; }
}
