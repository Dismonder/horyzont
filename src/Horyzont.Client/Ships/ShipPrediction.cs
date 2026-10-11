using Horyzont.Core.Frames;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Ships;
using Horyzont.Core.Ships.Flight;

using Microsoft.Extensions.Logging;

namespace Horyzont.Client.Ships;

/// <summary>
/// Prediction of the ship of the pilot (K5-25 decisions 16 and 17): the same step chain as <c>ShipFlightSystem.StepShip</c>
/// (environment, <see cref="ShipFlightModel.Step"/>, contact), a ring of 128 inputs, reconciliation with <see cref="ShipCorrection"/>
/// by input slot and a render offset that hides corrections below <see cref="SnapDistance"/>. Allocation-free after construction.
/// </summary>
public sealed class ShipPrediction
{
    /// <summary>Ring size.</summary>
    public const int Capacity = 128;

    /// <summary>Corrections at or above this distance snap (and log the last inputs) instead of smoothing.</summary>
    public const double SnapDistance = 5.0;

    /// <summary>Seconds the render offset of a smaller correction takes to fade.</summary>
    public const float SmoothSeconds = 0.15f;

    private const int LoggedInputs = 10;

    private readonly Entry[] _ring = new Entry[Capacity];
    private readonly ShipEnvironmentSource _environment;
    private readonly ShipSweep _sweep;
    private readonly FlightRules _rules;
    private readonly float _dt;
    private readonly ILogger _logger;
    private ShipMotion _motion;
    private bool _hasState;
    private uint _newest;
    private ShipTerrainGate _gate;
    private ShipLaunchBlock _launchBlock;
    private double _horizontalCap = double.PositiveInfinity;
    private Vector3D _offset;
    private float _offsetLeft;

    /// <summary>Creates the prediction.</summary>
    /// <param name="environment">Environment of a motion without the server-only inputs (gate, launch block, cap).</param>
    /// <param name="sweep">Hull sweep of the client (the same collider class as the server).</param>
    /// <param name="rules">Flight rules.</param>
    /// <param name="tickRate">Ticks per second.</param>
    /// <param name="logger">Logger.</param>
    public ShipPrediction(ShipEnvironmentSource environment, ShipSweep sweep, FlightRules rules, int tickRate, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(sweep);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(tickRate, 0);
        _environment = environment;
        _sweep = sweep;
        _rules = rules;
        _dt = 1f / tickRate;
        _logger = logger;
    }

    /// <summary>Gets the predicted motion after the newest input.</summary>
    public ShipMotion Motion => _motion;

    /// <summary>Gets the reparent counter of the predicted ship.</summary>
    public uint ParentSeq { get; private set; }

    /// <summary>Gets the stats the prediction steps with.</summary>
    public ShipStats? Stats { get; private set; }

    /// <summary>Gets the number of corrections that changed the state.</summary>
    public int Corrections { get; private set; }

    /// <summary>Gets the number of corrections that snapped.</summary>
    public int Snaps { get; private set; }

    /// <summary>Gets the number of corrections dropped for an older or unknown frame.</summary>
    public int Dropped { get; private set; }

    /// <summary>Gets the largest position error of the last correction (metres).</summary>
    public double LastError { get; private set; }

    /// <summary>Starts predicting from a known state (boarding, a correction without history).</summary>
    /// <param name="motion">The state.</param>
    /// <param name="parentSeq">Reparent counter of the ship.</param>
    /// <param name="stats">Stats of the ship.</param>
    public void Reset(in ShipMotion motion, uint parentSeq, ShipStats stats)
    {
        ArgumentNullException.ThrowIfNull(stats);
        _motion = motion;
        ParentSeq = parentSeq;
        Stats = stats;
        _hasState = true;
        Array.Clear(_ring);
        _offset = default;
        _offsetLeft = 0f;
    }

    /// <summary>Steps the prediction with the input of slot <paramref name="seq"/>; the velocity responds in this very step.</summary>
    /// <param name="seq">Input slot (consecutive).</param>
    /// <param name="control">Control of the slot.</param>
    public void Step(uint seq, in ShipControl control)
    {
        if (!_hasState || Stats is null)
        {
            return;
        }

        StepOnce(control);
        ref var entry = ref _ring[seq % Capacity];
        entry = new Entry(seq, control, _motion, true);
        _newest = seq;
        // Float ticks do not sum exactly to SmoothSeconds: a remainder below half a tick ends the fade.
        _offsetLeft -= _dt;
        if (_offsetLeft < _dt * 0.5f)
        {
            _offsetLeft = 0f;
        }
    }

    /// <summary>
    /// Reconciles with the server state after slot <see cref="ShipCorrection.LastProcessedSeq"/>: beyond the tolerance of
    /// <see cref="ShipCorrectionPolicy"/> the state becomes the server one and the later inputs are stepped again.
    /// </summary>
    /// <param name="correction">The correction.</param>
    /// <returns>True when the predicted state changed.</returns>
    public bool Apply(ShipCorrection correction)
    {
        ArgumentNullException.ThrowIfNull(correction);
        if (!_hasState || Stats is null || correction.ParentSeq != ParentSeq)
        {
            Dropped++;
            return false;
        }

        _gate = correction.Gate;
        _launchBlock = correction.LaunchBlock;
        _horizontalCap = float.IsPositiveInfinity(correction.HorizontalSpeedCap) ? double.PositiveInfinity : correction.HorizontalSpeedCap;
        ref var entry = ref _ring[correction.LastProcessedSeq % Capacity];
        if (!entry.Used || entry.Seq != correction.LastProcessedSeq || correction.LastProcessedSeq > _newest)
        {
            Dropped++;
            return false;
        }

        var server = ToMotion(correction);
        LastError = Vector3D.Distance(entry.Motion.Pose.Position, server.Pose.Position);
        if (ShipCorrectionPolicy.Within(entry.Motion, server))
        {
            return false;
        }

        var shownBefore = _motion.Pose.Position + _offset * (_offsetLeft / SmoothSeconds);
        _motion = server;
        entry = new Entry(entry.Seq, entry.Control, server, true);
        for (var seq = correction.LastProcessedSeq + 1; seq <= _newest; seq++)
        {
            ref var next = ref _ring[seq % Capacity];
            if (!next.Used || next.Seq != seq)
            {
                break;
            }

            StepOnce(next.Control);
            next = new Entry(seq, next.Control, _motion, true);
        }

        Corrections++;
        var jump = shownBefore - _motion.Pose.Position;
        if (jump.Length() >= SnapDistance)
        {
            Snaps++;
            _offset = default;
            _offsetLeft = 0f;
            LogSnap(jump.Length());
        }
        else
        {
            _offset = jump;
            _offsetLeft = SmoothSeconds;
        }

        return true;
    }

    /// <summary>The pose to render: the prediction plus the fading offset of the last small correction.</summary>
    /// <returns>The render pose in the parent frame.</returns>
    public PoseD RenderPose() => new(_motion.Pose.Position + (_offset * (_offsetLeft / SmoothSeconds)), _motion.Pose.Rotation);

    private void StepOnce(in ShipControl control)
    {
        var environment = _environment(_motion, Stats!) with { Gate = _gate, LaunchBlock = _launchBlock, HorizontalSpeedCap = _horizontalCap };
        var from = _motion.Pose;
        ShipFlightModel.Step(ref _motion, control, Stats!, environment, _rules, _dt);
        if (from != _motion.Pose)
        {
            ShipContact.Resolve(ref _motion, from, _sweep(from, _motion.Pose), environment, _rules);
        }
    }

    private static ShipMotion ToMotion(ShipCorrection c) => new()
    {
        Pose = new PoseD(Vector3D.FromFloat(c.Position), QuaternionD.FromFloat(c.Rotation)),
        LinearVelocity = c.LinearVelocity,
        AngularVelocity = c.AngularVelocity,
        Fuel = c.Fuel,
        Flags = c.Flags,
    };

    private void LogSnap(double distance)
    {
        Span<sbyte> axes = stackalloc sbyte[LoggedInputs];
        var count = 0;
        for (var seq = _newest; count < LoggedInputs && seq > 0; seq--, count++)
        {
            ref var e = ref _ring[seq % Capacity];
            axes[count] = e.Used && e.Seq == seq ? e.Control.ThrustY : (sbyte)0;
        }

        ShipPredictionLog.Snapped(_logger, distance, _newest, string.Join(',', axes[..count].ToArray()));
    }

    private readonly record struct Entry(uint Seq, ShipControl Control, ShipMotion Motion, bool Used);
}
