using Horyzont.Core.Intents;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Testing;

namespace Horyzont.Server.Tests.Survival;

/// <summary>
/// The food reaches the player through <see cref="Horyzont.Server.Crafting.ProgressReplication"/> (K6-33 D6, step C4): the compound of eating contains the operation 41,
/// which <c>OperationProgress</c> recognizes, so the owner gets message 48 with the food slots and nobody else does.
/// </summary>
public sealed class EatReplicationTests
{
    private readonly FoodServerRig _rig = new();

    [Fact]
    public void Eat_SendsProgressStateWithFood_ToOwnerOnly()
    {
        _rig.Progress.OnPlayerReady(_rig.Alice);
        _rig.Progress.OnPlayerReady(_rig.Bob);
        _rig.Sim.ApplyServerOperation(new ItemCreateOp(new SlotRef(WorldFactory.BagOf(1), 0), FoodServerRig.Berries, 2, 20, ItemSource.Admin));
        _rig.Sent.Clear();
        var now = _rig.Sim.State.CurrentTick;

        var result = _rig.Sim.Submit(_rig.Alice, new EatIntent(WorldFactory.BagOf(1), 0));

        Assert.True(result.IsAccepted);
        var state = Assert.Single(_rig.Sent.Where(s => s.Player == _rig.Alice).Select(s => s.Message).OfType<PlayerProgressState>());
        Assert.Equal(FoodServerRig.Berries, state.Food[0].Type);
        Assert.Equal(now, state.Food[0].Eaten);
        Assert.True(state.Food[1].IsEmpty);
        Assert.DoesNotContain(_rig.Sent, s => s.Player == _rig.Bob);
    }
}
