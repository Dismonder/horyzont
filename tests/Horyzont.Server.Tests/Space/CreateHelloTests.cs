using Horyzont.Core.Ids;
using Horyzont.Core.Simulation;
using Horyzont.Core.World;
using Horyzont.Testing;

using Microsoft.Extensions.Logging.Abstractions;

namespace Horyzont.Server.Tests.Space;

public sealed class CreateHelloTests
{
    [Fact]
    public void CreateHello_HomePlanetFirst()
    {
        // A seed whose registry does not start with the home planet (K5-21 decision 10).
        WorldState? state = null;
        for (ulong seed = 0; state is null; seed++)
        {
            var candidate = WorldFactory.CreateWithStarSystem(seed);
            if (candidate.Planets.All[0].Id != candidate.HomePlanet)
            {
                state = candidate;
            }
        }

        var sim = new ServerSimulation(state, new FakeTerrainService(), new InMemoryJournal(), new GameRules(), NullLogger<ServerSimulation>.Instance);
        var hooks = new GameServerHooks(sim, () => true, NullLogger<GameServerHooks>.Instance);
        var player = hooks.OnPlayerJoining(new PlatformIdentity(IdentityPlatform.Dev, 1), "alice");

        var hello = hooks.CreateHello(player);

        Assert.Equal(state.HomePlanet, hello.Planets[0].Id);
        Assert.Equal(4, hello.Planets.Count);
        var rest = hello.Planets.Skip(1).Select(p => p.Id.Value).ToList();
        Assert.Equal(rest.Order(), rest);
        Assert.Equal(state.Planets.All.OrderBy(p => p.Id.Value), hello.Planets.OrderBy(p => p.Id.Value));
    }
}
