using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Motion;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Simulation;
using Horyzont.Core.World;
using Horyzont.Game.Character;
using Horyzont.Game.Server.Characters;
using Horyzont.Game.Tests.Support;
using Horyzont.Game.Tests.Terrain;

namespace Horyzont.Game.Tests.Character.Fixtures;

/// <summary>An <see cref="IInputSource"/> that hands out what a test queued, one input per call.</summary>
public sealed class FakeInputSource : IInputSource
{
    private readonly Dictionary<PlayerId, Queue<(PlayerInput Input, uint Seq)>> _queues = [];

    /// <summary>Queues an input of a player.</summary>
    /// <param name="player">The player.</param>
    /// <param name="seq">Its sequence number.</param>
    /// <param name="input">The input.</param>
    public void Enqueue(PlayerId player, uint seq, PlayerInput input)
    {
        if (!_queues.TryGetValue(player, out var queue))
        {
            _queues[player] = queue = new Queue<(PlayerInput, uint)>();
        }

        queue.Enqueue((input, seq));
    }

    /// <summary>Gets how many inputs of a player wait.</summary>
    /// <param name="player">The player.</param>
    /// <returns>The count.</returns>
    public int Pending(PlayerId player) => _queues.TryGetValue(player, out var queue) ? queue.Count : 0;

    /// <inheritdoc />
    public bool TryTakeInput(PlayerId player, out PlayerInput input, out uint seq)
    {
        if (_queues.TryGetValue(player, out var queue) && queue.Count > 0)
        {
            (input, seq) = queue.Dequeue();
            return true;
        }

        input = default;
        seq = 0;
        return false;
    }
}

/// <summary>
/// A server world with <see cref="ServerCharacters"/> on a terrain of a test generator, stepped once per physics frame. Players join
/// through the simulation like the hooks do; the test controls the inputs.
/// </summary>
internal sealed class CharacterRig : IAsyncDisposable
{
    private readonly EngineTestScene _scene;
    private readonly PhysicsStepper _stepper;

    private CharacterRig(
        EngineTestScene scene, TerrainTestWorld world, ServerCharacters characters, FakeInputSource inputs, RecordingLogger<ServerCharacters> log,
        PhysicsStepper stepper)
    {
        _scene = scene;
        World = world;
        Characters = characters;
        Inputs = inputs;
        Log = log;
        _stepper = stepper;
    }

    /// <summary>Gets the terrain world.</summary>
    public TerrainTestWorld World { get; }

    /// <summary>Gets the characters under test.</summary>
    public ServerCharacters Characters { get; }

    /// <summary>Gets the fake input.</summary>
    public FakeInputSource Inputs { get; }

    /// <summary>Gets the log of the characters.</summary>
    public RecordingLogger<ServerCharacters> Log { get; }

    /// <summary>Gets the simulation.</summary>
    public ServerSimulation Sim => World.Sim;

    /// <summary>Gets the number of physics frames stepped.</summary>
    public long Ticks => _stepper.Ticks;

    /// <summary>Gets or sets what runs in each physics frame after the characters stepped.</summary>
    public Action<long>? AfterStep { get; set; }

    /// <summary>Gets or sets what runs in each physics frame before the characters step (the test queues the inputs here).</summary>
    public Action<long>? BeforeStep { get; set; }

    /// <summary>Creates the rig.</summary>
    /// <param name="scene">The test scene.</param>
    /// <param name="generator">The generator of the terrain; the flat ground when null.</param>
    /// <param name="movement">Movement rules.</param>
    /// <param name="rescue">The rescue position; the generator of the planet when null.</param>
    /// <param name="devFlight">Whether dev flight is allowed.</param>
    /// <param name="constructsReady">Readiness of asynchronous construct collision.</param>
    /// <param name="morePlanets">Planets of the world besides the terrain planet (without terrain).</param>
    /// <returns>The rig.</returns>
    public static async Task<CharacterRig> CreateAsync(
        EngineTestScene scene, Func<Godot.VoxelGenerator>? generator = null, MovementRules? movement = null,
        Func<PlanetParams, Vector3, Vector3>? rescue = null, bool devFlight = false,
        Func<PlanetId, Vector3, bool>? constructsReady = null, PlanetParams[]? morePlanets = null)
    {
        var world = await TerrainTestWorld.CreateAsync(scene, generator: generator, morePlanets: morePlanets);
        var inputs = new FakeInputSource();
        var log = new RecordingLogger<ServerCharacters>();
        var characters = new ServerCharacters(world.Sim, inputs, world.Service, new GameRules(), movement ?? new MovementRules(), devFlight, log, rescue, constructsReady);
        scene.Add(characters);
        var stepper = scene.Add(new PhysicsStepper());
        var rig = new CharacterRig(scene, world, characters, inputs, log, stepper);
        stepper.OnTick = tick =>
        {
            rig.BeforeStep?.Invoke(tick);
            characters.Step(world.Sim.State.CurrentTick);
            rig.AfterStep?.Invoke(tick);
        };
        return rig;
    }

    /// <summary>Joins a new player standing at a pose and spawns its body.</summary>
    /// <param name="number">Distinguishes the players.</param>
    /// <param name="feet">Where the player stands.</param>
    /// <param name="rotation">Body rotation; identity when null.</param>
    /// <returns>The player.</returns>
    public PlayerId Join(ulong number, Vector3 feet, Quaternion? rotation = null)
    {
        var join = Sim.PreparePlayerJoin(new PlatformIdentity(IdentityPlatform.Dev, number), $"player{number}");
        Sim.ApplyServerOperation(join);
        Sim.State.Players.TryGet(join.Player, out var state);
        Sim.UpdatePlayerKinematics(join.Player, state!.Frame, state.ParentSeq, feet, rotation ?? SurfaceFrame.FromYaw(TerrainUp(feet), 0f), 0f);
        Characters.Spawn(join.Player);
        return join.Player;
    }

    /// <summary>The body node of a spawned player.</summary>
    /// <param name="player">The player.</param>
    /// <returns>The body.</returns>
    public PlayerBody BodyOf(PlayerId player) => Characters.GetNode<PlayerBody>($"Body_{player.Value}");

    /// <summary>Waits (real time, limit in seconds) until the physics frame counter reaches a value.</summary>
    /// <param name="ticks">The target number of frames since the rig started.</param>
    /// <param name="seconds">Real-time limit.</param>
    /// <returns>Whether the target was reached.</returns>
    public async Task<bool> WaitTicks(long ticks, double seconds = 120)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (_stepper.Ticks < ticks && watch.Elapsed.TotalSeconds < seconds)
        {
            await _scene.WaitFrames(1);
        }

        return _stepper.Ticks >= ticks;
    }

    /// <summary>Waits until a player's body left the frozen mode.</summary>
    /// <param name="player">The player.</param>
    /// <param name="seconds">Real-time limit.</param>
    /// <returns>Whether it did.</returns>
    public async Task<bool> WaitUnfrozenAsync(PlayerId player, double seconds = 60)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (watch.Elapsed.TotalSeconds < seconds)
        {
            if (Characters.TryGetState(player, out var state) && state.Mode != MovementMode.Frozen)
            {
                return true;
            }

            await _scene.WaitFrames(1);
        }

        return false;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _stepper.OnTick = null;
        Characters.DespawnAll();
        Characters.GetParent()?.RemoveChild(Characters);
        Characters.QueueFree();
        await World.DisposeAsync();
    }

    private static Vector3 TerrainUp(Vector3 feet) => Horyzont.Core.World.Terrain.TerrainMath.UpAt(feet);
}
