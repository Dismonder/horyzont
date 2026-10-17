using Godot;

using Horyzont.Core.Motion;
using Horyzont.Game.Character;

namespace Horyzont.Game.Client.Character;

/// <summary>
/// The character model of a body on a rendering client: the space suit "Explorer" (<c>tools/art/explorer_character.py</c>) under
/// <see cref="PlayerBody.Visual"/>. It only reads the body (mode, velocity, up) and never feeds anything back, so the server and the
/// prediction do not depend on it. The model turns towards the horizontal movement (the body itself keeps the camera yaw), plays
/// the animation that matches the movement mode and speed, and lights the jetpack exhaust (<c>JetFlame</c>) while flying or taking
/// off. The multitool (<c>Multitool</c>) stays in the right hand.
/// </summary>
public sealed partial class CharacterVisual : Node3D
{
    /// <summary>Imported model with the skeleton and the animations.</summary>
    public const string ModelPath = "res://assets/characters/explorer.glb";

    /// <summary>Horizontal speed (m/s) below which the character stands.</summary>
    public const float MovingSpeed = 0.35f;

    private const double BlendSeconds = 0.2;
    private const double TurnRate = 12.0;
    private const double AirborneDelaySeconds = 0.15;
    private const double WalkToRunSpeed = 2.8;
    private const double RunToSprintSpeed = 6.2;
    private const double WalkNaturalSpeed = 1.7;
    private const double RunNaturalSpeed = 4.5;
    private const double SprintNaturalSpeed = 7.5;

    private static readonly StringName _idleAnimation = "Idle";
    private static readonly StringName _walkAnimation = "Walk";
    private static readonly StringName _runAnimation = "Run";
    private static readonly StringName _sprintAnimation = "Sprint";
    private static readonly StringName _jumpAnimation = "Jump";
    private static readonly StringName _fallAnimation = "Fall";
    private static readonly StringName _flyAnimation = "Fly";

    private PlayerBody? _body;
    private AnimationPlayer? _player;
    private Node3D? _flame;
    private StringName? _current;
    private Vector3 _facing;
    private double _airborne;

    /// <summary>Gets the animation that plays now (null before the first frame).</summary>
    public StringName? CurrentAnimation => _current;

    /// <summary>Creates the model under the visual node of a body. Main thread, the body already placed.</summary>
    /// <param name="body">The body to follow.</param>
    /// <returns>The model node.</returns>
    public static CharacterVisual Attach(PlayerBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var holder = body.Visual ?? throw new InvalidOperationException("The body has no Visual node.");
        var scene = GD.Load<PackedScene>(ModelPath) ?? throw new InvalidOperationException($"Model {ModelPath} not found.");
        var visual = new CharacterVisual { Name = "Explorer", _body = body };
        visual.AddChild(scene.Instantiate<Node3D>());
        holder.AddChild(visual);
        return visual;
    }

    /// <summary>Chooses the animation and its speed from the movement; pure, so it is testable without a scene.</summary>
    /// <param name="mode">Movement mode of the body.</param>
    /// <param name="horizontalSpeed">Speed along the ground (m/s).</param>
    /// <param name="verticalSpeed">Speed along "up" (m/s).</param>
    /// <param name="airborneSeconds">How long the body has been off the floor.</param>
    /// <returns>Animation name and playback speed.</returns>
    public static (StringName Animation, float Speed) Choose(MovementMode mode, double horizontalSpeed, double verticalSpeed, double airborneSeconds)
    {
        switch (mode)
        {
            case MovementMode.DevFlying:
                return (_flyAnimation, 1f);
            case MovementMode.Jumping:
                return (_jumpAnimation, 1f);
            case MovementMode.Falling when airborneSeconds >= AirborneDelaySeconds:
                return (verticalSpeed > 1.0 ? _jumpAnimation : _fallAnimation, 1f);
            case MovementMode.Frozen:
                return (_idleAnimation, 1f);
        }

        if (horizontalSpeed < MovingSpeed)
        {
            return (_idleAnimation, 1f);
        }

        if (horizontalSpeed < WalkToRunSpeed)
        {
            return (_walkAnimation, PlaybackSpeed(horizontalSpeed / WalkNaturalSpeed));
        }

        return horizontalSpeed < RunToSprintSpeed
            ? (_runAnimation, PlaybackSpeed(horizontalSpeed / RunNaturalSpeed))
            : (_sprintAnimation, PlaybackSpeed(horizontalSpeed / SprintNaturalSpeed));
    }

    /// <inheritdoc/>
    public override void _Ready()
    {
        _player = FindChild("AnimationPlayer", recursive: true, owned: false) as AnimationPlayer;
        if (_player is not null)
        {
            foreach (var name in new[] { _idleAnimation, _walkAnimation, _runAnimation, _sprintAnimation, _fallAnimation, _flyAnimation })
            {
                if (_player.HasAnimation(name))
                {
                    _player.GetAnimation(name).LoopMode = Animation.LoopModeEnum.Linear;
                }
            }
        }

        _flame = FindChild("JetFlame", recursive: true, owned: false) as Node3D;
        if (_flame is not null)
        {
            _flame.Visible = false;
        }

        _facing = _body is null ? Vector3.Forward : -_body.GlobalBasis.Z;
    }

    /// <summary>Whether the jetpack exhaust burns for a movement mode.</summary>
    /// <param name="mode">Movement mode of the body.</param>
    /// <param name="verticalSpeed">Speed along "up" (m/s).</param>
    /// <returns>True while flying and while rising after a jump.</returns>
    public static bool JetBurns(MovementMode mode, double verticalSpeed) =>
        mode == MovementMode.DevFlying || (mode == MovementMode.Jumping && verticalSpeed > 0.5);

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        if (_body is null || !IsVisibleInTree())
        {
            return;
        }

        var up = _body.GlobalBasis.Y.Normalized();
        var velocity = _body.Velocity;
        var vertical = velocity.Dot(up);
        var horizontal = velocity - (up * vertical);
        var speed = horizontal.Length();
        var mode = _body.Mode;
        _airborne = mode is MovementMode.Walking or MovementMode.Sprinting || _body.OnFloor ? 0 : _airborne + delta;

        Face(up, mode == MovementMode.DevFlying || speed < MovingSpeed ? _facing : horizontal / speed, delta);

        if (_player is null)
        {
            return;
        }

        UpdateFlame(JetBurns(mode, vertical));
        var (animation, playback) = Choose(mode, speed, vertical, _airborne);
        if (animation != _current)
        {
            _player.Play(animation, BlendSeconds);
            _current = animation;
        }

        _player.SpeedScale = playback;
    }

    private void UpdateFlame(bool burning)
    {
        if (_flame is null)
        {
            return;
        }

        _flame.Visible = burning;
    }

    private static float PlaybackSpeed(double ratio) => (float)Math.Clamp(ratio, 0.6, 1.8);

    private void Face(Vector3 up, Vector3 target, double delta)
    {
        if (_body!.Mode == MovementMode.DevFlying)
        {
            target = -_body.GlobalBasis.Z;
        }

        // Keep both directions in the tangent plane of the current "up" (it turns as the body walks around the planet).
        var facing = _facing - (up * _facing.Dot(up));
        target -= up * target.Dot(up);
        if (facing.LengthSquared() < 1e-6)
        {
            facing = -_body.GlobalBasis.Z;
        }

        if (target.LengthSquared() < 1e-6)
        {
            target = facing;
        }

        facing = facing.Normalized();
        target = target.Normalized();
        var angle = facing.SignedAngleTo(target, up);
        _facing = facing.Rotated(up, angle * (1.0 - Math.Exp(-TurnRate * delta))).Normalized();
        GlobalBasis = Basis.LookingAt(_facing, up);
    }
}
