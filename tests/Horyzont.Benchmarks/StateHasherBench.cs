using BenchmarkDotNet.Attributes;

using Horyzont.Core.Hashing;
using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Simulation;
using Horyzont.Core.World;
using Horyzont.Testing;

using Microsoft.Extensions.Logging.Abstractions;

namespace Horyzont.Benchmarks;

/// <summary>The state hash of a world after 10 000 operations of the shared deterministic script.</summary>
[MemoryDiagnoser]
public class StateHasherBench
{
    private WorldState _state = null!;

    [GlobalSetup]
    public void Setup()
    {
        var state = WorldFactory.CreateDefault();
        var sim = new ServerSimulation(state, new FakeTerrainService(), new NullJournal(), new GameRules(), NullLogger<ServerSimulation>.Instance);
        new WorldScript(7).RunUntil(sim, new OpId(10_000));
        _state = sim.State;
    }

    [Benchmark]
    public ulong Hash() => StateHasher.Hash(_state);

    private sealed class NullJournal : IJournalSink
    {
        public void Append(JournalEntry entry)
        {
        }
    }
}
