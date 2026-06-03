using System;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Manifold.Api.Server;
using Ehm93.VS.Driftworks.Core.Internal;

namespace Ehm93.VS.Driftworks.Core;

/// <summary>
/// Driftworks core. Owns the run-dimension lifecycle (the <see cref="RunManager"/>), the map
/// device, and the run controller; content packs supply the flavor (tilesets, modifiers, drops,
/// entities).
/// </summary>
public class CoreModSystem : ModSystem
{
    public const string ModId = "driftworkscore";

    /// <summary>The run controller, available server-side once Manifold is ready (null otherwise).</summary>
    internal RunManager? Runs { get; private set; }

    private RunSandbox? sandbox;

    // Run after Manifold (its facade initialises at ExecuteOrder 0.05) so GetManifoldServer is ready.
    public override double ExecuteOrder() => 0.5;

    public override void Start(ICoreAPI api)
    {
        api.RegisterBlockClass("DriftworksMapDevice", typeof(BlockMapDevice));
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        IManifoldServer manifold;
        try
        {
            manifold = api.GetManifoldServer(this);
        }
        catch (Exception e)
        {
            api.Logger.Error("[{0}] Manifold not available ({1}). Install Manifold >= 0.4.0; runs disabled.", ModId, e.Message);
            return;
        }

        if (!manifold.IsHealthy)
        {
            api.Logger.Warning("[{0}] Manifold reports unhealthy (Harmony patches failed at boot); runs disabled.", ModId);
            return;
        }

        var content = api.ModLoader.GetModSystem<DriftworksContentModSystem>();
        Runs = new RunManager(api, manifold, content);
        sandbox = new RunSandbox(api, Runs);
        sandbox.RegisterCommands();
        api.Logger.Notification("[{0}] Ready. Map device active; debug: /dw open | exit | close | status | cycle [n] | house.", ModId);
    }
}
