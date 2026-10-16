using FsCheck;
using FsCheck.Fluent;

using Horyzont.Core.BlockEntities;
using Horyzont.Core.Hashing;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Tests.BlockEntities;
using Horyzont.Core.Tests.Serialization;
using Horyzont.Core.World.Constructs;
using Horyzont.Testing;

using static Horyzont.Core.Tests.BlockEntities.FunctionalBlockFixture;

namespace Horyzont.Core.Tests.Simulation;

/// <summary>
/// K6-30 D10: four players sleep in, set the respawn point at, take down and put up beds. After every intent the respawn point of a player
/// is a bed whose owner is that player, and the owner of a bed has that bed as the respawn point. A property over sequences in which every
/// intent is refused holds trivially, so the generator must also be shown to reach the accepted cases and the refusals.
/// </summary>
public sealed class BedOwnershipProperties
{
    private const int Sequences = 1000;
    private const int StepsPerSequence = 50;
    private const int Players = 4;

    private static readonly CellPos[] _cells = [new(1, 1, 0), new(4, 1, 0), new(2, 0, 1)];

    /// <summary>The block and face a bed is put up against, per bed cell.</summary>
    private static readonly (CellPos Clicked, Face Face)[] _supports =
    [
        (FunctionalBlocks.FloorCells[1], Face.PosY),
        (FunctionalBlocks.FloorCells[4], Face.PosY),
        (FunctionalBlocks.FloorCells[2], Face.PosZ),
    ];

    private enum Action
    {
        Sleep,
        SetRespawn,
        Remove,
        PlaceBed,
    }

    private static Gen<Step> StepGen { get; } =
        from action in Gen.Frequency(
            (8, Gen.Constant(Action.Sleep)),
            (4, Gen.Constant(Action.SetRespawn)),
            (4, Gen.Constant(Action.Remove)),
            (4, Gen.Constant(Action.PlaceBed)))
        from player in Gen.Choose(1, Players)
        from bed in Gen.Choose(0, _cells.Length - 1)
        select new Step(action, player, bed);

    private static Gen<Step[]> Sequence { get; } = StepGen.ArrayOf(StepsPerSequence);

    [Fact]
    public void RandomSequences_KeepRespawnPointAndBedOwnerInStep()
    {
        Kit.ForAll(Sequence, sequence => Run(sequence, new Stats()), Sequences);

        AssertGeneratorCoverage();
    }

    [Fact]
    public void Generator_ReachesAcceptedSleepsRemovalsPlacementsAndNotOwnerRefusals() => AssertGeneratorCoverage();

    private static void AssertGeneratorCoverage()
    {
        var stats = new Stats();
        foreach (var sequence in Sequence.Sample(400, 20))
        {
            Run(sequence, stats);
        }

        Assert.True(stats.Slept >= 20, $"slept {stats.Slept} times");
        Assert.True(stats.RespawnSet >= 5, $"set the respawn point {stats.RespawnSet} times");
        Assert.True(stats.Removed >= 5, $"took a bed down {stats.Removed} times");
        Assert.True(stats.Placed >= 5, $"put a bed up {stats.Placed} times");
        Assert.True(stats.NotOwner >= 5, $"refused with NotOwner {stats.NotOwner} times");
    }

    private static void Run(Step[] sequence, Stats stats)
    {
        var f = ForIntents(Players);
        f.SetUpBed(_cells[0]);
        f.SetUpBed(_cells[1]);
        foreach (var step in sequence)
        {
            // Six ticks refill one block edit per player, so the limiter does not decide the outcome.
            f.Sim.Ticks(6);
            var player = new PlayerId((ulong)step.Player);
            var cell = _cells[step.Bed];
            var hash = StateHasher.Hash(f.State);
            var result = f.SubmitAs(player, Build(step, cell));
            if (result.IsAccepted)
            {
                stats.Count(step.Action);
            }
            else
            {
                Assert.Equal(hash, StateHasher.Hash(f.State));
                if (result.Reason == RejectReason.NotOwner)
                {
                    stats.NotOwner++;
                }
            }

            AssertOwnershipInStep(f);
        }
    }

    private static Intent Build(Step step, CellPos cell) => step.Action switch
    {
        Action.Sleep => new BlockInteractIntent(ConstructId, cell, BlockInteractAction.Sleep),
        Action.SetRespawn => new BlockInteractIntent(ConstructId, cell, BlockInteractAction.SetRespawn),
        Action.Remove => new RemoveBlockIntent(ConstructId, cell),
        _ => new PlaceBlockIntent(new ConstructFaceTarget(ConstructId, _supports[step.Bed].Clicked, _supports[step.Bed].Face), FunctionalBlocks.BedType, 0),
    };

    private static void AssertOwnershipInStep(FunctionalBlockFixture f)
    {
        foreach (var progress in f.State.Progress.All.Values)
        {
            var bed = progress.RespawnBed ?? throw new InvalidOperationException("A progress record without a respawn point exists.");
            Assert.True(
                f.State.BlockEntities.TryGet(new BlockRef(bed.Construct, bed.Cell), out var state), $"player {progress.Player.Value}: no bed at the respawn point");
            Assert.Equal(new BedBlockState(progress.Player), state);
            Assert.Equal(bed.ExpectedType, f.BlockAt(bed.Cell).TypeId);
        }

        foreach (var (key, state) in f.State.BlockEntities.All)
        {
            if (state is not BedBlockState { Owner.IsNone: false } bed)
            {
                continue;
            }

            Assert.Equal(FunctionalBlocks.BedType, f.BlockAt(key.Cell).TypeId);
            Assert.True(f.State.Progress.TryGet(bed.Owner, out var progress), $"owner {bed.Owner.Value} of the bed at {key.Cell} has no respawn point");
            Assert.Equal(BedAt(key.Cell), progress.RespawnBed);
        }
    }

    private sealed class Stats
    {
        public int Slept { get; private set; }

        public int RespawnSet { get; private set; }

        public int Removed { get; private set; }

        public int Placed { get; private set; }

        public int NotOwner { get; set; }

        public void Count(Action action)
        {
            switch (action)
            {
                case Action.Sleep:
                    Slept++;
                    break;
                case Action.SetRespawn:
                    RespawnSet++;
                    break;
                case Action.Remove:
                    Removed++;
                    break;
                default:
                    Placed++;
                    break;
            }
        }
    }

    private readonly record struct Step(Action Action, int Player, int Bed);
}
