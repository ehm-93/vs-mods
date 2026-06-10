using System;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace Ehm93.VS.Primitive.Kiln;

public class KilnModSystem : ModSystem
{
    public const string ModId = "primitivekiln";
    public const string ConfigFile = "primitivekiln.json";
    private const string Channel = "primitivekiln";

    // On the server this is the authoritative config (from ModConfig/primitivekiln.json). On a client it
    // starts as the client's own file but is overwritten in place with the server's values on join.
    public KilnConfig Config { get; private set; } = new();

    private Harmony? patcher;

    public override void StartPre(ICoreAPI api)
    {
        base.StartPre(api);
        try
        {
            Config = api.LoadModConfig<KilnConfig>(ConfigFile) ?? new KilnConfig();
        }
        catch (Exception e)
        {
            api.Logger.Warning("[{0}] Could not read {1}, using defaults: {2}", ModId, ConfigFile, e.Message);
            Config = new KilnConfig();
        }
        // (Re)write so a fresh install gets a template and existing files gain any newly-added fields.
        api.StoreModConfig(Config, ConfigFile);
    }

    public override void Start(ICoreAPI api)
    {
        base.Start(api);
        api.RegisterBlockEntityBehaviorClass("BrickClamp", typeof(BEBehaviorBrickClamp));

        if (!Harmony.HasAnyPatches(ModId))
        {
            patcher = new Harmony(ModId);
            patcher.PatchCategory(ModId);
        }
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        var channel = api.Network.RegisterChannel(Channel).RegisterMessageType<KilnConfig>();
        // Push the server's config to each client once they're fully in the world.
        api.Event.PlayerNowPlaying += player => channel.SendPacket(Config, player);
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        api.Network.RegisterChannel(Channel)
            .RegisterMessageType<KilnConfig>()
            // Mutate in place so the patches/behaviors holding Config see the server's values.
            .SetMessageHandler<KilnConfig>(packet => Config.CopyFrom(packet));

        // Insert the brick-clamp's fuel textures into the block atlas up front (main thread). Most aren't used
        // by any block, and inserting them from the tesselation thread yields the magenta unknown texture.
        api.Event.BlockTexturesLoaded += () => BEBehaviorBrickClamp.PreloadFuelTextures(api);
    }

    public override void Dispose()
    {
        patcher?.UnpatchAll(ModId);
    }
}
