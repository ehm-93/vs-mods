using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace Ehm93.VS.Mechanics.EasyPropick;

public class EasyPropickModSystem : ModSystem
{
    public const string ModId = "mechanicseasypropick";
    private const string ConfigFile = "mechanicseasypropick.json";
    private const string Channel = "mechanicseasypropick";

    // The live config. On the server it's the on-disk config; on the client it's whatever the server
    // pushed on join. ItemEasyProspectingPick reads this via api.ModLoader.GetModSystem<...>().Config.
    public EasyPropickConfig Config { get; private set; } = new EasyPropickConfig();

    public override void StartPre(ICoreAPI api)
    {
        base.StartPre(api);
        try
        {
            Config = api.LoadModConfig<EasyPropickConfig>(ConfigFile) ?? new EasyPropickConfig();
        }
        catch (Exception e)
        {
            api.Logger.Warning("[{0}] Could not read {1}, using defaults: {2}", ModId, ConfigFile, e.Message);
            Config = new EasyPropickConfig();
        }
        // (Re)write so a fresh install gets a template and existing files gain any newly-added fields.
        api.StoreModConfig(Config, ConfigFile);
    }

    public override void Start(ICoreAPI api)
    {
        base.Start(api);
        api.RegisterItemClass("EasyProspectingPick", typeof(ItemEasyProspectingPick));
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        var channel = api.Network.RegisterChannel(Channel).RegisterMessageType<EasyPropickConfig>();
        // Push the server's config to each client once they're fully in the world.
        api.Event.PlayerNowPlaying += player => channel.SendPacket(Config, player);
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        api.Network.RegisterChannel(Channel)
            .RegisterMessageType<EasyPropickConfig>()
            .SetMessageHandler<EasyPropickConfig>(packet => Config.CopyFrom(packet));
    }
}
