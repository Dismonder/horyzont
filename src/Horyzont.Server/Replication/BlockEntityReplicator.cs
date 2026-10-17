using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Simulation;

using Microsoft.Extensions.Logging;

namespace Horyzont.Server.Replication;

/// <summary>
/// Replicates the states of the functional blocks of constructs (message 46, K6-30 D15): the whole list of a construct goes to a
/// recipient when its link becomes live (after the state of the construct and the operations buffered behind it) and, after every
/// applied operation that changed a block state, to every recipient whose link is live. Created after <see cref="EntityReplication"/>,
/// so the block states follow the <see cref="OperationApplied"/> of the same operation. Main thread only.
/// </summary>
public sealed class BlockEntityReplicator
{
    private readonly ServerSimulation _simulation;
    private readonly ConstructStateSender _constructs;
    private readonly ILogger _logger;
    private readonly List<EntityId> _changed = [];
    private bool _detached;

    /// <summary>Initializes a new instance of the <see cref="BlockEntityReplicator"/> class and subscribes to the applied operations and the live links.</summary>
    /// <param name="simulation">The simulation whose operations change block states.</param>
    /// <param name="constructs">The construct state sender that knows the links of the recipients.</param>
    /// <param name="logger">Logger.</param>
    public BlockEntityReplicator(ServerSimulation simulation, ConstructStateSender constructs, ILogger<BlockEntityReplicator> logger)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(constructs);
        ArgumentNullException.ThrowIfNull(logger);
        _simulation = simulation;
        _constructs = constructs;
        _logger = logger;
        _simulation.Applied += OnApplied;
        _constructs.LinkLive += OnLinkLive;
    }

    /// <summary>Stops listening (shutdown).</summary>
    public void Detach()
    {
        if (_detached)
        {
            return;
        }

        _detached = true;
        _simulation.Applied -= OnApplied;
        _constructs.LinkLive -= OnLinkLive;
    }

    private void OnLinkLive(PlayerId player, EntityId construct)
    {
        if (_detached || _simulation.State.BlockEntities.CountOf(construct) == 0)
        {
            return;
        }

        // A fresh full state empties the map of the client, so a construct without states needs no message.
        _constructs.SendLive(player, StatesOf(construct));
        _logger.BlockStatesSentOnLinkLive(player.Value, construct.Value);
    }

    private void OnApplied(AppliedOperation applied)
    {
        if (_detached)
        {
            return;
        }

        _changed.Clear();
        Collect(applied.Operation);
        foreach (var construct in _changed)
        {
            // A construct that the operation removed is dropped by its own message; nothing is left to describe.
            if (!_simulation.State.Constructs.TryGet(construct, out _))
            {
                continue;
            }

            var recipients = _constructs.SendLiveToAll(construct, StatesOf(construct));
            _logger.BlockStatesSentForOperation(construct.Value, applied.Id.Value, recipients);
        }

        _changed.Clear();
    }

    private void Collect(Operation operation)
    {
        switch (operation)
        {
            case BlockEntitySetOp set:
                if (!_changed.Contains(set.Construct))
                {
                    _changed.Add(set.Construct);
                }

                break;
            case CompoundOp compound:
                foreach (var component in compound.Components)
                {
                    if (component is BlockEntitySetOp)
                    {
                        Collect(component);
                    }
                }

                break;
            default:
                break;
        }
    }

    private BlockEntityStates StatesOf(EntityId construct)
    {
        var entries = new List<BlockEntityEntry>(_simulation.State.BlockEntities.CountOf(construct));
        foreach (var pair in _simulation.State.BlockEntities.All)
        {
            if (pair.Key.Construct == construct)
            {
                entries.Add(new BlockEntityEntry(pair.Key.Cell, pair.Value));
            }
            else if (entries.Count > 0)
            {
                // Sorted by construct: the states of this one are over.
                break;
            }
        }

        return new BlockEntityStates(construct, entries);
    }
}
