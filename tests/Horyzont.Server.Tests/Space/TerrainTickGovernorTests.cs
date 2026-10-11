using Horyzont.Core.Ids;
using Horyzont.Server.Space;

using Microsoft.Extensions.Logging;

namespace Horyzont.Server.Tests.Space;

public sealed class TerrainTickGovernorTests
{
    private const int TickRate = 60;

    [Fact]
    public void TerrainTickGovernor_OverBudget_ThrottlesSoloPlanets_RestoresBelow()
    {
        var governor = new TerrainTickGovernor(TickRate, new Captured<TerrainTickGovernor>());
        Feed(governor, 7.5, 10 * TickRate);
        Assert.False(governor.Throttled);
        Assert.True(governor.ShouldUpdateViewers(1, 3));

        Feed(governor, 9.5, 5 * TickRate);
        Assert.True(governor.Throttled);
        Assert.True(governor.ShouldUpdateViewers(1, 8));
        Assert.False(governor.ShouldUpdateViewers(1, 9));
        Assert.True(governor.ShouldUpdateViewers(2, 9));
        Assert.True(governor.ShouldUpdateViewers(0, 9));

        // Between 6 and 8 ms the mode holds; below 6 ms for 10 s it ends.
        Feed(governor, 7.0, 20 * TickRate);
        Assert.True(governor.Throttled);
        // The 5 s average falls below 6 ms a third of a window after the drop; 10 s later the mode ends.
        Feed(governor, 4.0, 10 * TickRate);
        Assert.True(governor.Throttled);
        Feed(governor, 4.0, 2 * TickRate);
        Assert.False(governor.Throttled);
        Assert.True(governor.ShouldUpdateViewers(1, 9));
    }

    [Fact]
    public void TerrainTickGovernor_LogsEveryTerrain()
    {
        var logger = new Captured<TerrainTickGovernor>();
        var governor = new TerrainTickGovernor(TickRate, logger);
        var terrains = new[]
        {
            new PlanetTerrainStats(new PlanetId(1), 2, 3, 400, 1.5),
            new PlanetTerrainStats(new PlanetId(2), 1, 1, 120, 0.7),
            new PlanetTerrainStats(new PlanetId(3), 1, 2, 90, 0.4),
        };
        var rounds = 0;
        for (var i = 0; i < 30 * TickRate; i++)
        {
            governor.Observe(10.0);
            if (governor.LogDue)
            {
                rounds++;
                foreach (var terrain in terrains)
                {
                    governor.LogTerrain(terrain);
                }
            }
        }

        // Over budget from the end of the first 5 s window: rounds at 5 s, 15 s, 25 s.
        Assert.Equal(3, rounds);
        var entries = logger.Messages.Where(m => m.StartsWith("Terrain of planet", StringComparison.Ordinal)).ToList();
        Assert.Equal(9, entries.Count);
        Assert.Contains(entries, m => m.Contains("planet 2:", StringComparison.Ordinal) && m.Contains("120 blocks", StringComparison.Ordinal));
        Assert.Single(logger.Messages, m => m.StartsWith("Tick over budget", StringComparison.Ordinal));
    }

    private static void Feed(TerrainTickGovernor governor, double milliseconds, int ticks)
    {
        for (var i = 0; i < ticks; i++)
        {
            governor.Observe(milliseconds);
        }
    }

    private sealed class Captured<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
