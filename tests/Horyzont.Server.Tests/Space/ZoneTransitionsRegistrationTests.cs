using Horyzont.Server.Space;
using Horyzont.Server.Tests.Support;

using Microsoft.Extensions.Logging.Abstractions;

namespace Horyzont.Server.Tests.Space;

public sealed class ZoneTransitionsRegistrationTests
{
    [Fact]
    public async Task RegisterFramesPhase_ZoneTransitionsThenFrameNodes_BeforeCharacters()
    {
        await using var rig = new ServerRig();
        var characters = new Marker(ServerTickPhase.Characters);
        var flight = new Marker(ServerTickPhase.Frames);
        rig.Runtime.AddTickParticipant(characters);

        // K5-24 registers the flight of ships first, so it runs before the zone transitions.
        rig.Runtime.AddTickParticipant(flight);
        var zones = new ServerZoneTransitions(() => null, 30, NullLogger<ServerZoneTransitions>.Instance);
        var nodes = new Marker(ServerTickPhase.Frames);
        ServerZoneTransitions.RegisterFramesPhase(rig.Runtime, zones, nodes);

        IServerTickParticipant[] expected = [flight, zones, nodes, characters];
        Assert.Equal(expected, rig.Runtime.TickParticipants.Where(expected.Contains));
    }

    [Fact]
    public async Task RegisterFramesPhase_FrameNodesOutsidePhaseFrames_Throws()
    {
        await using var rig = new ServerRig();
        var zones = new ServerZoneTransitions(() => null, 30, NullLogger<ServerZoneTransitions>.Instance);

        Assert.Throws<ArgumentException>(() => ServerZoneTransitions.RegisterFramesPhase(rig.Runtime, zones, new Marker(ServerTickPhase.Compose)));
    }

    private sealed class Marker(ServerTickPhase phase) : IServerTickParticipant
    {
        public ServerTickPhase Phase => phase;

        public void OnTick(ServerRuntime runtime)
        {
        }
    }
}
