using Godot;

using Horyzont.Client.Frames;
using Horyzont.Client.Movement;
using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Game.Frames;

namespace Horyzont.Game.Client.Character;

/// <summary>Remote player visuals only. K3-15 owns creation, removal and ring registration.</summary>
public sealed partial class RemotePlayerPresentation : Node3D
{
    private RemoteMotionBuffer? _buffer;
    private RemoteMotionPolicy? _clock;
    private ClientFrames? _frames;

    /// <summary>Creates a placeholder mesh without any collision or movement simulation.</summary>
    public RemotePlayerPresentation()
    {
        PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
        AddChild(new MeshInstance3D
        {
            Name = "Visual",
            Mesh = new CapsuleMesh { Radius = 0.35f, Height = 1.8f },
            Position = new Vector3(0, 0.9, 0),
        });
    }

    /// <summary>Attaches the lifecycle owner's ring and the shared session clock.</summary>
    /// <param name="buffer">Ring registered in the full snapshot adapter.</param>
    /// <param name="clock">Single session interpolation clock.</param>
    /// <param name="frames">Client frames; with them a player in a construct frame is shown at frame ∘ local pose (K5-20).</param>
    public void Initialize(RemoteMotionBuffer buffer, RemoteMotionPolicy clock, ClientFrames? frames = null)
    {
        _frames = frames;
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(clock);
        _buffer = buffer;
        _clock = clock;
    }

    /// <summary>Places the visual pose; K3 currently renders only planet-frame players.</summary>
    /// <param name="sample">Interpolated full sample.</param>
    public void SetSample(in RemoteMotionSample sample) => SetSample(sample, sample.ServerTick.Value);

    /// <summary>Places the visual pose at a render tick: planet frames directly, construct frames composed with the frame at that tick.</summary>
    /// <param name="sample">Interpolated full sample.</param>
    /// <param name="renderTick">Server tick being rendered.</param>
    public void SetSample(in RemoteMotionSample sample, double renderTick)
    {
        if (sample.Frame.Kind == FrameKind.Construct && _frames?.Graph is { } graph
            && graph.TryGetWorldPose(sample.Frame, renderTick, out var frame))
        {
            // The Godot world is the system frame (K5-20, decision 1).
            Visible = true;
            GlobalTransform = GodotFrames.ToGodot(FrameMath.Compose(frame, FrameMath.FromFloat(sample.Position, sample.Rotation)));
            return;
        }

        if (sample.Frame.Kind != FrameKind.Planet)
        {
            Visible = false;
            return;
        }

        Visible = true;
        var planet = new PlanetId(sample.Frame.Id);
        GlobalTransform = new Transform3D(PlanetFrames.ToGodotBasis(planet, sample.Rotation), PlanetFrames.ToGodot(planet, sample.Position));
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (_buffer is not null && _clock is not null)
        {
            var tick = _clock.RenderTick;
            if (_buffer.TrySample(tick, out var sample))
            {
                SetSample(sample, tick);
            }
        }
    }
}
