using System;

namespace Ehm93.VS.Driftworks.Core.Api;

/// <summary>
/// A run-dungeon room a content pack contributes. <see cref="Build"/> stamps the room's interior
/// features into a cell (see <see cref="RoomContext"/>); the generator owns the cell shell and the
/// doorways between cells. Register rooms via <c>DriftworksContentModSystem.RegisterRoom</c>.
/// </summary>
public sealed class RoomDef
{
    public RoomDef(string code, Action<RoomContext> build)
    {
        Code = code;
        Build = build ?? throw new ArgumentNullException(nameof(build));
    }

    /// <summary>Stable identifier, e.g. "driftworkssample:pillars".</summary>
    public string Code { get; }

    /// <summary>Stamps the room's interior features into a cell.</summary>
    public Action<RoomContext> Build { get; }
}
