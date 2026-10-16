using Horyzont.Core.BlockEntities;
using Horyzont.Core.Ids;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Tests.Crafting;
using Horyzont.Core.Tests.Survival;

using static Horyzont.Core.Tests.Survival.HazardWorld;

namespace Horyzont.Core.Tests.BlockEntities;

/// <summary>K6-34, the oxygen generator state (kind 5) in operation 40 and the compounds that place and remove it (D12). Every refusal starts from the accepted counterpart.</summary>
public sealed class GeneratorApplierTests
{
    [Fact]
    public void PlaceCompound_CreatesInputAndState()
    {
        var f = new HazardWorld();

        f.Apply(f.PlaceGenerator(GeneratorCell, inputSlots: 3));

        var state = f.GeneratorAt(GeneratorCell);
        Assert.NotNull(state);
        Assert.Null(state.Started);
        var input = f.Blocks.Container(state.Input);
        Assert.Equal(ContainerKind.StationInput, input.Kind);
        Assert.Equal(ContainerHost.ConstructBlock(FunctionalBlockFixture.ConstructId, GeneratorCell), input.Host);
        Assert.Equal(ContainerAccess.Shared, input.Access);
        Assert.Equal(3, input.Size);
        Assert.Single(f.Blocks.HostedBy(GeneratorCell));
        Assert.False(f.Blocks.BlockAt(GeneratorCell).IsEmpty);
        Assert.Equal(1, f.State.BlockEntities.CountOf(FunctionalBlockFixture.ConstructId)); // the generator is the only state of this construct
    }

    [Fact]
    public void State_StartAndStop_AreOrdinaryUpdates()
    {
        var f = new HazardWorld();
        var input = f.SetUpGenerator(GeneratorCell);

        f.Apply(GeneratorState(GeneratorCell, input, new Tick(100)));
        Assert.Equal(new OxygenGeneratorBlockState(input, new Tick(100)), f.GeneratorAt(GeneratorCell));

        f.Apply(GeneratorState(GeneratorCell, input, new Tick(0)));
        Assert.Equal(new OxygenGeneratorBlockState(input, new Tick(0)), f.GeneratorAt(GeneratorCell));

        f.Apply(GeneratorState(GeneratorCell, input));
        Assert.Equal(new OxygenGeneratorBlockState(input, null), f.GeneratorAt(GeneratorCell));
    }

    [Fact]
    public void State_InputOfOtherBlock_Fails()
    {
        var f = new HazardWorld();
        var input = f.SetUpGenerator(GeneratorCell);
        var input2 = f.SetUpGenerator(GeneratorCell2);

        // The accepted counterpart: its own input. Then the input of the other generator, a missing id, a container of the player and a chest elsewhere.
        f.Apply(GeneratorState(GeneratorCell, input, new Tick(100)));
        f.AssertFails(GeneratorState(GeneratorCell, input2, new Tick(100)));
        f.AssertFails(GeneratorState(GeneratorCell, new ContainerId(9999)));
        f.AssertFails(GeneratorState(GeneratorCell, FunctionalBlockFixture.Bag));
        Assert.Equal(new OxygenGeneratorBlockState(input, new Tick(100)), f.GeneratorAt(GeneratorCell));
    }

    [Fact]
    public void State_InputNotStationInput_Fails()
    {
        var f = new HazardWorld();
        var input = f.SetUpGenerator(GeneratorCell);
        var chest = f.Blocks.SetUpChestAt(GeneratorCell2, Player1);

        // The accepted counterpart is its own StationInput; a chest container (even one hosted by another block) is not an input.
        f.Apply(GeneratorState(GeneratorCell, input));
        f.AssertFails(GeneratorState(GeneratorCell, chest));
        f.AssertFails(GeneratorState(GeneratorCell, FunctionalBlockFixture.Hotbar));
        Assert.Equal(input, f.GeneratorAt(GeneratorCell)!.Input);
    }

    [Fact]
    public void State_WithOutputContainer_Fails()
    {
        // The accepted counterpart: a generator has only the input.
        var accepted = new HazardWorld();
        accepted.Apply(accepted.PlaceGenerator(GeneratorCell));

        // A StationOutput container in the cell of a generator is refused: the generator has no output (D12), however the compound is ordered.
        var f = new HazardWorld();
        var input = f.State.ContainerIds.PeekNext();
        var output = new ContainerId(input.Value + 1);
        f.AssertFails(new CompoundOp(
        [
            InputContainer(input, GeneratorCell),
            InputContainer(output, GeneratorCell, kind: ContainerKind.StationOutput),
            GeneratorState(GeneratorCell, input),
            FunctionalBlockFixture.PlaceBlock(GeneratorCell, GeneratorBlockType),
        ]));

        // A generator that gets an output container later: the end-of-operation check of the cell refuses it as well.
        f.Apply(f.PlaceGenerator(GeneratorCell));
        f.AssertFails(InputContainer(f.State.ContainerIds.PeekNext(), GeneratorCell, kind: ContainerKind.StationOutput));
        Assert.Single(f.Blocks.HostedBy(GeneratorCell));
    }

    [Fact]
    public void State_SecondContainerInCell_Fails()
    {
        var f = new HazardWorld();
        f.Apply(f.PlaceGenerator(GeneratorCell));
        foreach (var kind in new[] { ContainerKind.StationInput, ContainerKind.StationOutput })
        {
            f.AssertFails(InputContainer(f.State.ContainerIds.PeekNext(), GeneratorCell, kind: kind));
            Assert.Single(f.Blocks.HostedBy(GeneratorCell));
        }
    }

    [Fact]
    public void InputWithoutState_Fails()
    {
        var f = new HazardWorld();

        // The accepted counterpart: the full placement.
        f.Apply(f.PlaceGenerator(GeneratorCell));

        // A StationInput container needs the state of a station or of a generator in its cell (the check at the end of the operation covers the touched cells).
        f.Blocks.EnsureBlock(GeneratorCell2, GeneratorBlockType);
        f.AssertFails(InputContainer(f.State.ContainerIds.PeekNext(), GeneratorCell2));
        f.AssertFails(new CompoundOp([InputContainer(f.State.ContainerIds.PeekNext(), GeneratorCell2)]));
        Assert.Empty(f.Blocks.HostedBy(GeneratorCell2));
    }

    [Fact]
    public void RemoveCompound_EmptyInput_Removes()
    {
        var f = new HazardWorld();
        var input = f.SetUpGenerator(GeneratorCell, new Tick(100));

        f.Apply(RemoveGenerator(GeneratorCell, input));

        Assert.Empty(f.Blocks.HostedBy(GeneratorCell));
        Assert.False(f.Blocks.HasState(GeneratorCell, out _));
        Assert.True(f.Blocks.BlockAt(GeneratorCell).IsEmpty);
        Assert.Equal(0, f.State.BlockEntities.Count);
    }

    [Fact]
    public void RemoveCompound_NonEmptyInput_Fails()
    {
        // The accepted counterpart: the same removal with an empty input (above). A fuel cell in the input keeps the container, and so the generator.
        var f = new HazardWorld();
        var input = f.SetUpGenerator(GeneratorCell);
        f.FeedGenerator(input, 0, 1);

        f.AssertFails(RemoveGenerator(GeneratorCell, input));
        f.AssertFails(new ContainerRemoveOp(input));
        f.AssertFails(FunctionalBlockFixture.ClearState(GeneratorCell, GeneratorBlockType));
        f.AssertFails(new BlockClearOp(FunctionalBlockFixture.ConstructId, GeneratorCell));
        Assert.NotNull(f.GeneratorAt(GeneratorCell));
    }

    [Fact]
    public void KindChange_StationToGenerator_Fails()
    {
        var f = new CraftingFixture();
        var (input, _) = f.SetUpStation(CraftingFixture.StationCell);

        // A station keeps its kind: it does not turn into a generator (and the other way round).
        f.AssertFails(new BlockEntitySetOp(FunctionalBlockFixture.ConstructId, CraftingFixture.StationCell, CraftingFixture.StationBlockType, new OxygenGeneratorBlockState(input, null)));

        var g = new HazardWorld();
        var generatorInput = g.SetUpGenerator(GeneratorCell);
        g.AssertFails(new BlockEntitySetOp(FunctionalBlockFixture.ConstructId, GeneratorCell, GeneratorBlockType, new StationBlockState(generatorInput, generatorInput, string.Empty, null)));
        g.AssertFails(new BlockEntitySetOp(FunctionalBlockFixture.ConstructId, GeneratorCell, GeneratorBlockType, new BedBlockState(Player1)));
        Assert.Equal(new OxygenGeneratorBlockState(generatorInput, null), g.GeneratorAt(GeneratorCell));
    }

    [Fact]
    public void Store_GeneratorsIndex_SetRemoveLoadUndo()
    {
        var f = new HazardWorld();
        Assert.Empty(f.State.BlockEntities.Generators);

        // Set: the generators are listed in the order of the block references; a bed and a chest are not generators.
        var input2 = f.SetUpGenerator(GeneratorCell2);
        var input1 = f.SetUpGenerator(GeneratorCell);
        f.Blocks.SetUpBed(Horyzont.Testing.FunctionalBlocks.BedCell, Player1);
        var expected = new[] { new BlockRef(FunctionalBlockFixture.ConstructId, GeneratorCell), new BlockRef(FunctionalBlockFixture.ConstructId, GeneratorCell2) }.Order().ToArray();
        Assert.Equal(expected, f.State.BlockEntities.Generators);

        // Remove: the index follows.
        f.Apply(RemoveGenerator(GeneratorCell2, input2));
        Assert.Equal([new BlockRef(FunctionalBlockFixture.ConstructId, GeneratorCell)], f.State.BlockEntities.Generators);

        // Undo: a compound that removes the generator and then fails leaves the index as it was; so does a placement that fails after the state was set.
        f.AssertFails(new CompoundOp([.. ((CompoundOp)RemoveGenerator(GeneratorCell, input1)).Components, new ItemDestroyOp(BagSlot(Player1, 0), Stone, 1, ItemSink.Admin)]));
        Assert.Equal([new BlockRef(FunctionalBlockFixture.ConstructId, GeneratorCell)], f.State.BlockEntities.Generators);
        f.AssertFails(new CompoundOp([.. ((CompoundOp)f.PlaceGenerator(GeneratorCell2)).Components, new ItemDestroyOp(BagSlot(Player1, 0), Stone, 1, ItemSink.Admin)]));
        Assert.Equal([new BlockRef(FunctionalBlockFixture.ConstructId, GeneratorCell)], f.State.BlockEntities.Generators);
    }
}
