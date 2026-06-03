using System.Collections.Generic;
using Ehm93.VS.Driftworks.Core.Api;
using Vintagestory.API.Common;

namespace Ehm93.VS.Driftworks.Core;

/// <summary>
/// The content extension point: content packs register run-dungeon rooms here (and, later,
/// modifiers, drops, etc.). Deliberately free of any Manifold/dimension types, so a content mod can
/// reference Driftworks core and register rooms without taking on the core's dependencies.
/// </summary>
public class DriftworksContentModSystem : ModSystem
{
    private readonly List<RoomDef> rooms = new();

    /// <summary>All registered rooms (the run-dungeon generator picks from these per cell).</summary>
    public IReadOnlyList<RoomDef> Rooms => rooms;

    /// <summary>Register a room for the run-dungeon generator. Call from your StartServerSide.</summary>
    public void RegisterRoom(RoomDef room)
    {
        if (room != null) rooms.Add(room);
    }
}
