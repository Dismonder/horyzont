using Horyzont.Client.Blocks;
using Horyzont.Core.BlockEntities;
using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Simulation;
using Horyzont.Testing;

using Microsoft.Extensions.Logging.Abstractions;

namespace Horyzont.Client.Tests.Blocks;

public sealed class SharedBlockEntitiesTests
{
    [Fact]
    public void TryGet_ReadsTheWorldStateOfTheServer()
    {
        var state = WorldFactory.CreateWithFunctionalBlocks();
        var view = new SharedBlockEntities(() => state);
        Assert.False(view.TryGet(FunctionalBlocks.Construct, FunctionalBlocks.BedCell, out _));

        var sim = new ServerSimulation(state, new FakeTerrainService(), new InMemoryJournal(), new GameRules(), NullLogger<ServerSimulation>.Instance);
        sim.ApplyServerOperation(new BlockEntitySetOp(FunctionalBlocks.Construct, FunctionalBlocks.BedCell, FunctionalBlocks.BedType, new BedBlockState(PlayerId.None)));

        Assert.True(view.TryGet(FunctionalBlocks.Construct, FunctionalBlocks.BedCell, out var bed));
        Assert.Equal(new BedBlockState(PlayerId.None), bed);
    }
}
