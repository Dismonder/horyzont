using GdUnit4;
using Horyzont.Client.Replication;
using Horyzont.Client.Tools;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Simulation;
using Horyzont.Core.World.Constructs;
using Horyzont.Game.Client.Tools;
using Horyzont.Game.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using static GdUnit4.Assertions;

namespace Horyzont.Game.Tests.Tools;

[TestSuite]
public class ToolSeamTests
{
    [TestCase(Timeout = 300000)]
    [RequireGodotRuntime]
    public async Task OnIntentReply_Observer_SeesReplyBeforeTrackerCommits()
    {
        await using var scene = EngineTestScene.Create();
        var rig = await ToolRig.StartSoloAsync(scene, ModeFixture.NewDirectory(), Guid.NewGuid());
        using var tools = new ToolNode();
        var prediction = new Prediction();
        tools.Initialize(rig.Composition.Client!, hello => new RemoteToolWorldView(hello, NoRemoteTerrain.Instance),
            () => prediction, rig.Player, new GameRules(), NullLoggerFactory.Instance);
        tools.OnServerHello(rig.Composition.Client!.Hello!);
        prediction.Pending = tools.Pending!;
        tools.Pending!.Add(1, new RemoveBlockIntent(new EntityId(1), default(CellPos)), default, 0);
        tools.OnIntentReply(new IntentReply(1, RejectReason.None, new OpId(1)));
        AssertThat(string.Join(",", prediction.Calls)).IsEqual("reply:1,commit");
        AssertThat(tools.Pending.Count).IsEqual(0);
        tools.OnIntentReply(new IntentReply(1, RejectReason.None, new OpId(1)));
        AssertThat(string.Join(",", prediction.Calls)).IsEqual("reply:1,commit,reply:0");
        tools.OnDisconnected(Horyzont.Core.Protocol.DisconnectCode.HostLeft);
        AssertThat(await rig.Fixture.StopAsync()).IsTrue();
    }

    private sealed class Prediction : IEditPrediction, IIntentReplyObserver
    {
        public PendingIntentTracker Pending { get; set; } = null!;
        public List<string> Calls { get; } = [];
        public void NoteReply(IntentReply reply) => Calls.Add($"reply:{Pending.Count}");
        public void Begin(uint clientSeq, Intent intent, in ToolPreview preview) { }
        public void Commit(uint clientSeq) => Calls.Add("commit");
        public void Revert(uint clientSeq) => Calls.Add("revert");
    }
}
