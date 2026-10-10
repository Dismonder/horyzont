using System.Numerics;

using Horyzont.Core.Checkpoints;
using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.World;
using Horyzont.Core.World.Blocks;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Testing.Ships;

/// <summary>One block of a fixture ship.</summary>
/// <param name="Key">Block type key.</param>
/// <param name="Cell">Cell.</param>
/// <param name="Orientation">Orientation 0-23.</param>
public readonly record struct ShipBlock(string Key, CellPos Cell, byte Orientation = 0);

/// <summary>
/// Ships for tests. The definitions repeat the numbers of <c>game/content/blocks/blocks.json</c> (Core tests cannot read the
/// content); <c>ShippedContent_FixtureDefinitionsMatchBlocksJson</c> keeps the two equal.
/// </summary>
public static class ShipFixtures
{
    /// <summary>Block type keys in registry order (ids 1 … 17); core:missing is 18.</summary>
    public static readonly string[] BlockKeys =
    [
        "core:stone_block", "core:wood_planks", "core:metal_plate", "core:stone_slab", "core:stone_slope",
        Cockpit, Seat, NavStation, Engine, Stabilizer, FuelTank, Cargo, LandingGear, PulseDrive, Hull, Window, Door,
    ];

    /// <summary>Cockpit.</summary>
    public const string Cockpit = "core:ship_cockpit";

    /// <summary>Passenger seat.</summary>
    public const string Seat = "core:ship_seat";

    /// <summary>Navigator station.</summary>
    public const string NavStation = "core:ship_nav_station";

    /// <summary>Tier 1 engine.</summary>
    public const string Engine = "core:ship_engine_t1";

    /// <summary>Stabilizer.</summary>
    public const string Stabilizer = "core:ship_stabilizer";

    /// <summary>Fuel tank.</summary>
    public const string FuelTank = "core:ship_fuel_tank";

    /// <summary>Cargo bay.</summary>
    public const string Cargo = "core:ship_cargo";

    /// <summary>Landing gear.</summary>
    public const string LandingGear = "core:ship_landing_gear";

    /// <summary>Pulse drive.</summary>
    public const string PulseDrive = "core:ship_pulse_drive";

    /// <summary>Hull.</summary>
    public const string Hull = "core:ship_hull";

    /// <summary>Window.</summary>
    public const string Window = "core:ship_window";

    /// <summary>Door.</summary>
    public const string Door = "core:ship_door";

    /// <summary>Entity id of the fixture construct.</summary>
    public static readonly EntityId ConstructId = new(1);

    private static readonly Dictionary<string, BlockTypeDef> _definitions = CreateDefinitions();

    /// <summary>The 30-block ship; nose toward −Z, up +Y.</summary>
    public static IReadOnlyList<ShipBlock> Thirty { get; } = CreateThirty();

    /// <summary>Definitions by key, including core:missing.</summary>
    public static IReadOnlyDictionary<string, BlockTypeDef> Definitions => _definitions;

    /// <summary>The definitions of one block.</summary>
    /// <param name="key">Block type key.</param>
    /// <returns>The definition.</returns>
    public static BlockTypeDef Def(string key) => _definitions[key];

    /// <summary>Orientation of an engine whose nozzle faces <paramref name="nozzle"/> (it pushes the ship the opposite way).</summary>
    /// <param name="nozzle">Construct face of the nozzle.</param>
    /// <returns>The orientation index.</returns>
    public static byte EngineFacing(Face nozzle) =>
        BlockOrientation.FromUpForward(nozzle is Face.PosY or Face.NegY ? Face.PosZ : Face.PosY, nozzle);

    /// <summary>Content table for a world created by <see cref="CreateWorld"/> (or any world with the fixture keys).</summary>
    /// <param name="state">The world.</param>
    /// <returns>The table.</returns>
    public static BlockContentTable ContentFor(WorldState state) => BlockContentTable.Create(state.BlockTypes, _definitions);

    /// <summary>
    /// A world with one planet, the fixture block types and, optionally, the construct <see cref="ConstructId"/> built like a
    /// loaded checkpoint (static, on the home planet).
    /// </summary>
    /// <param name="blocks">Blocks of the construct; null = no construct.</param>
    /// <param name="origin">Construct origin in the planet frame.</param>
    /// <param name="mode">Construct mode.</param>
    /// <returns>The world.</returns>
    public static WorldState CreateWorld(IReadOnlyList<ShipBlock>? blocks, Vector3 origin = default, ConstructMode mode = ConstructMode.Static)
    {
        var builder = new WorldStateBuilder(WorldFactory.DefaultWorldId, 42);
        builder.SetProgress(default, default, blocks is null ? 1UL : 2UL, 1);
        builder.AddPlanet(WorldFactory.HomePlanetParams);
        for (var i = 0; i < BlockKeys.Length; i++)
        {
            builder.AddBlockType(new RegistryEntry((ushort)(i + 1), BlockKeys[i]));
        }

        builder.AddBlockType(new RegistryEntry((ushort)(BlockKeys.Length + 1), BlockTypeRegistry.MissingKey));
        builder.AddMaterial(new RegistryEntry(1, "stone"));
        builder.AddMaterial(new RegistryEntry(2, "dirt"));
        if (blocks is not null)
        {
            builder.AddConstruct(new ConstructHeaderRecord(
                ConstructId, FrameRef.Of(WorldFactory.HomePlanet), 0, mode, origin, Quaternion.Identity, 1));
            var chunks = new Dictionary<ChunkPos, uint[]>();
            foreach (var block in blocks)
            {
                if (!chunks.TryGetValue(block.Cell.Chunk, out var cells))
                {
                    cells = new uint[ConstructChunk.CellCount];
                    chunks.Add(block.Cell.Chunk, cells);
                }

                cells[block.Cell.LocalIndex] = StateOf(block).Packed;
            }

            foreach (var (position, cells) in chunks.OrderBy(c => c.Key))
            {
                builder.AddChunk(new ConstructChunkRecord(new ConstructChunkKey(ConstructId, position), cells));
            }
        }

        return builder.Build();
    }

    /// <summary>Block state of a fixture block; the key must be in <see cref="BlockKeys"/>.</summary>
    /// <param name="block">The block.</param>
    /// <returns>The state.</returns>
    public static BlockState StateOf(ShipBlock block) =>
        BlockState.Create((ushort)(Array.IndexOf(BlockKeys, block.Key) + 1), block.Orientation);

    /// <summary>Operations that create a construct with the blocks (the first block creates it, the rest are placed).</summary>
    /// <param name="construct">Id of the new construct.</param>
    /// <param name="blocks">Blocks; at least one.</param>
    /// <param name="origin">Origin in the planet frame.</param>
    /// <returns>The operations in application order.</returns>
    public static IEnumerable<Operation> BuildOperations(EntityId construct, IReadOnlyList<ShipBlock> blocks, Vector3 origin)
    {
        yield return new ConstructCreateOp(
            construct, FrameRef.Of(WorldFactory.HomePlanet), origin, Quaternion.Identity, blocks[0].Cell, StateOf(blocks[0]));
        for (var i = 1; i < blocks.Count; i++)
        {
            yield return new BlockSetOp(construct, blocks[i].Cell, StateOf(blocks[i]));
        }
    }

    private static Dictionary<string, BlockTypeDef> CreateDefinitions()
    {
        var all = new (string Key, float Mass, BlockFunction Function)[]
        {
            ("core:stone_block", 2400f, BlockFunction.None),
            ("core:wood_planks", 500f, BlockFunction.None),
            ("core:metal_plate", 200f, BlockFunction.None),
            ("core:stone_slab", 1200f, BlockFunction.None),
            ("core:stone_slope", 1200f, BlockFunction.None),
            (Cockpit, 400f, BlockFunction.Cockpit),
            (Seat, 80f, BlockFunction.CreateSeat(SeatRole.Passenger)),
            (NavStation, 250f, BlockFunction.CreateSeat(SeatRole.Navigator)),
            (Engine, 600f, BlockFunction.CreateEngine(60000f, 1)),
            (Stabilizer, 300f, BlockFunction.CreateStabilizer(40000f)),
            (FuelTank, 250f, BlockFunction.CreateFuelTank(1000f)),
            (Cargo, 200f, BlockFunction.CreateCargo(1)),
            (LandingGear, 150f, BlockFunction.LandingGear),
            (PulseDrive, 800f, BlockFunction.CreatePulseDrive(2000f)),
            (Hull, 200f, BlockFunction.None),
            (Window, 150f, BlockFunction.None),
            (Door, 200f, BlockFunction.Door),
            (BlockTypeRegistry.MissingKey, BlockContentTable.MissingMassKg, BlockFunction.None),
        };
        var result = new Dictionary<string, BlockTypeDef>(StringComparer.Ordinal);
        foreach (var (key, mass, function) in all)
        {
            result.Add(key, new BlockTypeDef(key, BlockShape.Cube, 1, true, mass, function));
        }

        return result;
    }

    private static ShipBlock[] CreateThirty()
    {
        var forward = BlockOrientation.Identity;
        var nozzleDown = EngineFacing(Face.NegY);
        var nozzleBack = EngineFacing(Face.PosZ);
        return
        [
            // Upper deck, nose toward −Z.
            new(Cockpit, new CellPos(2, 2, 0)),
            new(Window, new CellPos(1, 2, 0)),
            new(Window, new CellPos(3, 2, 0)),
            new(Seat, new CellPos(1, 2, 1)),
            new(Seat, new CellPos(3, 2, 1)),
            new(NavStation, new CellPos(2, 2, 1)),
            new(Stabilizer, new CellPos(1, 2, 2)),
            new(Stabilizer, new CellPos(3, 2, 2)),
            new(Cargo, new CellPos(2, 2, 2)),
            new(PulseDrive, new CellPos(2, 2, 3)),
            new(Engine, new CellPos(2, 2, 4), nozzleBack),

            // Lower deck: two engines with the nozzle down, a forward-facing engine, tanks, hull and the door.
            new(Engine, new CellPos(1, 1, 2), nozzleDown),
            new(Engine, new CellPos(3, 1, 2), nozzleDown),
            new(Engine, new CellPos(2, 1, 0), forward),
            new(FuelTank, new CellPos(2, 1, 2)),
            new(FuelTank, new CellPos(2, 1, 3)),
            new(Hull, new CellPos(0, 1, 1)),
            new(Hull, new CellPos(4, 1, 1)),
            new(Hull, new CellPos(0, 1, 4)),
            new(Hull, new CellPos(4, 1, 4)),
            new(Hull, new CellPos(1, 1, 1)),
            new(Hull, new CellPos(3, 1, 1)),
            new(Hull, new CellPos(1, 1, 3)),
            new(Hull, new CellPos(3, 1, 3)),
            new(Hull, new CellPos(2, 1, 1)),
            new(Door, new CellPos(2, 1, 4)),

            // Landing gear: the lowest layer, standing on the ground.
            new(LandingGear, new CellPos(0, 0, 1)),
            new(LandingGear, new CellPos(4, 0, 1)),
            new(LandingGear, new CellPos(0, 0, 4)),
            new(LandingGear, new CellPos(4, 0, 4)),
        ];
    }
}
