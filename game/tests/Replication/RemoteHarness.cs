using Godot;

using Horyzont.Client;
using Horyzont.Client.Replication;
using Horyzont.Client.Session;
using Horyzont.Constructs.Content;
using Horyzont.Constructs.Meshing;
using Horyzont.Core.Checkpoints;
using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.World;
using Horyzont.Core.World.Constructs;
using Horyzont.Game.Client.Replication;
using Horyzont.Game.Constructs;
using Horyzont.Game.Tests.Constructs;
using Horyzont.Net.Session;
using Horyzont.Net.Transport;
using Horyzont.Net.Transport.InMemory;
using Horyzont.Testing;

using Microsoft.Extensions.Logging.Abstractions;

using Quaternion = System.Numerics.Quaternion;
using Vector3 = System.Numerics.Vector3;

namespace Horyzont.Game.Tests.Replication;

/// <summary>
/// A remote client without a network: a <see cref="RemoteConstructs"/> over a <see cref="ConstructMirror"/> that the test feeds with the
/// messages the server would send. The client runtime is never connected; the node only needs it for its listener registration.
/// </summary>
internal sealed class RemoteHarness : IDisposable
{
    public static readonly EntityId Construct = new(5);

    private readonly InMemoryNetwork _network = new(TimeProvider.System);
    private readonly InMemoryTransport _transport;

    public RemoteHarness(Node host, Action<MeshJobSlot>? beforeBuild = null, bool render = false)
    {
        Render = render;
        Catalog = BlockContentLoader.Load();
        State = ConstructTestWorld.NewState(Catalog);
        _transport = _network.CreateTransport("client", TransportOptions.Default);
        Client = new ClientRuntime(_transport, new ClientOptions { BuildHash = 1 }, false, TimeProvider.System, NullLoggerFactory.Instance);
        Mirror = new ConstructMirror(TimeProvider.System);
        Player = new FakePlayer();
        Constructs = new RemoteConstructs { Name = "RemoteConstructs", BeforeBuild = beforeBuild };
        Constructs.Initialize(Client, Mirror, Player, Catalog, render, NullLoggerFactory.Instance, TimeProvider.System);
        host.AddChild(Constructs);
        Hello = new ServerHello(
            State.Id, State.CurrentTick, 60, new PlayerId(1), new EntityId(1), State.Planets.All, State.BlockTypes.Entries, State.Materials.Entries);
        Constructs.OnServerHello(Hello);
        Table = BlockRenderTable.Build(State.BlockTypes.Entries, Catalog);
        State.BlockTypes.TryGetId("core:stone_block", out var stone);
        Stone = BlockState.Create(stone, 0);
    }

    /// <summary>Gets whether the world draws meshes (false: collision only, no render surfaces).</summary>
    public bool Render { get; }

    public BlockTypeCatalog Catalog { get; }

    public WorldState State { get; }

    public ClientRuntime Client { get; }

    public ConstructMirror Mirror { get; }

    public FakePlayer Player { get; }

    public RemoteConstructs Constructs { get; }

    public ServerHello Hello { get; }

    public BlockRenderTable Table { get; }

    public BlockState Stone { get; }

    public FrameRef Planet => FrameRef.Of(WorldFactory.HomePlanet);

    public ConstructHeaderRecord Header(uint version, Vector3? origin = null) =>
        new(Construct, Planet, 0, ConstructMode.Static, origin ?? new Vector3(5, 4000, 5), Quaternion.Identity, version);

    public ConstructChunkRecord Chunk(ChunkPos chunk, params (int Index, BlockState Block)[] blocks)
    {
        var data = new uint[4096];
        foreach (var (index, block) in blocks)
        {
            data[index] = block.Packed;
        }

        return new ConstructChunkRecord(new ConstructChunkKey(Construct, chunk), data);
    }

    /// <summary>Delivers a complete state in one part, the way the server sends a small construct.</summary>
    public void Publish(uint version, params ConstructChunkRecord[] chunks) =>
        Mirror.Apply(new ConstructState(Header(version), 0, 1, chunks));

    public void Edit(ulong opId, uint version, CellPos cell, bool clear = false) =>
        Mirror.Apply(new OperationApplied(new OpId(opId), default,
            clear ? new BlockClearOp(Construct, cell) : new BlockSetOp(Construct, cell, Stone), [new ConstructChange(Construct, version, false)]));

    /// <summary>The mesher output for a chunk of the mirror, computed on the calling thread.</summary>
    public ChunkMeshData Expected(ChunkPos chunk)
    {
        var input = new ChunkMeshInput();
        var data = new ChunkMeshData();
        input.Fill(Mirror, new ConstructChunkKey(Construct, chunk), 1);
        new ChunkMesher().Build(input, Table, data);
        return data;
    }

    public void Dispose()
    {
        Constructs.OnDisconnected(Horyzont.Core.Protocol.DisconnectCode.Normal);
        Constructs.Shutdown.GetAwaiter().GetResult();
        Constructs.GetParent()?.RemoveChild(Constructs);
        Constructs.Free();
        _transport.Dispose();
    }

    /// <summary>A stationary local player at the surface.</summary>
    internal sealed class FakePlayer : ILocalPlayerView
    {
        public bool IsActive => true;

        public PlanetId Planet => WorldFactory.HomePlanet;

        public Vector3 PredictedFeet { get; set; } = new(0, 4000, 0);

        public Quaternion BodyRotation => Quaternion.Identity;

        public float Pitch => 0f;

        public Vector3 Eye => PredictedFeet + new Vector3(0, 1.7f, 0);

        public Vector3 CameraOrigin => Eye;

        public Vector3 CameraDirection => -Vector3.UnitZ;

        public CameraMode CameraMode => CameraMode.FirstPerson;
    }
}
