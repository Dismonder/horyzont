using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Simulation;
using Horyzont.Persistence.Terrain;

namespace Horyzont.Game.Terrain;

/// <summary>
/// Replays the journal tail at server start with terrain loading (K2-09): Voxel Tools cannot load synchronously, so every terrain
/// edit first loads its area through <see cref="TerrainAreaLoader"/>, then goes to <see cref="ServerSimulation.Replay"/>.
/// </summary>
public static class TerrainReplay
{
    /// <summary>The planets edited by the terrain operations of a journal tail, sorted by id (K5-22: the replay leases them).</summary>
    /// <param name="tail">Journal tail.</param>
    /// <returns>The planets.</returns>
    public static IReadOnlyList<PlanetId> EditedPlanets(IReadOnlyList<JournalEntry> tail)
    {
        ArgumentNullException.ThrowIfNull(tail);
        var planets = new SortedSet<ulong>();
        foreach (var entry in tail)
        {
            if (entry.Operation is TerrainEditOp edit)
            {
                planets.Add(edit.Planet.Value);
            }
        }

        var result = new List<PlanetId>(planets.Count);
        foreach (var planet in planets)
        {
            result.Add(new PlanetId(planet));
        }

        return result;
    }

    /// <summary>
    /// Checks the terrain semantics of the tail, then replays it. A tail with a terrain edit written under other semantics is not
    /// replayed at all (the result says so; the world opens in safe mode, K2-10). Entries from disk are treated as hostile: a
    /// terrain edit on an unknown planet or failing <see cref="TerrainRules.CheckEdit"/> stops the replay before any viewer exists.
    /// Main thread.
    /// </summary>
    /// <param name="simulation">The simulation of the loaded world.</param>
    /// <param name="tail">Journal tail after the checkpoint.</param>
    /// <param name="loader">Area loader over the terrain service of the simulation.</param>
    /// <param name="savedSemantics">Semantics string from the world meta table (<see cref="TerrainSemanticsMeta.Read"/>).</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>The semantics check; when it is a mismatch, nothing was replayed.</returns>
    /// <exception cref="ReplayFailedException">A hostile or failing entry; carries its operation number.</exception>
    /// <exception cref="TimeoutException">An area did not load in time.</exception>
    public static async Task<TerrainTailCheck> ReplayAsync(
        ServerSimulation simulation, IReadOnlyList<JournalEntry> tail, TerrainAreaLoader loader, string savedSemantics, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(tail);
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(savedSemantics);
        var check = TerrainSemanticsMeta.CheckTail(savedSemantics, TerrainEditSemantics.Current, tail);
        if (check.IsMismatch)
        {
            return check;
        }

        try
        {
            foreach (var entry in tail)
            {
                ct.ThrowIfCancellationRequested();
                if (OperationTerrain.TryGetTerrainEdit(entry.Operation, out var edit))
                {
                    if (!simulation.State.Planets.TryGet(edit.Planet, out var planet))
                    {
                        throw new ReplayFailedException(entry.Id, edit.Kind, "unknown planet");
                    }

                    if (!TerrainRules.CheckEdit(edit, planet, out var failure))
                    {
                        throw new ReplayFailedException(entry.Id, edit.Kind, failure!);
                    }

                    await loader.EnsureLoadedAsync(
                        edit.Planet, edit.Center, edit.Radius + (TerrainRules.EditMarginVoxels * planet.VoxelSizeMeters), ct).ConfigureAwait(true);
                }

                simulation.Replay(entry);
            }
        }
        finally
        {
            loader.ReleaseViewers();
        }

        return check;
    }
}
