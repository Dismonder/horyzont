using Horyzont.Core.BlockEntities;
using Horyzont.Core.Ids;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Tests.BlockEntities;

using static Horyzont.Core.Tests.BlockEntities.FunctionalBlockFixture;

namespace Horyzont.Core.Tests.Crafting;

/// <summary>K6-32, the station state in operation 40 and the compounds that place and remove a station (D18, D19). Every refusal starts from the accepted counterpart.</summary>
public sealed class StationStateOpsTests
{
    private static readonly Tick _start = new(100);

    [Fact]
    public void BlockEntitySet_StationWithOwnContainers_Stores()
    {
        var f = new CraftingFixture();
        var (input, output) = f.SetUpStation(CraftingFixture.StationCell);

        f.Apply(CraftingFixture.StationState(CraftingFixture.StationCell, input, output, "t:a", _start));

        Assert.True(f.Blocks.HasState(CraftingFixture.StationCell, out var state));
        Assert.Equal(new StationBlockState(input, output, "t:a", _start), state);

        // Back to idle without a recipe.
        f.Apply(CraftingFixture.StationState(CraftingFixture.StationCell, input, output));
        Assert.True(f.Blocks.HasState(CraftingFixture.StationCell, out var idle));
        Assert.Equal(new StationBlockState(input, output, string.Empty, null), idle);
    }

    [Fact]
    public void BlockEntitySet_StationWithForeignInput_Fails()
    {
        var f = new CraftingFixture();
        var (input, output) = f.SetUpStation(CraftingFixture.StationCell);
        var (input2, _) = f.SetUpStation(CraftingFixture.StationCell2);

        // The accepted counterpart: its own containers. Then the input of another station, the output in the input place, a missing id and a container of the player.
        f.Apply(CraftingFixture.StationState(CraftingFixture.StationCell, input, output, "t:a"));
        f.AssertFails(CraftingFixture.StationState(CraftingFixture.StationCell, input2, output, "t:a"));
        f.AssertFails(CraftingFixture.StationState(CraftingFixture.StationCell, output, output, "t:a"));
        f.AssertFails(CraftingFixture.StationState(CraftingFixture.StationCell, new ContainerId(9999), output, "t:a"));
        f.AssertFails(CraftingFixture.StationState(CraftingFixture.StationCell, Bag, output, "t:a"));
    }

    [Fact]
    public void BlockEntitySet_StationOutputOfWrongKind_Fails()
    {
        var f = new CraftingFixture();
        var (input, output) = f.SetUpStation(CraftingFixture.StationCell);
        var chestAtStation = f.Blocks.CreateChestContainer(CraftingFixture.StationCell);

        f.Apply(CraftingFixture.StationState(CraftingFixture.StationCell, input, output, "t:a"));
        f.AssertFails(CraftingFixture.StationState(CraftingFixture.StationCell, input, chestAtStation, "t:a"));
        f.AssertFails(CraftingFixture.StationState(CraftingFixture.StationCell, output, input, "t:a"));
    }

    [Fact]
    public void BlockEntitySet_StationRecipeOver64Bytes_Fails()
    {
        var f = new CraftingFixture();
        var (input, output) = f.SetUpStation(CraftingFixture.StationCell);

        f.Apply(CraftingFixture.StationState(CraftingFixture.StationCell, input, output, new string('r', 64)));
        f.AssertFails(CraftingFixture.StationState(CraftingFixture.StationCell, input, output, new string('r', 65)));
    }

    [Fact]
    public void BlockEntitySet_StationStartedWithoutRecipe_Fails()
    {
        var f = new CraftingFixture();
        var (input, output) = f.SetUpStation(CraftingFixture.StationCell);

        // Started != null requires a recipe (review A1, 4-5).
        f.Apply(CraftingFixture.StationState(CraftingFixture.StationCell, input, output, "t:a", _start));
        f.AssertFails(CraftingFixture.StationState(CraftingFixture.StationCell, input, output, string.Empty, _start));
    }

    [Fact]
    public void BlockEntitySet_StationRecipeLoneSurrogate_Fails()
    {
        var f = new CraftingFixture();
        var (input, output) = f.SetUpStation(CraftingFixture.StationCell);

        // The recipe key must be strict UTF-8 (the journal writes it with ByteWriter, which refuses a lone surrogate). ContentLoader rejects such a key in B3.
        f.Apply(CraftingFixture.StationState(CraftingFixture.StationCell, input, output, "t:\U0001F600"));
        f.AssertFails(CraftingFixture.StationState(CraftingFixture.StationCell, input, output, "t:\uD800"));
    }

    [Fact]
    public void ContainerCreate_StationContainerWithoutState_Fails()
    {
        var f = new CraftingFixture();

        // The accepted counterpart: the full placement of a station.
        f.Apply(f.PlaceStation(CraftingFixture.StationCell));

        // The applier checks the cells an operation touched at its end (review A1, 4-3): a station container needs the station state of its cell.
        f.Blocks.EnsureBlock(CraftingFixture.StationCell2, CraftingFixture.StationBlockType);
        var input = f.State.ContainerIds.PeekNext();
        f.AssertFails(CraftingFixture.StationContainer(input, ContainerKind.StationInput, CraftingFixture.StationCell2));
        f.AssertFails(new CompoundOp(
        [
            CraftingFixture.StationContainer(input, ContainerKind.StationInput, CraftingFixture.StationCell2),
            CraftingFixture.StationContainer(new ContainerId(input.Value + 1), ContainerKind.StationOutput, CraftingFixture.StationCell2),
        ]));
        Assert.Empty(f.Blocks.HostedBy(CraftingFixture.StationCell2));
    }

    [Fact]
    public void ContainerRemove_StationContainerWithState_Fails()
    {
        var f = new CraftingFixture();
        var (input, output) = f.SetUpStation(CraftingFixture.StationCell);
        var (input2, output2) = f.SetUpStation(CraftingFixture.StationCell2);

        // The accepted counterpart: the whole removal of the second station.
        f.Apply(CraftingFixture.RemoveStation(CraftingFixture.StationCell2, input2, output2));

        f.AssertFails(new ContainerRemoveOp(input));
        f.AssertFails(new ContainerRemoveOp(output));
        Assert.Equal(2, f.Blocks.HostedBy(CraftingFixture.StationCell).Count());
    }

    [Fact]
    public void BlockEntitySet_ClearStationKeepingContainers_Fails()
    {
        var f = new CraftingFixture();
        var (input, output) = f.SetUpStation(CraftingFixture.StationCell);
        var (input2, output2) = f.SetUpStation(CraftingFixture.StationCell2);

        // The accepted counterpart: the state is cleared together with the containers.
        f.Apply(CraftingFixture.RemoveStation(CraftingFixture.StationCell2, input2, output2));

        f.AssertFails(FunctionalBlockFixture.ClearState(CraftingFixture.StationCell, CraftingFixture.StationBlockType));
        Assert.True(f.Blocks.HasState(CraftingFixture.StationCell, out var state));
        Assert.Equal(new StationBlockState(input, output, string.Empty, null), state);
    }

    [Fact]
    public void BlockEntitySet_StationKindChange_Fails()
    {
        var f = new CraftingFixture();
        f.SetUpStation(CraftingFixture.StationCell);

        // A station keeps its kind.
        f.AssertFails(new BlockEntitySetOp(ConstructId, CraftingFixture.StationCell, CraftingFixture.StationBlockType, new BedBlockState(Player1)));
    }

    [Fact]
    public void PlaceFurnace_Compound_TwoContainersStateAndBlock()
    {
        var f = new CraftingFixture();
        var version = f.Blocks.ConstructVersion;
        var first = f.State.ContainerIds.PeekNext();

        var applied = f.Apply(f.PlaceStation(CraftingFixture.StationCell, inputSlots: 4, outputSlots: 2));

        var input = f.Blocks.Container(first);
        var output = f.Blocks.Container(new ContainerId(first.Value + 1));
        Assert.Equal(ContainerKind.StationInput, input.Kind);
        Assert.Equal(ContainerKind.StationOutput, output.Kind);
        Assert.Equal(4, input.Size);
        Assert.Equal(2, output.Size);
        Assert.Equal(ContainerHost.ConstructBlock(ConstructId, CraftingFixture.StationCell), input.Host);
        Assert.Equal(input.Host, output.Host);
        Assert.Equal(ContainerAccess.Shared, input.Access);
        Assert.True(f.Blocks.HasState(CraftingFixture.StationCell, out var state));
        Assert.Equal(new StationBlockState(first, new ContainerId(first.Value + 1), string.Empty, null), state);
        Assert.Equal(CraftingFixture.StationBlockType, f.Blocks.BlockAt(CraftingFixture.StationCell).TypeId);
        Assert.Equal(version + 1, f.Blocks.ConstructVersion);
        Assert.Equal(2, applied.ContainerChanges.Count);
        Assert.Equal(first.Value + 2, f.State.ContainerIds.NextValue);
    }

    [Fact]
    public void RemoveFurnace_Compound_ClearsAll()
    {
        var f = new CraftingFixture();
        var (input, output) = f.SetUpStation(CraftingFixture.StationCell);
        Assert.Equal(2, f.Blocks.HostedBy(CraftingFixture.StationCell).Count());

        f.Apply(CraftingFixture.RemoveStation(CraftingFixture.StationCell, input, output));

        Assert.Empty(f.Blocks.HostedBy(CraftingFixture.StationCell));
        Assert.False(f.Blocks.HasState(CraftingFixture.StationCell, out _));
        Assert.True(f.Blocks.BlockAt(CraftingFixture.StationCell).IsEmpty);
        Assert.Equal(0, f.State.BlockEntities.Count);
    }

    [Fact]
    public void BlockClear_FurnaceWithState_Fails()
    {
        var accepted = new CraftingFixture();
        var (acceptedInput, acceptedOutput) = accepted.SetUpStation(CraftingFixture.StationCell);
        accepted.Apply(CraftingFixture.RemoveStation(CraftingFixture.StationCell, acceptedInput, acceptedOutput));

        // The condition of K6-30 D13 holds for the new kind: a block with a state and with containers is not cleared alone.
        var f = new CraftingFixture();
        var (input, output) = f.SetUpStation(CraftingFixture.StationCell);
        f.AssertFails(new BlockClearOp(ConstructId, CraftingFixture.StationCell));
        f.AssertFails(new CompoundOp([FunctionalBlockFixture.ClearState(CraftingFixture.StationCell, CraftingFixture.StationBlockType), new BlockClearOp(ConstructId, CraftingFixture.StationCell)]));
        Assert.Equal(2, f.Blocks.HostedBy(CraftingFixture.StationCell).Count());
        Assert.True(f.Blocks.HasState(CraftingFixture.StationCell, out _));
        Assert.NotEqual(input, output);
    }
}
