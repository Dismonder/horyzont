using Godot;

namespace Horyzont.Game.Tests.Character.Fixtures;

/// <summary>
/// Calls a callback once per physics frame, so a test can move bodies inside a physics frame (<c>MoveAndSlide</c> uses the physics
/// delta only there). Physics ticks come from the engine clock, so tests wait on <see cref="Ticks"/> with a real-time limit.
/// </summary>
public sealed partial class PhysicsStepper : Node
{
    /// <summary>Gets or sets the callback, called with the number of the tick (from 1).</summary>
    public Action<long>? OnTick { get; set; }

    /// <summary>Gets the number of physics frames seen.</summary>
    public long Ticks { get; private set; }

    /// <inheritdoc />
    public override void _PhysicsProcess(double delta)
    {
        Ticks++;
        OnTick?.Invoke(Ticks);
    }
}
