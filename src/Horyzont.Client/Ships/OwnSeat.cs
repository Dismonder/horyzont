using Horyzont.Client.Session;
using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Client.Ships;

/// <summary>The own seat from the own reparent operations (stand-in for the seat in <c>KindData</c> of K5-26).</summary>
public sealed class OwnSeat : IClientSessionListener, IOwnSeatSource
{
    private EntityId _entity;
    private EntityId _construct;
    private CellPos? _cell;

    /// <inheritdoc />
    public bool TryGetOwnSeat(out EntityId construct, out CellPos cell)
    {
        construct = _construct;
        cell = _cell ?? default;
        return _cell is not null;
    }

    /// <inheritdoc />
    public void OnServerHello(ServerHello hello)
    {
        ArgumentNullException.ThrowIfNull(hello);
        _entity = hello.YourEntity;
        _cell = null;
    }

    /// <inheritdoc />
    public void OnMessage(Message message)
    {
        if (message is OperationApplied { Operation: EntityReparentOp op } && _entity != default && op.Entity == _entity)
        {
            _cell = op.NewParent.Kind == FrameKind.Construct ? op.SeatCell : null;
            _construct = _cell is null ? default : new EntityId(op.NewParent.Id);
        }
    }

    /// <inheritdoc />
    public void OnDisconnected(DisconnectCode code) => _cell = null;
}
