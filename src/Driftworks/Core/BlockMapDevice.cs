using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Ehm93.VS.Driftworks.Core.Internal;

namespace Ehm93.VS.Driftworks.Core;

/// <summary>
/// The map device. Persistent and uncraftable (placed by worldgen, or in creative for now).
/// Right-clicking it opens an ephemeral run and drops you in — the spine of the run loop. The
/// socket / key / modifier activation flow lands in later build steps.
/// </summary>
public class BlockMapDevice : Block
{
    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        if (api.Side == EnumAppSide.Server && byPlayer is IServerPlayer player)
        {
            var core = api.ModLoader.GetModSystem<CoreModSystem>();
            if (core?.Runs is RunManager runs)
            {
                runs.TryOpenRun(player, out string msg);
                player.SendMessage(GlobalConstants.GeneralChatGroup, msg, EnumChatType.Notification);
            }
            else
            {
                player.SendMessage(GlobalConstants.GeneralChatGroup,
                    "Map device offline (Manifold unavailable).", EnumChatType.Notification);
            }
        }

        return true;
    }
}
