using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.World.Terrain;

namespace Horyzont.Client.Terrain;

/// <summary>Builds the cache report of the client: the versions it holds, nearest to the viewer first, in messages the codec accepts.</summary>
public static class TerrainCacheReports
{
    /// <summary>Largest number of keys the server takes per planet; the client keeps the nearest ones and skips the rest.</summary>
    public const int MaxKeysPerPlanet = 65_536;

    /// <summary>Splits the cached versions of a planet into report messages.</summary>
    /// <param name="planet">The planet.</param>
    /// <param name="versions">Cached versions (blocks with data; any order).</param>
    /// <param name="viewer">Position the nearest blocks are chosen around, planet frame.</param>
    /// <param name="voxelSize">Voxel size of the planet in metres.</param>
    /// <returns>Messages of at most 4096 entries each; none when the cache holds nothing.</returns>
    public static IReadOnlyList<TerrainCacheReport> Build(PlanetId planet, ReadOnlySpan<TerrainBlockVersion> versions, Vector3 viewer, float voxelSize)
    {
        var edge = TerrainMath.BlockSize * voxelSize;
        var ranked = new List<(float Distance, TerrainBlockVersion Version)>(versions.Length);
        foreach (var version in versions)
        {
            var key = version.Key;
            if (key.Planet != planet || key.Lod != 0 || version.Version == 0)
            {
                continue;
            }

            var center = new Vector3((key.X + 0.5f) * edge, (key.Y + 0.5f) * edge, (key.Z + 0.5f) * edge);
            ranked.Add((Vector3.DistanceSquared(center, viewer), version));
        }

        ranked.Sort(static (a, b) =>
        {
            var c = a.Distance.CompareTo(b.Distance);
            return c != 0 ? c : a.Version.Key.CompareTo(b.Version.Key);
        });
        var count = Math.Min(ranked.Count, MaxKeysPerPlanet);
        var reports = new List<TerrainCacheReport>();
        for (var start = 0; start < count; start += ProtocolConstants.MaxTerrainCacheEntries)
        {
            var take = Math.Min(ProtocolConstants.MaxTerrainCacheEntries, count - start);
            var entries = new TerrainBlockVersion[take];
            for (var i = 0; i < take; i++)
            {
                entries[i] = ranked[start + i].Version;
            }

            reports.Add(new TerrainCacheReport(planet, entries));
        }

        return reports;
    }
}
