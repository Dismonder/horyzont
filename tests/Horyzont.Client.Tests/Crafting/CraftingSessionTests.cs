using Horyzont.Client.Crafting;
using Horyzont.Core.BlockEntities;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Simulation;
using Horyzont.Core.World.Blocks;
using Horyzont.Core.World.Constructs;

using Microsoft.Extensions.Logging.Abstractions;

namespace Horyzont.Client.Tests.Crafting;

public sealed class CraftingSessionTests
{
    private static readonly BlockRef _bench = new(new EntityId(100), new CellPos(4, 1, 0));

    private sealed class Rig
    {
        public Rig()
        {
            Fixture = new CraftingClientFixture();
            Session = new CraftingSession(Fixture.Sender, new GameRules(), NullLogger<CraftingSession>.Instance);
        }

        public CraftingClientFixture Fixture { get; }

        public CraftingSession Session { get; }

        public CraftOutcome Craft(string recipe, ushort count) => Session.Craft(Fixture.Recipe(recipe), count);

        public void Reply(RejectReason reason) => Session.OnIntentReply(new IntentReply(Fixture.Sender.LastSeq, reason, default));
    }

    [Fact]
    public void Craft_SendsIntentWithStation()
    {
        var rig = new Rig();
        var opened = 0;
        rig.Session.Opened += () => opened++;

        rig.Session.OpenStation(StationKind.Workbench, _bench);

        Assert.True(rig.Session.IsStationOpen);
        Assert.Equal(StationKind.Workbench, rig.Session.Kind);
        Assert.Equal(_bench, rig.Session.Station);
        Assert.Equal(1, opened);
        Assert.Equal(CraftOutcome.Sent, rig.Craft("t:bench1", 2));
        Assert.Equal(new CraftIntent("t:bench1", 2, _bench), Assert.Single(rig.Fixture.Sender.Intents));
        Assert.Equal(1, rig.Session.PendingCount);
    }

    [Fact]
    public void CraftHands_SendsWithoutStation()
    {
        var rig = new Rig();

        Assert.False(rig.Session.IsStationOpen);
        Assert.Equal(StationKind.Hands, rig.Session.Kind);
        Assert.Null(rig.Session.Station);
        Assert.Equal(CraftOutcome.Sent, rig.Craft("t:hand", 1));

        Assert.Equal(new CraftIntent("t:hand", 1, null), Assert.Single(rig.Fixture.Sender.Intents));
    }

    [Fact]
    public void Craft_RecipeOfAnotherStationKind_SendsNothing()
    {
        var rig = new Rig();
        Assert.Equal(CraftOutcome.WrongStation, rig.Craft("t:bench1", 1));

        rig.Session.OpenStation(StationKind.Workbench, _bench);
        Assert.Equal(CraftOutcome.WrongStation, rig.Craft("t:hand", 1));
        Assert.Equal(CraftOutcome.WrongStation, rig.Craft("t:smelt", 1));

        Assert.Empty(rig.Fixture.Sender.Intents);

        // The control: the recipe of the open station is sent.
        Assert.Equal(CraftOutcome.Sent, rig.Craft("t:bench2", 1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    public void Craft_CountOutsideTheRules_SendsNothing(ushort count)
    {
        var rig = new Rig();

        Assert.Equal(CraftOutcome.InvalidCount, rig.Craft("t:hand", count));

        Assert.Empty(rig.Fixture.Sender.Intents);
        Assert.Equal(CraftOutcome.Sent, rig.Craft("t:hand", 50));
    }

    [Fact]
    public void Close_ForgetsTheStation()
    {
        var rig = new Rig();
        var closed = 0;
        rig.Session.Closed += () => closed++;
        rig.Session.OpenStation(StationKind.Kitchen, _bench);

        rig.Session.Close();

        Assert.False(rig.Session.IsStationOpen);
        Assert.Equal(StationKind.Hands, rig.Session.Kind);
        Assert.Null(rig.Session.Station);
        Assert.Equal(1, closed);

        rig.Session.Close();
        Assert.Equal(1, closed);
    }

    [Fact]
    public void Reject45_ShowsLocked()
    {
        var rig = new Rig();
        var changed = 0;
        rig.Session.Changed += () => changed++;
        rig.Craft("t:hand", 1);
        Assert.False(rig.Session.ShowsLocked);

        rig.Reply(RejectReason.RecipeLocked);

        Assert.Equal(RejectReason.RecipeLocked, rig.Session.LastRefusal);
        Assert.True(rig.Session.ShowsLocked);
        Assert.Equal(0, rig.Session.PendingCount);
        Assert.True(changed > 0);

        // A reply to a craft that was accepted clears the refusal.
        rig.Craft("t:hand", 1);
        rig.Reply(RejectReason.None);
        Assert.False(rig.Session.ShowsLocked);
        Assert.Equal(RejectReason.None, rig.Session.LastRefusal);
    }

    [Fact]
    public void Reply_ToAnotherIntent_Ignored()
    {
        var rig = new Rig();
        rig.Craft("t:hand", 1);

        rig.Session.OnIntentReply(new IntentReply(rig.Fixture.Sender.LastSeq + 7, RejectReason.RecipeLocked, default));

        Assert.False(rig.Session.ShowsLocked);
        Assert.Equal(1, rig.Session.PendingCount);
    }

    [Fact]
    public void PendingFull_Waiting()
    {
        var rig = new Rig();
        for (var i = 0; i < CraftingSession.MaxPending; i++)
        {
            Assert.Equal(CraftOutcome.Sent, rig.Craft("t:hand", 1));
        }

        Assert.Equal(CraftOutcome.Waiting, rig.Craft("t:hand", 1));
        Assert.Equal(CraftingSession.MaxPending, rig.Fixture.Sender.Intents.Count);

        rig.Reply(RejectReason.None);

        Assert.Equal(CraftOutcome.Sent, rig.Craft("t:hand", 1));
    }

    [Fact]
    public void CannotSend_SendsNothing()
    {
        var rig = new Rig();
        rig.Fixture.Sender.CanSend = false;

        Assert.Equal(CraftOutcome.CannotSend, rig.Craft("t:hand", 1));

        Assert.Empty(rig.Fixture.Sender.Intents);
        Assert.Equal(0, rig.Session.PendingCount);
    }

    [Fact]
    public void Disconnect_ClosesTheStationAndForgetsPending()
    {
        var rig = new Rig();
        var closed = 0;
        rig.Session.Closed += () => closed++;
        rig.Session.OpenStation(StationKind.Fabricator, _bench);
        rig.Craft("t:fab", 1);

        rig.Session.OnDisconnected(DisconnectCode.Normal);

        Assert.False(rig.Session.IsStationOpen);
        Assert.Equal(0, rig.Session.PendingCount);
        Assert.Equal(RejectReason.None, rig.Session.LastRefusal);
        Assert.Equal(1, closed);
    }
}
