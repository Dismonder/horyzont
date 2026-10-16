using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Props;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.World;

namespace Horyzont.Client.Props;

/// <summary>
/// The harvested props of a remote client (K6-31 D17, D18): the state of a region is the whole truth the server last sent for it, so a
/// message replaces the region (an empty list clears it). The client forgets a region by its own distance (<see cref="DropMeters"/>, larger
/// than the leaving distance of the server), so coming back always brings a fresh state; a disconnect forgets everything. Main thread only.
/// </summary>
public sealed class ClientPropStates : IPropStateView
{
    /// <summary>Distance from the body beyond which a region is forgotten, in metres; always larger than the leaving distance of the server.</summary>
    public const float DropMeters = 768f;

    private const int CellsPerRegionEdge = 8;

    private readonly Dictionary<RegionId, HashSet<ulong>> _regions = [];
    private readonly List<RegionId> _scratch = [];

    /// <inheritdoc />
    public event Action<PlanetId, PropRegionKey>? RegionChanged;

    /// <summary>Gets the number of regions the client holds a state with harvested props for.</summary>
    public int RegionCount => _regions.Count;

    /// <inheritdoc />
    public bool IsHarvested(PlanetId planet, PropId prop) =>
        _regions.TryGetValue(new RegionId(planet.Value, prop.Region.Value), out var set) && set.Contains(prop.Value);

    /// <summary>Replaces the state of every region of a message.</summary>
    /// <param name="message">Message 45.</param>
    public void Apply(PropStates message)
    {
        ArgumentNullException.ThrowIfNull(message);
        foreach (var region in message.Regions)
        {
            var id = new RegionId(message.Planet.Value, region.Region.Value);
            if (region.Props.Count == 0)
            {
                _regions.Remove(id);
            }
            else
            {
                var set = new HashSet<ulong>(region.Props.Count);
                foreach (var entry in region.Props)
                {
                    set.Add(entry.Prop.Value);
                }

                _regions[id] = set;
            }

            RegionChanged?.Invoke(message.Planet, region.Region);
        }
    }

    /// <summary>Forgets the regions farther than <see cref="DropMeters"/> from the body, and those of other planets.</summary>
    /// <param name="planet">The planet the body stands on.</param>
    /// <param name="feetInPlanet">The feet in the frame of the planet.</param>
    public void Forget(PlanetParams planet, Vector3 feetInPlanet)
    {
        ArgumentNullException.ThrowIfNull(planet);
        _scratch.Clear();
        foreach (var id in _regions.Keys)
        {
            if (id.Planet != planet.Id.Value || DistanceToRegion(planet, feetInPlanet, new PropRegionKey(id.Region)) > DropMeters)
            {
                _scratch.Add(id);
            }
        }

        foreach (var id in _scratch)
        {
            _regions.Remove(id);
        }
    }

    /// <summary>Forgets everything (disconnect); raises <see cref="RegionChanged"/> for every region that held a state.</summary>
    public void Clear()
    {
        if (_regions.Count == 0)
        {
            return;
        }

        var all = new List<RegionId>(_regions.Keys);
        _regions.Clear();
        foreach (var id in all)
        {
            RegionChanged?.Invoke(new PlanetId(id.Planet), new PropRegionKey(id.Region));
        }
    }

    private static float DistanceToRegion(PlanetParams planet, Vector3 feet, PropRegionKey region)
    {
        if (!region.IsValid || !CubeSphereGrid.TryGetCellsPerFace(planet.RadiusMeters, out var cells))
        {
            return float.PositiveInfinity;
        }

        var centre = CubeSphereGrid.DirectionOfPoint(
            region.Face, (region.RegionU * CellsPerRegionEdge) + (CellsPerRegionEdge / 2.0), (region.RegionV * CellsPerRegionEdge) + (CellsPerRegionEdge / 2.0),
            cells);
        return Vector3.Distance(feet, centre * planet.RadiusMeters);
    }

    private readonly record struct RegionId(ulong Planet, ulong Region);
}
