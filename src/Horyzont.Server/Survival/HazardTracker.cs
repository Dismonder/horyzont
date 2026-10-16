using Horyzont.Core.Ids;
using Horyzont.Core.Simulation;
using Horyzont.Core.Survival;
using Horyzont.Core.World;

namespace Horyzont.Server.Survival;

/// <summary>
/// The surroundings of the players for the suit (K6-34 D17): the profile, the height, the time of day, the shelter, the generator and the seat, evaluated for a player every
/// <see cref="HazardRules.HazardEvaluateEveryTicks"/> ticks (spread over the players by their ids) and at once when there is no sample yet or the frame, the parent counter or the seat changed. Main thread
/// only (the terrain); allocates nothing after the first sample of a player.
/// </summary>
internal sealed class HazardTracker
{
    /// <summary>Initializes a new instance of the <see cref="HazardTracker"/> class.</summary>
    /// <param name="simulation">The simulation, with the hazard content.</param>
    public HazardTracker(ServerSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        throw new NotImplementedException("K6-34 C3");
    }

    /// <summary>Gets the number of evaluations done so far (for the tests of the schedule).</summary>
    public int Evaluations => throw new NotImplementedException("K6-34 C3");

    /// <summary>The sample of a player at a tick: the kept one, or a new one when it is due.</summary>
    /// <param name="player">The player.</param>
    /// <param name="now">The current tick.</param>
    /// <returns>The sample.</returns>
    public HazardSample Sample(PlayerState player, Tick now) => throw new NotImplementedException("K6-34 C3");

    /// <summary>Forgets a player (leaving), so the next sample is taken at once.</summary>
    /// <param name="player">The player.</param>
    public void Forget(PlayerId player) => throw new NotImplementedException("K6-34 C3");
}
