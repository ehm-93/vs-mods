using System;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace Ehm93.VS.Primitive.DryFuels;

public class DryFuelsModSystem : ModSystem
{
    public const string ModId = "dryfuels";
    public const string ConfigFile = "dryfuels.json";
    private const string Channel = "dryfuels";

    // On the server this is the authoritative config (from ModConfig/dryfuels.json). On a client it starts
    // as the client's own file but is overwritten in place with the server's values on join.
    public DryFuelsConfig Config { get; private set; } = new();

    private Harmony? patcher;

    public override void StartPre(ICoreAPI api)
    {
        base.StartPre(api);
        try
        {
            Config = api.LoadModConfig<DryFuelsConfig>(ConfigFile) ?? new DryFuelsConfig();
        }
        catch (Exception e)
        {
            api.Logger.Warning("[dryfuels] Could not read {0}, using defaults: {1}", ConfigFile, e.Message);
            Config = new DryFuelsConfig();
        }
        // (Re)write so a fresh install gets a template and existing files gain any newly-added fields.
        api.StoreModConfig(Config, ConfigFile);
    }

    public override void Start(ICoreAPI api)
    {
        base.Start(api);
        api.RegisterBlockEntityBehaviorClass("FuelSeasoning", typeof(BEBehaviorFuelSeasoning));
        api.RegisterBlockEntityClass("SeasonedCharcoalPit", typeof(BlockEntitySeasonedCharcoalPit));

        if (!Harmony.HasAnyPatches(ModId))
        {
            patcher = new Harmony(ModId);
            patcher.PatchCategory(ModId);
        }
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        var channel = api.Network.RegisterChannel(Channel).RegisterMessageType<DryFuelsConfig>();
        // Push the server's config to each client once they're fully in the world.
        api.Event.PlayerNowPlaying += player => channel.SendPacket(Config, player);
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        api.Network.RegisterChannel(Channel)
            .RegisterMessageType<DryFuelsConfig>()
            .SetMessageHandler<DryFuelsConfig>(packet =>
            {
                Config.CopyFrom(packet);      // mutate in place so behaviors holding Config see the new values
                ApplyBurnProps(api);          // re-tint the fuels' combustible props to the server's values
            });
    }

    // Override the seasoned fuels' combustible props from config. Runs at asset load on both sides (local
    // config), and again on the client when the server's config arrives.
    public override void AssetsFinalize(ICoreAPI api)
    {
        base.AssetsFinalize(api);
        ApplyBurnProps(api);
    }

    private void ApplyBurnProps(ICoreAPI api)
    {
        ApplyBurn(api.World.GetItem(new AssetLocation("dryfuels:firewood"))?.CombustibleProps,
            Config.FirewoodBurnDuration, Config.FirewoodBurnTemperature);
        ApplyBurn(api.World.GetBlock(new AssetLocation("dryfuels:peatbrick"))?.CombustibleProps,
            Config.PeatBrickBurnDuration, Config.PeatBrickBurnTemperature);
    }

    private static void ApplyBurn(CombustibleProperties? props, float duration, int temperature)
    {
        if (props == null) return;
        props.BurnDuration = duration;
        props.BurnTemperature = temperature;
    }

    public override void Dispose()
    {
        patcher?.UnpatchAll(ModId);
    }
}
