using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Simulation;

namespace Horyzont.Client.Props;

/// <summary>
/// The harvested props of a client that shares the world with the server (solo and host, K6-31 D18): the server sends no prop states to
/// the local player, so they are read from the world state itself, which is always current. <see cref="RegionChanged"/> comes from the
/// operations the simulation applies (<see cref="OperationProps"/>, so also a prop harvested by a dig or regrown inside a compound). The
/// world is loaded after the client is composed, so the simulation is asked at every lookup and the subscription is made as soon as it exists.
/// Main thread only.
/// </summary>
/// <param name="simulation">The simulation of the server in this process; null before the world is loaded.</param>
public sealed class SharedPropStates(Func<ServerSimulation?> simulation) : IPropStateView
{
    private readonly Func<ServerSimulation?> _simulation = simulation ?? throw new ArgumentNullException(nameof(simulation));
    private readonly List<PropChange> _changes = [];
    private readonly List<(PlanetId Planet, PropRegionKey Region)> _regions = [];
    private Action<PlanetId, PropRegionKey>? _changed;
    private ServerSimulation? _attached;

    /// <inheritdoc />
    public event Action<PlanetId, PropRegionKey>? RegionChanged
    {
        add
        {
            _changed += value;
            Attach();
        }

        remove => _changed -= value;
    }

    /// <inheritdoc />
    public bool IsHarvested(PlanetId planet, PropId prop)
    {
        var sim = Attach();
        return sim is not null && sim.State.HarvestedProps.Contains(planet, prop);
    }

    private ServerSimulation? Attach()
    {
        var sim = _simulation();
        if (sim is null || sim == _attached)
        {
            return sim;
        }

        if (_attached is not null)
        {
            _attached.Applied -= OnApplied;
        }

        _attached = sim;
        sim.Applied += OnApplied;
        return sim;
    }

    private void OnApplied(AppliedOperation applied)
    {
        if (_changed is null)
        {
            return;
        }

        _changes.Clear();
        OperationProps.CollectChanges(applied.Operation, _changes);
        _regions.Clear();
        foreach (var change in _changes)
        {
            var key = (change.Planet, change.Prop.Region);
            if (!_regions.Contains(key))
            {
                _regions.Add(key);
            }
        }

        foreach (var (planet, region) in _regions)
        {
            _changed?.Invoke(planet, region);
        }
    }
}
