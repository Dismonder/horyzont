using Horyzont.Core.Checkpoints;
using Horyzont.Core.Frames;
using Horyzont.Core.Hashing;
using Horyzont.Core.Space;
using Horyzont.Core.World;
using Horyzont.Testing;

namespace Horyzont.Core.Tests.Hashing;

public sealed class StarSystemHashTests
{
    // Hash of WorldFactory.CreateDefault() recorded before K5-21 touched StateHasher (README Y14): a world without a star
    // system must hash byte for byte as before.
    private const ulong DefaultWorldHash = 17523897430694564880;

    [Fact]
    public void StateHasher_NoStarSystem_HashUnchanged()
    {
        var state = WorldFactory.CreateDefault();
        Assert.Null(state.StarSystem);
        Assert.Equal(DefaultWorldHash, StateHasher.Hash(state));
    }

    [Fact]
    public void StateHasher_StarSystemChange_ChangesHash()
    {
        var created = StarSystemGenesis.CreateWorldBodies(77, "core:test/1");
        var bodies = created.System.Bodies.ToArray();
        var far = bodies[3];
        bodies[3] = far with { Pose = new PoseD(far.Pose.Position + new Vector3D(0, 1, 0), far.Pose.Rotation) };
        var moved = created.System with { Bodies = bodies };

        var withSystem = StateHasher.Hash(Build(created, created.System));
        Assert.Equal(withSystem, StateHasher.Hash(Build(created, created.System)));
        Assert.NotEqual(withSystem, StateHasher.Hash(Build(created, null)));
        Assert.NotEqual(withSystem, StateHasher.Hash(Build(created, moved)));
        Assert.NotEqual(withSystem, StateHasher.Hash(Build(created, created.System with { SunCycleSeconds = 1801 })));
    }

    private static WorldState Build(GeneratedStarSystem created, StarSystemParams? system)
    {
        var builder = new WorldStateBuilder(WorldFactory.DefaultWorldId, 77);
        foreach (var planet in created.Bodies)
        {
            builder.AddPlanet(planet);
        }

        if (system is not null)
        {
            builder.SetStarSystem(system);
        }

        return builder.Build();
    }
}
