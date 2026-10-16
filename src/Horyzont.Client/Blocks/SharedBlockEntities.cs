using System.Diagnostics.CodeAnalysis;

using Horyzont.Core.BlockEntities;
using Horyzont.Core.Ids;
using Horyzont.Core.World;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Client.Blocks;

/// <summary>
/// The block states of a client that shares the world with the server (solo and host): the server sends no construct data to the
/// local player of a host, so the states are read from the world state itself, which is always current. Main thread only.
/// </summary>
/// <param name="world">The world state of the server in this process.</param>
public sealed class SharedBlockEntities(WorldState world) : IBlockEntityView
{
    private readonly WorldState _world = world ?? throw new ArgumentNullException(nameof(world));

    /// <inheritdoc />
    public bool TryGet(EntityId construct, CellPos cell, [NotNullWhen(true)] out BlockEntityState? state) =>
        _world.BlockEntities.TryGet(new BlockRef(construct, cell), out state);
}
