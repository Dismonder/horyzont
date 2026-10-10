using Godot;

using Horyzont.Core.World;
using Horyzont.Core.World.Terrain;
using Horyzont.Game.Planet;
using Horyzont.Game.Terrain;
using Horyzont.Persistence.Terrain;

namespace Horyzont.Game.Server;

/// <summary>
/// The persistent mipmaps of the edited terrain of the server (K3-16, closing the deferral of K2-09): one <see cref="DerivedLodRebuilder"/>
/// per planet over the block buffer, one disposable database <c>terrain-derived.db</c> next to the world file. Voxel Tools writes the
/// mipmaps of an edit only when it unloads a block, so a far excavation could be lost; these are rebuilt from the edited LOD 0 blocks
/// after every edit and after a restart. The data is no checkpoint and no journal: it can be deleted at any time. Main thread, except
/// the reads of the streams.
/// </summary>
public sealed class ServerDerivedTerrain : IDisposable
{
    private readonly DerivedLodStore _store;
    private readonly Dictionary<ulong, DerivedLodRebuilder> _rebuilders = [];
    private readonly List<DerivedLodRebuilder> _ordered = [];
    private readonly List<GeneratedBlockSource> _sources = [];
    private readonly TerrainBlockCodec _codec = new();

    /// <summary>Creates the container over an opened store.</summary>
    /// <param name="store">The derived database.</param>
    public ServerDerivedTerrain(DerivedLodStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <summary>Gets the number of planets with a rebuilder.</summary>
    public int PlanetCount => _rebuilders.Count;

    /// <summary>The fingerprint of the profile of a planet: what makes a derived block mean something else when it changes.</summary>
    /// <param name="planet">The planet.</param>
    /// <returns>The text.</returns>
    public static string ProfileOf(PlanetParams planet)
    {
        ArgumentNullException.ThrowIfNull(planet);
        return $"{planet}|semantics={TerrainEditSemantics.Current}|derived=1";
    }

    /// <summary>Makes the rebuilder of a planet.</summary>
    /// <param name="planet">The planet.</param>
    /// <param name="source">Authoritative LOD 0 blocks.</param>
    /// <param name="generator">Generator of the planet; its blocks fill the children nobody edited (main thread).</param>
    /// <returns>The rebuilder to give to the stream of the planet.</returns>
    public DerivedLodRebuilder Add(PlanetParams planet, ITerrainBlockSource source, VoxelGenerator generator)
    {
        ArgumentNullException.ThrowIfNull(planet);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(generator);
        var generated = new GeneratedBlockSource(generator, _codec);
        var maxLod = (byte)(PlanetTerrainLayout.LodCount(planet) - 1);
        var rebuilder = new DerivedLodRebuilder(source, generated.Encode, _codec, _store, ProfileOf(planet), maxLod);
        _rebuilders.Add(planet.Id.Value, rebuilder);
        _ordered.Add(rebuilder);
        _sources.Add(generated);
        return rebuilder;
    }

    /// <summary>Marks the mipmaps above blocks that were edited as stale.</summary>
    /// <param name="versions">Edited blocks.</param>
    public void Invalidate(IEnumerable<TerrainBlockVersion> versions)
    {
        ArgumentNullException.ThrowIfNull(versions);
        foreach (var version in versions)
        {
            if (version.Key.Lod == 0 && _rebuilders.TryGetValue(version.Key.Planet.Value, out var rebuilder))
            {
                rebuilder.Invalidate(version.Key);
            }
        }
    }

    /// <summary>Builds at most two mipmaps of every planet; call once per server tick.</summary>
    public void Step()
    {
        for (var i = 0; i < _ordered.Count; i++)
        {
            _ordered[i].Step(DerivedLodRebuilder.MaxBlocksPerStep);
        }
    }

    /// <summary>Drains the database writer (step 7a of the shutdown, after the terrain stopped); the used database is never closed on a timeout.</summary>
    /// <returns>True when the writer finished in time.</returns>
    public Task<bool> CloseAsync() => _store.CloseAsync();

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var source in _sources)
        {
            source.Dispose();
        }

        _sources.Clear();
        _codec.Dispose();
    }
}
