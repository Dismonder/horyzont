using System.Numerics;
using System.Reflection;

using Horyzont.Core.Checkpoints;
using Horyzont.Core.Ids;
using Horyzont.Core.Space;
using Horyzont.Core.World;
using Horyzont.Core.World.Blocks;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Testing.Ships;

/// <summary>
/// Worlds with ship-mode constructs for frame tests (K5-20). The construct header is loaded with <see cref="ConstructMode.Ship"/>
/// through <see cref="WorldStateBuilder"/>, the way a checkpoint loads it, so no ship-mode operation (K5-23) is needed.
/// </summary>
public static class TestShips
{
    /// <summary>Entity id of the moving ship of <see cref="CreateDefault"/>.</summary>
    public static readonly EntityId Moving = new(1);

    /// <summary>Entity id of the parked ship of <see cref="CreateDefault"/>.</summary>
    public static readonly EntityId Parked = new(2);

    /// <summary>Block type id of stone in these worlds.</summary>
    public const ushort StoneTypeId = 1;

    /// <summary>The home planet frame.</summary>
    public static FrameRef HomeFrame => FrameRef.Of(WorldFactory.HomePlanet);

    /// <summary>A world with the home planet, two ship-mode constructs 10 m above the north pole (<see cref="Moving"/>, <see cref="Parked"/>).</summary>
    /// <returns>The world.</returns>
    public static WorldState CreateDefault() => CreateWorld(
        new TestShip(Moving, HomeFrame, new Vector3(0f, 4010f, 0f), Quaternion.Identity),
        new TestShip(Parked, HomeFrame, new Vector3(50f, 4010f, 0f), Quaternion.Identity));

    /// <summary>A world with the home planet and the given constructs (ids must be below 100).</summary>
    /// <param name="ships">The constructs.</param>
    /// <returns>The world.</returns>
    public static WorldState CreateWorld(params TestShip[] ships) => Create(42, [WorldFactory.HomePlanetParams], null, ships);

    /// <summary>A world with the star system of <paramref name="seed"/> (K5-21: four bodies) and the given constructs (ids below 100).</summary>
    /// <param name="seed">Galaxy seed.</param>
    /// <param name="ships">The constructs.</param>
    /// <returns>The world.</returns>
    public static WorldState CreateStarSystemWorld(ulong seed, params TestShip[] ships)
    {
        var created = StarSystemGenesis.CreateWorldBodies(seed, WorldFactory.HomePlanetParams.GeneratorProfile);
        return Create(seed, created.Bodies, created.System, ships);
    }

    private static WorldState Create(ulong seed, IReadOnlyList<PlanetParams> planets, StarSystemParams? system, TestShip[] ships)
    {
        ArgumentNullException.ThrowIfNull(ships);
        var builder = new WorldStateBuilder(WorldFactory.DefaultWorldId, seed);
        builder.SetProgress(default, default, 100, 1);
        foreach (var planet in planets)
        {
            builder.AddPlanet(planet);
        }

        if (system is not null)
        {
            builder.SetStarSystem(system);
        }

        builder.AddBlockType(new RegistryEntry(StoneTypeId, WorldFactory.StoneBlockKey));
        builder.AddBlockType(new RegistryEntry(StoneTypeId + 1, BlockTypeRegistry.MissingKey));
        builder.AddMaterial(new RegistryEntry(1, "stone"));
        builder.AddMaterial(new RegistryEntry(2, "dirt"));
        foreach (var ship in ships)
        {
            builder.AddConstruct(new ConstructHeaderRecord(ship.Id, ship.Parent, 0, ship.Mode, ship.Origin, ship.Rotation, 1));
            var chunks = new Dictionary<ChunkPos, uint[]>();
            for (var x = 0; x < ship.Size; x++)
            {
                for (var z = 0; z < ship.Size; z++)
                {
                    var cell = new CellPos(x, 0, z);
                    if (!chunks.TryGetValue(cell.Chunk, out var blocks))
                    {
                        blocks = new uint[ConstructChunk.CellCount];
                        chunks.Add(cell.Chunk, blocks);
                    }

                    blocks[cell.LocalIndex] = BlockState.Create(StoneTypeId, 0).Packed;
                }
            }

            foreach (var (chunk, blocks) in chunks)
            {
                builder.AddChunk(new ConstructChunkRecord(new ConstructChunkKey(ship.Id, chunk), blocks));
            }
        }

        return builder.Build();
    }

    /// <summary>
    /// Switches the mode of a live construct in place, the effect K5-23's <c>ShipModeSetOp</c> will have. Until that operation exists
    /// only tests do this, through the backing field of the read-only <see cref="Construct.Mode"/>.
    /// </summary>
    /// <param name="construct">The construct.</param>
    /// <param name="mode">The new mode.</param>
    public static void ForceMode(Construct construct, ConstructMode mode)
    {
        ArgumentNullException.ThrowIfNull(construct);
        var field = typeof(Construct).GetField("<Mode>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Construct.Mode has no auto-property backing field any more.");
        field.SetValue(construct, mode);
    }
}
