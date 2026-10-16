using System.Numerics;

using Horyzont.Core.Crafting;
using Horyzont.Core.Ids;
using Horyzont.Core.Protocol;
using Horyzont.Core.World;
using Horyzont.Core.World.Blocks;
using Horyzont.Core.World.Constructs;
using Horyzont.Core.World.Terrain;

namespace Horyzont.Core.Survival;

/// <summary>
/// The roof, the heat and the comfort around a point (K6-33 D14). All limits are inclusive, like <see cref="StationLevels"/>: a block counts when the
/// distance of its cell centre to the point is at most the range, and the roof when the vertical ray meets a construct or the terrain at a distance of at
/// most <see cref="VitalsRules.RestRoofRayMeters"/>. The roof is one vertical ray (the full shelter of 26 rays is K6-34), so a point just beside a
/// ceiling is not roofed.
/// </summary>
public static class RestEnvironment
{
    /// <summary>Words of the bitset of the comfort types already counted: one bit per block type id 0 to <see cref="ProtocolConstants.MaxBlockTypes"/>.</summary>
    private const int SeenWords = (ProtocolConstants.MaxBlockTypes / 64) + 1;

    /// <summary>Half the diagonal of a chunk of 16 cells: every cell centre of a chunk lies within this of the chunk centre.</summary>
    private const float ChunkRadius = 13.86f;

    /// <summary>
    /// Evaluates the surroundings of a point; called every 30 ticks for the resting players on the main thread, never by the applier. Allocates nothing:
    /// the different comfort types are a bitset on the stack.
    /// </summary>
    /// <param name="state">The world (constructs).</param>
    /// <param name="blocks">Block content: comfort and heat.</param>
    /// <param name="terrain">The terrain (the roof ray).</param>
    /// <param name="planet">Planet of the point.</param>
    /// <param name="point">The point in the frame of the planet.</param>
    /// <param name="rules">The numbers of the ranges.</param>
    /// <returns>The conditions.</returns>
    public static RestConditions Evaluate(
        WorldState state, BlockContentTable blocks, ITerrainService terrain, PlanetId planet, Vector3 point, VitalsRules rules)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(rules);

        var up = TerrainMath.UpAt(point);
        var roofed = ConstructRaycast.Raycast(state.Constructs, FrameRef.Of(planet), point, up, rules.RestRoofRayMeters, out _)
            || terrain.Raycast(planet, point, up, rules.RestRoofRayMeters, out _);

        var heatRange = rules.RestHeatRangeMeters;
        var comfortRange = rules.RestComfortRangeMeters;
        var scanRange = Math.Max(heatRange, comfortRange);
        Span<ulong> seen = stackalloc ulong[SeenWords];
        var heated = false;
        var comfort = 0;

        var all = state.Constructs.All;
        for (var c = 0; c < all.Count; c++)
        {
            var construct = all[c];
            if (!StationLevels.IsNear(state, construct, planet, point, scanRange))
            {
                continue;
            }

            // The order of the chunks does not change the result (a flag and a sum over different types), so the unordered enumerator avoids the sort.
            var chunks = construct.EnumerateChunksUnordered();
            while (chunks.MoveNext())
            {
                var chunk = chunks.Current;
                if (chunk.Value.NonEmptyCount == 0)
                {
                    continue;
                }

                var chunkLocal = new Vector3((chunk.Key.X * 16) + 8f, (chunk.Key.Y * 16) + 8f, (chunk.Key.Z * 16) + 8f);
                if (!StationLevels.TryToPlanet(state, construct, chunkLocal, planet, out var chunkCentre) || Vector3.Distance(chunkCentre, point) > scanRange + ChunkRadius)
                {
                    continue;
                }

                var cells = chunk.Value.Blocks;
                for (var i = 0; i < cells.Length; i++)
                {
                    var type = (ushort)(cells[i] & 0xFFFFu);
                    if (type == 0)
                    {
                        continue;
                    }

                    var def = blocks.Get(type);
                    if (def.Comfort == 0 && !def.Heat)
                    {
                        continue;
                    }

                    var at = CellPos.FromChunk(chunk.Key, i);
                    var local = new Vector3(at.X + 0.5f, at.Y + 0.5f, at.Z + 0.5f);
                    if (!StationLevels.TryToPlanet(state, construct, local, planet, out var centre))
                    {
                        continue;
                    }

                    var distance = Vector3.Distance(centre, point);
                    heated |= def.Heat && distance <= heatRange;
                    if (def.Comfort > 0 && distance <= comfortRange && type <= ProtocolConstants.MaxBlockTypes && MarkSeen(seen, type))
                    {
                        comfort += def.Comfort;
                    }
                }
            }
        }

        return new RestConditions(roofed, heated, (byte)Math.Clamp(comfort, 0, rules.MaxComfort));
    }

    /// <summary>Marks a block type as counted.</summary>
    /// <returns>True when the type was not counted before.</returns>
    private static bool MarkSeen(Span<ulong> seen, ushort type)
    {
        var word = type >> 6;
        var bit = 1UL << (type & 63);
        if ((seen[word] & bit) != 0)
        {
            return false;
        }

        seen[word] |= bit;
        return true;
    }
}
