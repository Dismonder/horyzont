using Horyzont.Client.Tools;
using Horyzont.Core.Intents;
using Horyzont.Core.Protocol.Messages;

namespace Horyzont.Client.Replication;

/// <summary>
/// Routes the edit prediction by intent: block edits to the construct overlay, terrain edits to the terrain prediction of K3-16
/// (<see cref="NoEditPrediction"/> until then). Replies are shown to both before the tracker settles the intent.
/// </summary>
public sealed class CompositeEditPrediction : IEditPrediction, IIntentReplyObserver
{
    private readonly IEditPrediction _blocks;
    private readonly IEditPrediction _terrain;
    private readonly List<(uint Seq, IEditPrediction Owner)> _routes = new(PendingIntentTracker.MaxPending);

    /// <summary>Initializes a new instance of the <see cref="CompositeEditPrediction"/> class.</summary>
    /// <param name="blocks">Prediction of block edits.</param>
    /// <param name="terrain">Prediction of terrain edits; none when null.</param>
    public CompositeEditPrediction(IEditPrediction blocks, IEditPrediction? terrain = null)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        _blocks = blocks;
        _terrain = terrain ?? NoEditPrediction.Instance;
    }

    /// <inheritdoc />
    public void Begin(uint clientSeq, Intent intent, in ToolPreview preview)
    {
        var owner = intent is PlaceBlockIntent or RemoveBlockIntent ? _blocks : _terrain;
        _routes.Add((clientSeq, owner));
        owner.Begin(clientSeq, intent, in preview);
    }

    /// <inheritdoc />
    public void NoteReply(IntentReply reply)
    {
        ArgumentNullException.ThrowIfNull(reply);
        if (_blocks is IIntentReplyObserver blocks)
        {
            blocks.NoteReply(reply);
        }

        if (_terrain is IIntentReplyObserver terrain)
        {
            terrain.NoteReply(reply);
        }
    }

    /// <inheritdoc />
    public void Commit(uint clientSeq) => Take(clientSeq)?.Commit(clientSeq);

    /// <inheritdoc />
    public void Revert(uint clientSeq) => Take(clientSeq)?.Revert(clientSeq);

    private IEditPrediction? Take(uint clientSeq)
    {
        for (var i = 0; i < _routes.Count; i++)
        {
            if (_routes[i].Seq == clientSeq)
            {
                var owner = _routes[i].Owner;
                _routes.RemoveAt(i);
                return owner;
            }
        }

        return null;
    }
}
