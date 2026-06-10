using System;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace Ehm93.VS.Primitive.Fireplace;

public class FireplaceModSystem : ModSystem
{
    public const string ModId = "primitivefireplace";
    public const string ConfigFile = "primitivefireplace.json";
    private const string Channel = "primitivefireplace";

    // On the server this is the authoritative config (from ModConfig/primitivefireplace.json). On a client
    // it starts as the client's own file but is overwritten in place with the server's values on join.
    public FireplaceConfig Config { get; private set; } = new();

    private Harmony? patcher;

    public override void StartPre(ICoreAPI api)
    {
        base.StartPre(api);
        try
        {
            Config = api.LoadModConfig<FireplaceConfig>(ConfigFile) ?? new FireplaceConfig();
        }
        catch (Exception e)
        {
            api.Logger.Warning("[{0}] Could not read {1}, using defaults: {2}", ModId, ConfigFile, e.Message);
            Config = new FireplaceConfig();
        }
        // (Re)write so a fresh install gets a template and existing files gain any newly-added fields.
        api.StoreModConfig(Config, ConfigFile);
    }

    public override void Start(ICoreAPI api)
    {
        base.Start(api);
        api.RegisterBlockEntityBehaviorClass("FireplaceStoneRing", typeof(BEBehaviorStoneRing));

        if (!Harmony.HasAnyPatches(ModId))
        {
            patcher = new Harmony(ModId);
            patcher.PatchCategory(ModId);
        }
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        var channel = api.Network.RegisterChannel(Channel).RegisterMessageType<FireplaceConfig>();
        // Push the server's config to each client once they're fully in the world.
        api.Event.PlayerNowPlaying += player => channel.SendPacket(Config, player);
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        api.Network.RegisterChannel(Channel)
            .RegisterMessageType<FireplaceConfig>()
            // Mutate in place so the patches holding Config see the server's values.
            .SetMessageHandler<FireplaceConfig>(packet => Config.CopyFrom(packet));
    }

    public override void Dispose()
    {
        patcher?.UnpatchAll(ModId);
    }
}
