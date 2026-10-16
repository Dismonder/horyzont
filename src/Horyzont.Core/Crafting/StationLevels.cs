using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Simulation;
using Horyzont.Core.World;
using Horyzont.Core.World.Blocks;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.Crafting;

/// <summary>The level of a workbench from the upgrades around it (K6-32, D14).</summary>
public static class StationLevels
{
    /// <summary>Half the diagonal of a chunk of 16 cells: every cell centre of a chunk lies within this of the chunk centre.</summary>
    private const float ChunkRadius = 13.86f;

    /// <summary>
    /// 1 plus the number of different block types with the function <see cref="BlockFunctionKind.StationUpgrade"/> of the same station kind whose cell
    /// centre lies within <c>GameRules.StationUpgradeRangeMeters</c> (inclusive) of the centre of the station cell, in any construct on the same planet,
    /// capped at <see cref="StationKinds.MaxLevel"/>.
    /// </summary>
    /// <param name="state">World state.</param>
    /// <param name="rules">Game rules (<c>StationUpgradeRangeMeters</c>).</param>
    /// <param name="content">Block content.</param>
    /// <param name="construct">Construct of the station.</param>
    /// <param name="cell">Cell of the station.</param>
    /// <returns>The level, at least 1.</returns>
    public static int Compute(WorldState state, GameRules rules, BlockContentTable content, EntityId construct, CellPos cell)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(content);
        if (!state.Constructs.TryGet(construct, out var host))
        {
            return 1;
        }

        var function = content.Get(host.GetBlock(cell).TypeId).Function;
        if (function.Kind != BlockFunctionKind.Station)
        {
            return 1;
        }

        var kind = function.Station;
        var most = StationKinds.MaxLevel(kind) - 1;
        if (most <= 0 || !ContainerReach.TryCellCenter(state, construct, cell, out var planet, out var centre))
        {
            return 1;
        }

        var range = rules.StationUpgradeRangeMeters;
        Span<ushort> seen = stackalloc ushort[most];
        var found = 0;

        // The constructs are visited in id order; the answer is a count of distinct types, so it does not depend on the order anyway.
        var all = state.Constructs.All;
        for (var c = 0; c < all.Count && found < most; c++)
        {
            var other = all[c];
            if (!IsNear(state, other, planet, centre, range))
            {
                continue;
            }

            foreach (var chunk in other.Chunks)
            {
                if (chunk.Value.NonEmptyCount == 0 || found >= most)
                {
                    continue;
                }

                var chunkLocal = new Vector3((chunk.Key.X * 16) + 8f, (chunk.Key.Y * 16) + 8f, (chunk.Key.Z * 16) + 8f);
                if (!TryToPlanet(state, other, chunkLocal, planet, out var chunkCentre) || Vector3.Distance(chunkCentre, centre) > range + ChunkRadius)
                {
                    continue;
                }

                var blocks = chunk.Value.Blocks;
                for (var i = 0; i < blocks.Length && found < most; i++)
                {
                    var type = (ushort)(blocks[i] & 0xFFFFu);
                    if (type == 0)
                    {
                        continue;
                    }

                    var def = content.Get(type).Function;
                    if (def.Kind != BlockFunctionKind.StationUpgrade || def.Station != kind || Contains(seen, found, type))
                    {
                        continue;
                    }

                    var at = CellPos.FromChunk(chunk.Key, i);
                    var local = new Vector3(at.X + 0.5f, at.Y + 0.5f, at.Z + 0.5f);
                    if (TryToPlanet(state, other, local, planet, out var upgradeCentre) && Vector3.Distance(upgradeCentre, centre) <= range)
                    {
                        seen[found++] = type;
                    }
                }
            }
        }

        return 1 + found;
    }

    private static bool Contains(Span<ushort> seen, int count, ushort type)
    {
        for (var i = 0; i < count; i++)
        {
            if (seen[i] == type)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A cheap test on the bounds of a construct: false when no cell of it can be within <paramref name="range"/> of <paramref name="centre"/>.</summary>
    private static bool IsNear(WorldState state, Construct construct, PlanetId planet, Vector3 centre, float range)
    {
        if (construct.BlockCount == 0)
        {
            return false;
        }

        var min = construct.BoundsMin;
        var max = construct.BoundsMax;
        var middle = new Vector3((min.X + max.X + 1) * 0.5f, (min.Y + max.Y + 1) * 0.5f, (min.Z + max.Z + 1) * 0.5f);
        var half = new Vector3(max.X + 1 - min.X, max.Y + 1 - min.Y, max.Z + 1 - min.Z).Length() * 0.5f;
        return TryToPlanet(state, construct, middle, planet, out var middlePlanet) && Vector3.Distance(middlePlanet, centre) <= range + half;
    }

    private static bool TryToPlanet(WorldState state, Construct construct, Vector3 local, PlanetId planet, out Vector3 point)
    {
        if (state.TryToPlanetFrame(FrameRef.Of(construct.Id), local, out var found, out point) && found == planet)
        {
            return true;
        }

        point = default;
        return false;
    }
}
