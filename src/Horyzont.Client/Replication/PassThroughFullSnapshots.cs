using Horyzont.Client.Session;
using Horyzont.Core.Protocol.Messages;

namespace Horyzont.Client.Replication;

/// <summary>Neutral K2 adapter forwarding only full wire snapshots to registered sinks.</summary>
public sealed class PassThroughFullSnapshots : IClientSessionListener
{
    private readonly List<IFullSnapshotSink> _sinks = [];

    /// <summary>Registers a sink before polling the session.</summary>
    /// <param name="sink">Full snapshot consumer.</param>
    public void AddSink(IFullSnapshotSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        if (!_sinks.Contains(sink))
        {
            _sinks.Add(sink);
        }
    }

    /// <inheritdoc />
    public void OnMessage(Message message)
    {
        if (message is Snapshot { BaselineSnapshotId: 0 } full)
        {
            foreach (var sink in _sinks)
            {
                sink.OnFullSnapshot(full);
            }
        }
    }
}
