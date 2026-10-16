using Horyzont.Core.Checkpoints;
using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Serialization;
using Horyzont.Core.World;
using Horyzont.Net.Session;

using Microsoft.Extensions.Logging;

namespace Horyzont.Server.Replication;

/// <summary>What <see cref="ConstructStateSender.RouteOperation"/> did with an operation for one recipient.</summary>
public enum ConstructOperationRoute
{
    /// <summary>The recipient has no state link for the construct.</summary>
    NotTracked,

    /// <summary>The operation was sent.</summary>
    Sent,

    /// <summary>The operation waits behind the running transfer.</summary>
    Buffered,

    /// <summary>The operation is already contained in (or will be contained in) the full state.</summary>
    Dropped,

    /// <summary>The link was restarted with a newer full state.</summary>
    Restarted,
}

/// <summary>
/// Sends complete construct states in parts and keeps the per-recipient order between a state and the operations that follow it
/// (K3-15, design points 5 and 6). A transfer copies the header and chunks from one world state when it starts, is planned and
/// sent in bounded steps, and never blocks the tick. Operations that arrive meanwhile wait (at most 256 per recipient) and follow
/// the last part; an overflow, a version gap or a staging overflow replaces the transfer with a newer full state.
/// </summary>
public sealed class ConstructStateSender
{
    /// <summary>Most buffered operations per recipient while its transfers run.</summary>
    public const int MaxBufferedOperations = 256;

    /// <summary>Soft target of the encoded size of one part.</summary>
    public const int SoftPartBytes = 32 * 1024;

    /// <summary>Bytes of copied chunk data one recipient may hold in running transfers (a single larger construct is still sent).</summary>
    public const int MaxStagedBytesPerClient = 8 * 1024 * 1024;

    /// <summary>Chunks measured per <see cref="Pump"/> call over all recipients, so planning a huge construct never stalls a tick.</summary>
    public const int PlanChunksPerPump = 64;

    /// <summary>Most parts one recipient receives per <see cref="Pump"/> call (the byte budget usually stops earlier).</summary>
    public const int MaxPartsPerPump = 16;

    private const int ChunkBytes = 4096 * sizeof(uint);

    private readonly ServerSession _session;
    private readonly WorldState _world;
    private readonly IReplicationSendBudget _budget;
    private readonly ILogger _logger;
    private readonly ByteWriter _writer = new(64 * 1024);
    private readonly Dictionary<PlayerId, Recipient> _recipients = [];
    private readonly List<PlayerId> _order = [];
    private long _sequence;

    /// <summary>Initializes a new instance of the <see cref="ConstructStateSender"/> class.</summary>
    /// <param name="session">Session the frames go through.</param>
    /// <param name="world">Authoritative world, read on the main thread only.</param>
    /// <param name="budget">Per-client reliable budget shared with other senders.</param>
    /// <param name="logger">Logger.</param>
    public ConstructStateSender(ServerSession session, WorldState world, IReplicationSendBudget budget, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(logger);
        _session = session;
        _world = world;
        _budget = budget;
        _logger = logger;
    }

    /// <summary>
    /// Raised when the link of a recipient to a construct becomes live: its complete state and the operations that were buffered behind it
    /// have been sent (K6-30 D15). Not raised when the transfer is replaced by a newer one.
    /// </summary>
    public event Action<PlayerId, EntityId>? LinkLive;

    /// <summary>Gets the number of completed full-state transfers (all recipients).</summary>
    public long CompletedTransfers { get; private set; }

    /// <summary>Gets the number of transfers that were replaced by a newer state.</summary>
    public long RestartedTransfers { get; private set; }

    /// <summary>
    /// Starts, or restarts with a newer state, the transfer of a construct to a recipient: a known link keeps its priority, a new one is
    /// a static construct.
    /// </summary>
    /// <param name="player">Recipient.</param>
    /// <param name="construct">Construct entity.</param>
    public void Enqueue(PlayerId player, EntityId construct)
    {
        if (_recipients.TryGetValue(player, out var recipient) && recipient.Links.TryGetValue(construct, out var link))
        {
            Enqueue(player, construct, link.Priority, link.Distance);
            return;
        }

        Enqueue(player, construct, ConstructTransferPriority.Static, 0f);
    }

    /// <summary>
    /// Starts, or restarts with a newer state, the transfer of a construct to a recipient (K5-26 decision 5). Transfers that have sent a
    /// part go on first (never preempted), then by <paramref name="priority"/>, ships by <paramref name="distance"/>, then by arrival.
    /// </summary>
    /// <param name="player">Recipient.</param>
    /// <param name="construct">Construct entity.</param>
    /// <param name="priority">Priority class.</param>
    /// <param name="distance">Distance from the recipient in the region (ships); infinity when unknown.</param>
    public void Enqueue(PlayerId player, EntityId construct, ConstructTransferPriority priority, float distance)
    {
        var recipient = Get(player);
        if (!recipient.Links.TryGetValue(construct, out var link))
        {
            link = new Link(construct);
            recipient.Links.Add(construct, link);
            recipient.Order.Add(link);
        }
        else
        {
            if (link.State != LinkState.Live)
            {
                RestartedTransfers++;
            }

            Release(recipient, link);
        }

        link.State = LinkState.Pending;
        link.Priority = priority;
        link.Distance = float.IsNaN(distance) ? float.PositiveInfinity : distance;
        link.Sequence = ++_sequence;
        recipient.Unsorted = true;
    }

    /// <summary>Forgets the link of a construct: a running transfer is cancelled and its memory released.</summary>
    /// <param name="player">Recipient.</param>
    /// <param name="construct">Construct entity.</param>
    public void Cancel(PlayerId player, EntityId construct)
    {
        if (_recipients.TryGetValue(player, out var recipient) && recipient.Links.Remove(construct, out var link))
        {
            Release(recipient, link);
            recipient.Order.Remove(link);
        }
    }

    /// <summary>Forgets a recipient who left.</summary>
    /// <param name="player">Recipient.</param>
    public void ForgetPlayer(PlayerId player)
    {
        if (_recipients.Remove(player))
        {
            _order.Remove(player);
        }
    }

    /// <summary>Releases everything (shutdown).</summary>
    public void Clear()
    {
        _recipients.Clear();
        _order.Clear();
    }

    /// <summary>Whether a transfer to the recipient has not completed yet.</summary>
    /// <param name="player">Recipient.</param>
    /// <param name="construct">Construct entity.</param>
    /// <returns>True while the link is pending, planned or sending.</returns>
    public bool IsTransferring(PlayerId player, EntityId construct) =>
        _recipients.TryGetValue(player, out var recipient) && recipient.Links.TryGetValue(construct, out var link) && link.State != LinkState.Live;

    /// <summary>Whether the recipient holds a complete, up-to-date copy that follows operations.</summary>
    /// <param name="player">Recipient.</param>
    /// <param name="construct">Construct entity.</param>
    /// <returns>True for a live link.</returns>
    public bool IsLive(PlayerId player, EntityId construct) =>
        _recipients.TryGetValue(player, out var recipient) && recipient.Links.TryGetValue(construct, out var link) && link.State == LinkState.Live;

    /// <summary>Gets the bytes of chunk data a recipient currently holds in running transfers.</summary>
    /// <param name="player">Recipient.</param>
    /// <returns>Staged bytes.</returns>
    public int StagedBytes(PlayerId player) => _recipients.TryGetValue(player, out var recipient) ? recipient.Staged : 0;

    /// <summary>Gets the number of operations a recipient has buffered behind running transfers.</summary>
    /// <param name="player">Recipient.</param>
    /// <returns>Buffered operations.</returns>
    public int BufferedOperations(PlayerId player) => _recipients.TryGetValue(player, out var recipient) ? recipient.Buffered : 0;

    /// <summary>Sends a live (small, reliable) message and charges the shared budget; Live traffic is never refused.</summary>
    /// <param name="player">Recipient.</param>
    /// <param name="message">Message.</param>
    public void SendLive(PlayerId player, Message message)
    {
        ArgumentNullException.ThrowIfNull(message);
        _writer.Reset();
        MessageCodec.Write(_writer, message);
        _budget.TryConsume(player, ReplicationTraffic.Live, _writer.Length);
        _session.SendEncoded(player, message.Type, _writer.WrittenSpan);
    }

    /// <summary>Sends a live message to every recipient whose link to the construct is live (K6-30 D15).</summary>
    /// <param name="construct">Construct entity.</param>
    /// <param name="message">Message.</param>
    /// <returns>Number of recipients.</returns>
    public int SendLiveToAll(EntityId construct, Message message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var sent = 0;
        for (var i = 0; i < _order.Count; i++)
        {
            var player = _order[i];
            if (IsLive(player, construct))
            {
                SendLive(player, message);
                sent++;
            }
        }

        return sent;
    }

    /// <summary>Delivers one visible effect of an operation to a recipient, keeping the order after its full state.</summary>
    /// <param name="player">Recipient.</param>
    /// <param name="message">The operation as sent on the wire.</param>
    /// <param name="change">Resulting version of the affected construct.</param>
    /// <returns>What happened.</returns>
    public ConstructOperationRoute RouteOperation(PlayerId player, OperationApplied message, ConstructChange change)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (!_recipients.TryGetValue(player, out var recipient) || !recipient.Links.TryGetValue(change.Construct, out var link))
        {
            return ConstructOperationRoute.NotTracked;
        }

        switch (link.State)
        {
            case LinkState.Pending:
                return ConstructOperationRoute.Dropped;
            case LinkState.Live:
                if (IsNewer(link.SentVersion, change.Version))
                {
                    return ConstructOperationRoute.Dropped;
                }

                if (change.Version != unchecked(link.SentVersion + 1))
                {
                    Enqueue(player, change.Construct);
                    return ConstructOperationRoute.Restarted;
                }

                link.SentVersion = change.Version;
                SendLive(player, message);
                return ConstructOperationRoute.Sent;
            default:
                if (!IsNewer(change.Version, link.Header!.Version))
                {
                    return ConstructOperationRoute.Dropped;
                }

                if (recipient.Buffered >= MaxBufferedOperations)
                {
                    Enqueue(player, change.Construct);
                    return ConstructOperationRoute.Restarted;
                }

                link.Buffer.Add(message);
                recipient.Buffered++;
                return ConstructOperationRoute.Buffered;
        }
    }

    /// <summary>Captures, plans and sends as much as the budget allows; never more than a bounded step.</summary>
    public void Pump()
    {
        var plan = PlanChunksPerPump;
        for (var p = 0; p < _order.Count; p++)
        {
            var player = _order[p];
            if (!_recipients.TryGetValue(player, out var recipient))
            {
                continue;
            }

            Advance(player, recipient, ref plan);
        }
    }

    private static bool IsNewer(uint value, uint previous) => unchecked((int)(value - previous)) > 0;

    private Recipient Get(PlayerId player)
    {
        if (!_recipients.TryGetValue(player, out var recipient))
        {
            recipient = new Recipient();
            _recipients.Add(player, recipient);
            _order.Add(player);
        }

        return recipient;
    }

    private void Advance(PlayerId player, Recipient recipient, ref int plan)
    {
        if (recipient.Unsorted)
        {
            // Only after an enqueue or a first part: never in the steady state of live copies.
            recipient.Order.Sort(LinkOrder.Instance);
            recipient.Unsorted = false;
        }

        // Pending -> capture -> plan: bounded per call.
        for (var i = 0; i < recipient.Order.Count; i++)
        {
            var link = recipient.Order[i];
            if (link.State == LinkState.Pending && !TryCapture(recipient, link))
            {
                if (link.State == LinkState.Removed)
                {
                    recipient.Links.Remove(link.Id);
                    recipient.Order.RemoveAt(i--);
                }

                continue;
            }

            if (link.State == LinkState.Planning)
            {
                PlanSome(link, ref plan);
            }
        }

        // Round-robin one part per link and pass, until the byte budget refuses or the per-call limit is reached.
        var parts = 0;
        var progressed = true;
        while (progressed && parts < MaxPartsPerPump)
        {
            progressed = false;
            for (var i = 0; i < recipient.Order.Count && parts < MaxPartsPerPump; i++)
            {
                var link = recipient.Order[i];
                if (link.State != LinkState.Sending)
                {
                    continue;
                }

                var result = SendPart(player, recipient, link);
                if (result == PartResult.Refused)
                {
                    return;
                }

                parts++;
                progressed = true;
            }
        }
    }

    private bool TryCapture(Recipient recipient, Link link)
    {
        if (!_world.Constructs.TryGet(link.Id, out var construct))
        {
            link.State = LinkState.Removed;
            return false;
        }

        var need = construct.ChunkCount * ChunkBytes;
        if (recipient.Staged != 0 && recipient.Staged + need > MaxStagedBytesPerClient)
        {
            return false;
        }

        var chunks = new List<ConstructChunkRecord>(construct.ChunkCount);
        foreach (var pair in construct.Chunks)
        {
            chunks.Add(new ConstructChunkRecord(new ConstructChunkKey(link.Id, pair.Key), pair.Value.Blocks.ToArray()));
        }

        link.Header = ConstructHeaderRecord.From(construct);
        link.Chunks = chunks.ToArray();
        link.Staged = chunks.Count * ChunkBytes;
        recipient.Staged += link.Staged;
        link.Planned = 0;
        link.PartEnds.Clear();
        link.GroupBytes = 0;
        link.GroupCount = 0;
        link.PartIndex = 0;
        link.Frame = null;
        link.State = LinkState.Planning;
        if (chunks.Count == 0)
        {
            link.PartEnds.Add(0);
            link.State = LinkState.Sending;
        }

        return true;
    }

    private void PlanSome(Link link, ref int plan)
    {
        var chunks = link.Chunks!;
        var header = link.Header!;
        var baseline = Measure(header, []);
        while (link.Planned < chunks.Length && plan > 0)
        {
            var chunk = chunks[link.Planned];
            var size = Measure(header, [chunk]) - baseline;
            if (link.GroupCount > 0 && (link.GroupCount == ProtocolConstants.MaxChunksPerConstructStatePart || link.GroupBytes + size > SoftPartBytes))
            {
                link.PartEnds.Add(link.Planned);
                link.GroupCount = 0;
                link.GroupBytes = 0;
            }

            link.GroupCount++;
            link.GroupBytes += size;
            link.Planned++;
            plan--;
        }

        if (link.Planned == chunks.Length)
        {
            link.PartEnds.Add(chunks.Length);
            link.State = LinkState.Sending;
        }
    }

    private int Measure(ConstructHeaderRecord header, ConstructChunkRecord[] chunks)
    {
        _writer.Reset();
        MessageCodec.Write(_writer, new ConstructState(header, 0, 1, chunks));
        return _writer.Length;
    }

    private PartResult SendPart(PlayerId player, Recipient recipient, Link link)
    {
        if (link.Frame is null)
        {
            var start = link.PartIndex == 0 ? 0 : link.PartEnds[link.PartIndex - 1];
            var end = link.PartEnds[link.PartIndex];
            var slice = new ArraySegment<ConstructChunkRecord>(link.Chunks!, start, end - start);
            _writer.Reset();
            MessageCodec.Write(_writer, new ConstructState(link.Header!, (ushort)link.PartIndex, (ushort)link.PartEnds.Count, slice));
            link.Frame = _writer.WrittenSpan.ToArray();
        }

        if (!_budget.TryConsume(player, ReplicationTraffic.Bulk, link.Frame.Length))
        {
            return PartResult.Refused;
        }

        _session.SendEncoded(player, MessageType.ConstructState, link.Frame);
        link.Frame = null;
        link.PartIndex++;
        if (link.PartIndex == 1)
        {
            // Started: it now goes before every transfer that has not sent anything.
            recipient.Unsorted = true;
        }

        if (link.PartIndex < link.PartEnds.Count)
        {
            return PartResult.Sent;
        }

        Complete(player, recipient, link);
        return PartResult.Completed;
    }

    private void Complete(PlayerId player, Recipient recipient, Link link)
    {
        CompletedTransfers++;
        var id = link.Id;
        link.SentVersion = link.Header!.Version;
        var buffered = link.Buffer;
        recipient.Staged -= link.Staged;
        link.Staged = 0;
        link.Chunks = null;
        link.Header = null;
        link.State = LinkState.Live;
        recipient.Buffered -= buffered.Count;
        for (var i = 0; i < buffered.Count; i++)
        {
            var message = buffered[i];
            for (var c = 0; c < message.ConstructChanges.Count; c++)
            {
                var change = message.ConstructChanges[c];
                if (change.Construct != id)
                {
                    continue;
                }

                if (IsNewer(link.SentVersion, change.Version) || change.Version == link.SentVersion)
                {
                    break;
                }

                if (change.Version != unchecked(link.SentVersion + 1))
                {
                    _logger.BufferedOperationGap(player.Value, id.Value, change.Version, link.SentVersion);
                    buffered.Clear();
                    Enqueue(player, id);
                    return;
                }

                link.SentVersion = change.Version;
                SendLive(player, message);
                break;
            }

        }

        buffered.Clear();
        LinkLive?.Invoke(player, id);
    }

    private static void Release(Recipient recipient, Link link)
    {
        recipient.Staged -= link.Staged;
        recipient.Buffered -= link.Buffer.Count;
        link.Staged = 0;
        link.Buffer.Clear();
        link.Chunks = null;
        link.Header = null;
        link.Frame = null;
        link.PartEnds.Clear();
        link.Planned = 0;
        link.PartIndex = 0;
    }

    private enum LinkState
    {
        Pending,
        Planning,
        Sending,
        Live,
        Removed,
    }

    private enum PartResult
    {
        Sent,
        Completed,
        Refused,
    }

    private sealed class Recipient
    {
        public Dictionary<EntityId, Link> Links { get; } = [];

        public List<Link> Order { get; } = [];

        public int Staged { get; set; }

        public int Buffered { get; set; }

        public bool Unsorted { get; set; }
    }

    /// <summary>Started transfers first, then priority, distance and arrival; live and finished links keep their relative order.</summary>
    private sealed class LinkOrder : IComparer<Link>
    {
        public static readonly LinkOrder Instance = new();

        public int Compare(Link? x, Link? y)
        {
            ArgumentNullException.ThrowIfNull(x);
            ArgumentNullException.ThrowIfNull(y);
            var order = IsStarted(y).CompareTo(IsStarted(x));
            if (order == 0)
            {
                order = x.Priority.CompareTo(y.Priority);
            }

            if (order == 0)
            {
                order = x.Distance.CompareTo(y.Distance);
            }

            return order != 0 ? order : x.Sequence.CompareTo(y.Sequence);
        }

        private static bool IsStarted(Link link) => link.State == LinkState.Sending && link.PartIndex > 0;
    }

    private sealed class Link(EntityId id)
    {
        public EntityId Id { get; } = id;

        public LinkState State { get; set; }

        public uint SentVersion { get; set; }

        public ConstructHeaderRecord? Header { get; set; }

        public ConstructChunkRecord[]? Chunks { get; set; }

        public int Staged { get; set; }

        public int Planned { get; set; }

        public int GroupBytes { get; set; }

        public int GroupCount { get; set; }

        public List<int> PartEnds { get; } = [];

        public int PartIndex { get; set; }

        public byte[]? Frame { get; set; }

        public List<OperationApplied> Buffer { get; } = [];

        public ConstructTransferPriority Priority { get; set; } = ConstructTransferPriority.Static;

        public float Distance { get; set; }

        public long Sequence { get; set; }
    }
}
