using Horyzont.Client.Session;
using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.World.Markers;

namespace Horyzont.Client.Markers;

/// <summary>
/// The map markers a client may see (K5-27 decision 10): replaced by <see cref="MarkersState"/> and kept current by the marker
/// operations of <see cref="OperationApplied"/>. It has its own appliers: the server already checked limits, owners and ids, and
/// gaps in the operation numbers are legal (the server sends a client only the operations it may see). Main thread only.
/// </summary>
public sealed class ClientMarkerStore : IClientSessionListener
{
    private readonly SortedDictionary<ulong, MarkerRecord> _byId = [];

    /// <summary>Raised after the set of markers changed.</summary>
    public event Action? Changed;

    /// <summary>Gets the number of markers.</summary>
    public int Count => _byId.Count;

    /// <summary>Gets a counter that increases with every change (the HUD and maps redraw when it moves).</summary>
    public int Version { get; private set; }

    /// <summary>Gets the markers in id order.</summary>
    public SortedDictionary<ulong, MarkerRecord>.ValueCollection All => _byId.Values;

    /// <summary>Looks up a marker.</summary>
    /// <param name="id">Marker id.</param>
    /// <param name="marker">The marker when found.</param>
    /// <returns>True when the marker exists.</returns>
    public bool TryGet(MarkerId id, out MarkerRecord marker) => _byId.TryGetValue(id.Value, out marker!);

    /// <inheritdoc />
    public void OnMessage(Message message)
    {
        switch (message)
        {
            case MarkersState state:
                _byId.Clear();
                foreach (var marker in state.Markers)
                {
                    _byId[marker.Id.Value] = marker;
                }

                Touch();
                break;
            case OperationApplied { Operation: MarkerSetOp set }:
                _byId[set.Marker.Value] = set.ToRecord();
                Touch();
                break;
            case OperationApplied { Operation: MarkerRemoveOp remove }:
                if (_byId.Remove(remove.Marker.Value))
                {
                    Touch();
                }

                break;
        }
    }

    /// <inheritdoc />
    public void OnDisconnected(DisconnectCode code) => Clear();

    /// <summary>Forgets every marker (the session ended or a new hello arrived).</summary>
    public void Clear()
    {
        if (_byId.Count == 0)
        {
            return;
        }

        _byId.Clear();
        Touch();
    }

    /// <inheritdoc />
    public void OnServerHello(ServerHello hello) => Clear();

    private void Touch()
    {
        Version++;
        Changed?.Invoke();
    }
}
