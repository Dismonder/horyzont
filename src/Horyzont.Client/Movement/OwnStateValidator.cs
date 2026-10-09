using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Motion;
using Horyzont.Core.Protocol.Messages;

using Microsoft.Extensions.Logging;

namespace Horyzont.Client.Movement;

/// <summary>
/// Checks the own-character state in a snapshot as hostile data before it reaches prediction: wrong entity or frame, non-finite values,
/// a rotation off the unit length, an older snapshot, an acknowledgement of an input never sent. A rejected snapshot is logged,
/// but at most for the first rejection and then every <see cref="LogEvery"/>-th one, so a broken server cannot flood the log.
/// Allocation-free after construction.
/// </summary>
public sealed class OwnStateValidator
{
    /// <summary>Rejections between two log entries (the first one is always logged).</summary>
    public const int LogEvery = 32;

    private const float RotationTolerance = 1e-3f;
    private const EntityStateFields RequiredFields =
        EntityStateFields.Position | EntityStateFields.Rotation | EntityStateFields.Velocity | EntityStateFields.MotionFlags;

    private readonly EntityId _ownEntity;
    private readonly PlanetId[] _planets;
    private readonly ILogger _logger;
    private bool _hasAccepted;
    private uint _lastSnapshotId;
    private Tick _lastTick;
    private uint _lastAck;
    private uint _lastSent;
    private int _rejected;
    private int _unknownFlags;

    /// <summary>Initializes a new instance of the <see cref="OwnStateValidator"/> class.</summary>
    /// <param name="ownEntity">Body entity of the local player (<c>ServerHello.YourEntity</c>).</param>
    /// <param name="planets">Planets announced in the server hello.</param>
    /// <param name="logger">Logger.</param>
    public OwnStateValidator(EntityId ownEntity, IEnumerable<PlanetId> planets, ILogger<OwnStateValidator> logger)
    {
        ArgumentNullException.ThrowIfNull(planets);
        ArgumentNullException.ThrowIfNull(logger);
        _ownEntity = ownEntity;
        _planets = [.. planets];
        _logger = logger;
    }

    /// <summary>Gets the number of snapshots rejected so far.</summary>
    public int RejectedCount => _rejected;

    /// <summary>Creates a validator from the server hello.</summary>
    /// <param name="hello">The hello.</param>
    /// <param name="logger">Logger.</param>
    /// <returns>The validator.</returns>
    public static OwnStateValidator FromHello(ServerHello hello, ILogger<OwnStateValidator> logger)
    {
        ArgumentNullException.ThrowIfNull(hello);
        return new OwnStateValidator(hello.YourEntity, hello.Planets.Select(p => p.Id), logger);
    }

    /// <summary>Records that the client has sent inputs up to <paramref name="seq"/>; acknowledgements beyond it are rejected.</summary>
    /// <param name="seq">Number of the latest sent input.</param>
    public void NoteSent(uint seq) => _lastSent = Math.Max(_lastSent, seq);

    /// <summary>Forgets the history: a new session.</summary>
    public void Reset()
    {
        _hasAccepted = false;
        _lastSnapshotId = 0;
        _lastTick = default;
        _lastAck = 0;
        _lastSent = 0;
        _unknownFlags = 0;
    }

    /// <summary>Validates the own-character state in a snapshot.</summary>
    /// <param name="snapshot">The snapshot.</param>
    /// <param name="state">The state when the verdict is <see cref="OwnStateVerdict.Accepted"/>.</param>
    /// <returns>The verdict.</returns>
    public OwnStateVerdict Validate(Snapshot snapshot, out OwnState state)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        state = default;
        var verdict = Check(snapshot, out var entity, out var planet);
        if (verdict != OwnStateVerdict.Accepted)
        {
            if (_rejected++ % LogEvery == 0)
            {
                _logger.SnapshotRejected(snapshot.SnapshotId, verdict, _rejected);
            }

            return verdict;
        }

        var mode = entity.MotionFlags <= (byte)MovementMode.DevFlying ? (MovementMode)entity.MotionFlags : MovementMode.Falling;
        if (entity.MotionFlags > (byte)MovementMode.DevFlying)
        {
            if (_unknownFlags++ % LogEvery == 0)
            {
                _logger.UnknownMotionFlags(entity.MotionFlags, snapshot.SnapshotId);
            }
        }

        _hasAccepted = true;
        _lastSnapshotId = snapshot.SnapshotId;
        _lastTick = snapshot.ServerTick;
        _lastAck = snapshot.LastProcessedInputSeq;
        state = new OwnState(
            planet, entity.ParentSeq, entity.Position, entity.Rotation, entity.Velocity, mode, snapshot.LastProcessedInputSeq,
            snapshot.SnapshotId, snapshot.ServerTick);
        return OwnStateVerdict.Accepted;
    }

    private static bool IsFinite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    private OwnStateVerdict Check(Snapshot snapshot, out EntityState entity, out PlanetId planet)
    {
        planet = default;
        entity = default;
        var found = false;
        for (var i = 0; i < snapshot.Entities.Count; i++)
        {
            if (snapshot.Entities[i].Entity == _ownEntity)
            {
                entity = snapshot.Entities[i];
                found = true;
                break;
            }
        }

        if (!found)
        {
            return OwnStateVerdict.MissingOwnEntity;
        }

        if (snapshot.BaselineSnapshotId != 0 || (entity.Fields & RequiredFields) != RequiredFields)
        {
            return OwnStateVerdict.MissingFields;
        }

        if (entity.FrameIndex >= snapshot.Frames.Count)
        {
            return OwnStateVerdict.FrameIndexOutOfRange;
        }

        var frame = snapshot.Frames[entity.FrameIndex].Frame;
        if (frame.Kind != FrameKind.Planet || Array.IndexOf(_planets, new PlanetId(frame.Id)) < 0)
        {
            return OwnStateVerdict.UnknownPlanet;
        }

        planet = new PlanetId(frame.Id);
        var rotation = entity.Rotation;
        var length = rotation.Length();
        if (!IsFinite(entity.Position) || !IsFinite(entity.Velocity) || !float.IsFinite(length))
        {
            return OwnStateVerdict.NonFinite;
        }

        if (MathF.Abs(length - 1f) > RotationTolerance)
        {
            return OwnStateVerdict.BadRotation;
        }

        if (_hasAccepted && (snapshot.SnapshotId <= _lastSnapshotId || snapshot.ServerTick < _lastTick))
        {
            return OwnStateVerdict.Stale;
        }

        if (snapshot.LastProcessedInputSeq > _lastSent)
        {
            return OwnStateVerdict.AckBeyondSent;
        }

        return _hasAccepted && snapshot.LastProcessedInputSeq < _lastAck ? OwnStateVerdict.AckRegressed : OwnStateVerdict.Accepted;
    }
}
