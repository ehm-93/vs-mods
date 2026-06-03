using System;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Manifold.Api.Server;
using Ehm93.VS.Driftworks.Core.Internal;

namespace Ehm93.VS.Driftworks.Core;

/// <summary>
/// Driftworks core. Owns the run-dimension lifecycle, the map device, and the run controller;
/// content packs supply the flavor (tilesets, modifiers, drops, entities).
///
/// At this stage it stands up build-step 1 only: a debug harness (<c>/dw</c>) that proves the
/// ephemeral run-dimension round trip on top of Manifold — open, enter, exit, and release the
/// dimension index, with index recycling — before anything real sits on it.
/// </summary>
public class CoreModSystem : ModSystem
{
    public const string ModId = "driftworkscore";

    private RunSandbox? sandbox;

    // Run after Manifold (its facade initialises at ExecuteOrder 0.05) so GetManifoldServer is ready.
    public override double ExecuteOrder() => 0.5;

    public override void StartServerSide(ICoreServerAPI api)
    {
        IManifoldServer manifold;
        try
        {
            manifold = api.GetManifoldServer(this);
        }
        catch (Exception e)
        {
            api.Logger.Error("[{0}] Manifold not available ({1}). Install Manifold >= 0.4.0; run dimensions disabled.", ModId, e.Message);
            return;
        }

        if (!manifold.IsHealthy)
        {
            api.Logger.Warning("[{0}] Manifold reports unhealthy (Harmony patches failed at boot); run dimensions disabled.", ModId);
            return;
        }

        sandbox = new RunSandbox(api, manifold);
        sandbox.RegisterCommands();
        api.Logger.Notification("[{0}] Ready. Debug harness: /dw open | exit | close | status | cycle [n].", ModId);
    }
}
