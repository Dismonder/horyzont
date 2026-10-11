using Horyzont.Server.Tests.Support;

namespace Horyzont.Server.Tests;

public sealed class TickPhaseTests
{
    [Fact]
    public async Task ServerRuntime_Participants_RunInPhaseOrder_StableWithinPhase()
    {
        await using var rig = new ServerRig();
        var calls = new List<string>();

        // Registered out of phase order; within a phase the registration order stays (K5-20 decision 8, README Y7).
        rig.Runtime.AddTickParticipant(new Recorder("compose-1", ServerTickPhase.Compose, calls));
        rig.Runtime.AddTickParticipant(new DefaultPhase("characters-1", calls));
        rig.Runtime.AddTickParticipant(new Recorder("frames-1", ServerTickPhase.Frames, calls));
        rig.Runtime.AddTickParticipant(new Recorder("characters-2", ServerTickPhase.Characters, calls));
        rig.Runtime.AddTickParticipant(new Recorder("compose-2", ServerTickPhase.Compose, calls));
        rig.Runtime.AddTickParticipant(new Recorder("frames-2", ServerTickPhase.Frames, calls));
        await rig.StartAsync();
        calls.Clear();

        rig.Step();
        rig.Step();

        string[] tick = ["frames-1", "frames-2", "characters-1", "characters-2", "compose-1", "compose-2"];
        Assert.Equal([.. tick, .. tick], calls);
        Assert.Equal(ServerTickPhase.Characters, ((IServerTickParticipant)new DefaultPhase("x", calls)).Phase);
        Assert.Equal(0, (int)ServerTickPhase.Frames);
        Assert.Equal(1, (int)ServerTickPhase.Characters);
        Assert.Equal(2, (int)ServerTickPhase.Compose);
        await rig.StopAsync();
    }

    private sealed class Recorder(string name, ServerTickPhase phase, List<string> calls) : IServerTickParticipant
    {
        public ServerTickPhase Phase => phase;

        public void OnTick(ServerRuntime runtime) => calls.Add(name);
    }

    private sealed class DefaultPhase(string name, List<string> calls) : IServerTickParticipant
    {
        public void OnTick(ServerRuntime runtime) => calls.Add(name);
    }
}
