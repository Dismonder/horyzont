using Horyzont.Core.Ids;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.Items;

/// <summary>The thing a container belongs to. Create it with the factory methods; unused fields stay at their default.</summary>
/// <param name="Kind">Host kind.</param>
/// <param name="Id">Player, construct, entity or planet id, by <paramref name="Kind"/>.</param>
/// <param name="Cell">Cell of the block for <see cref="ContainerHostKind.ConstructBlock"/>.</param>
/// <param name="Site">Site of a place of interest for <see cref="ContainerHostKind.Poi"/>.</param>
/// <param name="Number">Chest number in the site for <see cref="ContainerHostKind.Poi"/>.</param>
public readonly record struct ContainerHost(ContainerHostKind Kind, ulong Id, CellPos Cell, ulong Site, ulong Number)
{
    /// <summary>Container of a player.</summary>
    /// <param name="player">The player.</param>
    /// <returns>The host.</returns>
    public static ContainerHost Player(PlayerId player) => new(ContainerHostKind.Player, player.Value, default, 0, 0);

    /// <summary>Container of a construct block.</summary>
    /// <param name="construct">The construct.</param>
    /// <param name="cell">Cell of the block.</param>
    /// <returns>The host.</returns>
    public static ContainerHost ConstructBlock(EntityId construct, CellPos cell) => new(ContainerHostKind.ConstructBlock, construct.Value, cell, 0, 0);

    /// <summary>Container of a persistent entity.</summary>
    /// <param name="entity">The entity.</param>
    /// <returns>The host.</returns>
    public static ContainerHost PersistentEntity(EntityId entity) => new(ContainerHostKind.PersistentEntity, entity.Value, default, 0, 0);

    /// <summary>Chest of a place of interest.</summary>
    /// <param name="planet">Planet.</param>
    /// <param name="site">Site on the planet.</param>
    /// <param name="number">Chest number in the site.</param>
    /// <returns>The host.</returns>
    public static ContainerHost Poi(PlanetId planet, ulong site, ulong number) => new(ContainerHostKind.Poi, planet.Value, default, site, number);

    /// <summary>The player of a <see cref="ContainerHostKind.Player"/> host.</summary>
    public PlayerId PlayerId => new(Id);
}
