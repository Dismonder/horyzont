using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Motion;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.World.Terrain;
using Horyzont.Game.Server.Characters;
using Horyzont.Server;

namespace Horyzont.Integration.Tests.Coop;

/// <summary>
/// The characters of the game server in miniature, for tests without an engine: a player walks on the sphere of its feet at the
/// walking or sprinting speed of the <see cref="MovementRules"/>, one step per input, and the position is written to the simulation
/// (so the validator measures reach from it) and sent back by the same <see cref="OwnStateSender"/> the game uses.
/// </summary>
internal sealed class CoopCharacters : IServerCharacterSource, IServerClientMessageHandler, IServerTickParticipant, IServerPlayerObserver
{
    private const float TickSeconds = 1f / 60f;

    private static readonly MovementRules _movement = new();

    private readonly Dictionary<PlayerId, Character> _characters = [];
    private ServerRuntime? _runtime;
    private OwnStateSender? _sender;

    /// <summary>Gets the number of players with a body.</summary>
    public int Count => _characters.Count;

    /// <summary>Binds the characters to a started runtime; call after <c>StartAsync</c> and before any client connects.</summary>
    /// <param name="runtime">The runtime.</param>
    public void Bind(ServerRuntime runtime)
    {
        _runtime = runtime;
        _sender = new OwnStateSender(runtime.Session, runtime.Simulation, this);
    }

    /// <inheritdoc />
    public bool TryGetState(PlayerId player, out ServerCharacterState state)
    {
        if (_characters.TryGetValue(player, out var c))
        {
            state = c.ToState();
            return true;
        }

        state = default;
        return false;
    }

    /// <inheritdoc />
    public bool Handle(PlayerId player, Message message) => false;

    /// <inheritdoc />
    public void OnTick(ServerRuntime runtime)
    {
        // Like the game: one buffered input per player and tick moves the body, then the state goes back to the player.
        var players = runtime.Session.PlayersInGame;
        for (var i = 0; i < players.Count; i++)
        {
            var player = players[i];
            if (!_characters.TryGetValue(player, out var c) && !TryCreate(player, out c))
            {
                continue;
            }

            if (runtime.Session.TryTakeInput(player, out var wire, out var seq))
            {
                c.LastSeq = seq;
                if (MotorInput.TryFrom(wire, _movement, out var motor))
                {
                    Step(c, motor);
                    runtime.Simulation.UpdatePlayerKinematics(player, c.Frame, c.ParentSeq, c.Feet, c.Rotation, c.Pitch);
                }
            }
        }

        _sender?.Send(runtime.State.CurrentTick);
    }

    /// <inheritdoc />
    public void OnPlayerReady(PlayerId player) => TryCreate(player, out _);

    /// <inheritdoc />
    public void OnPlayerLeaving(PlayerId player, DisconnectCode reason)
    {
        _characters.Remove(player);
        _sender?.Forget(player);
    }

    private static void Step(Character c, in MotorInput input)
    {
        var up = TerrainMath.UpAt(c.Feet);
        var rotation = SurfaceFrame.FromYaw(up, input.Yaw);
        var forward = Vector3.Transform(-Vector3.UnitZ, rotation);
        var right = Vector3.Cross(forward, up);
        var direction = (forward * input.MoveY) + (right * input.MoveX);
        if (direction.LengthSquared() > 1f)
        {
            direction = Vector3.Normalize(direction);
        }

        var speed = (input.Buttons & InputButtons.Sprint) != 0 ? _movement.SprintSpeed : _movement.WalkSpeed;
        var radius = c.Feet.Length();
        var moved = c.Feet + (direction * speed * TickSeconds);
        c.Feet = Vector3.Normalize(moved) * radius;
        c.Rotation = SurfaceFrame.FromYaw(TerrainMath.UpAt(c.Feet), input.Yaw);
        c.Pitch = input.Pitch;
        c.Mode = (input.Buttons & InputButtons.Sprint) != 0 ? MovementMode.Sprinting : MovementMode.Walking;
    }

    private bool TryCreate(PlayerId player, out Character character)
    {
        character = null!;
        if (_runtime is null || !_runtime.State.Players.TryGet(player, out var state))
        {
            return false;
        }

        character = new Character
        {
            Planet = new PlanetId(state.Frame.Id),
            Frame = state.Frame,
            ParentSeq = state.ParentSeq,
            Feet = state.Position,
            Rotation = state.BodyRotation,
            Pitch = state.Pitch,
        };
        _characters[player] = character;
        return true;
    }

    private sealed class Character
    {
        public PlanetId Planet { get; init; }

        public FrameRef Frame { get; init; }

        public uint ParentSeq { get; init; }

        public Vector3 Feet { get; set; }

        public Quaternion Rotation { get; set; } = Quaternion.Identity;

        public float Pitch { get; set; }

        public MovementMode Mode { get; set; } = MovementMode.Walking;

        public uint LastSeq { get; set; }

        public ServerCharacterState ToState() =>
            new(Planet, Frame, ParentSeq, Feet, Rotation, Pitch, Vector3.Zero, Mode, LastSeq);
    }
}
