using System.Buffers.Binary;
using System.IO.Hashing;

using GdUnit4;

using Horyzont.Core.Ids;
using Horyzont.Core.Props;
using Horyzont.Game.Tests.Support;

using static GdUnit4.Assertions;

namespace Horyzont.Game.Tests.Planet;

/// <summary>
/// K6-31 C5: the props of the real generator <c>core:planet/1</c> in two regions, found by the same <see cref="PropField"/> the server uses.
/// A changed constant means a changed generator or a changed layout V1 (both frozen, ZASADY 3): every harvested prop of an existing world
/// would point at another place.
/// </summary>
[TestSuite]
public class PropFieldDigestTests
{
    private const ulong Seed = 21;

    // Written at step C5 (generator core:planet/1, layout V1, seed 21, home planet, regions (face 0, 5, 7) and (face 3, 40, 12)).
    private const ulong ExpectedDigest = 554016997098247071UL;
    private const int ExpectedCount = 78;

    [TestCase(Timeout = 300000)]
    [RequireGodotRuntime]
    public async Task PlanetPropField_GoldenDigest()
    {
        await using var scene = EngineTestScene.Create();
        await using var world = GeneratedWorld.Create(scene, Seed);
        var field = new PropField(world.State, world.Terrain.Service);
        var hash = new XxHash64();
        var count = 0;
        var entry = new byte[17];
        foreach (var (face, regionU, regionV) in new[] { (0, 5, 7), (3, 40, 12) })
        {
            for (var u = 0; u < 8; u++)
            {
                for (var v = 0; v < 8; v++)
                {
                    for (var slot = 0; slot < 4; slot++)
                    {
                        var id = PropId.Create(face, (regionU * 8) + u, (regionV * 8) + v, slot);
                        if (!field.TryGetProp(world.Home.Id, id, out var prop))
                        {
                            continue;
                        }

                        // The anchor to a millimetre: the float of the sampler may differ in the last bits between machines, the layout may not.
                        BinaryPrimitives.WriteUInt64LittleEndian(entry, id.Value);
                        entry[8] = (byte)prop.Kind;
                        BinaryPrimitives.WriteInt32LittleEndian(entry.AsSpan(9), (int)MathF.Round(prop.Anchor.Length() * 1000f));
                        BinaryPrimitives.WriteInt32LittleEndian(entry.AsSpan(13), (int)MathF.Round(prop.Scale * 1000f));
                        hash.Append(entry);
                        count++;
                    }
                }
            }
        }

        var digest = hash.GetCurrentHashAsUInt64();
        Godot.GD.Print($"K6-31 PlanetPropField digest {digest}UL, {count} props");
        AssertThat(count).IsEqual(ExpectedCount);
        AssertThat(digest).IsEqual(ExpectedDigest);
    }
}
