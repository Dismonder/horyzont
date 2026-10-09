
using FsCheck.Xunit;

using Horyzont.Client.Tools;
using Horyzont.Core.Ids;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Simulation;
using Horyzont.Core.World.Blocks;
using Horyzont.Core.World.Constructs;
using Horyzont.Testing;

namespace Horyzont.Client.Tests.Tools;

public sealed class ToolSettingsAndPaletteTests
{
    private static ServerHello Hello(RegistryEntry[] blocks, RegistryEntry[] materials) =>
        new(WorldFactory.DefaultWorldId, new Tick(0), 60, new PlayerId(1), new EntityId(1), [], blocks, materials);

    [Fact]
    public void ToolSettings_RadiusSteps_ClampedHalfToThreeInQuarterSteps()
    {
        var rules = new GameRules();
        var settings = new ToolSettings(ToolKind.TerrainManipulator, 1f, 1, 1, 0);

        Assert.Equal(1.25f, settings.WithRadiusSteps(1, rules).Radius);
        Assert.Equal(0.75f, settings.WithRadiusSteps(-1, rules).Radius);
        Assert.Equal(0.5f, settings.WithRadiusSteps(-2, rules).Radius);
        Assert.Equal(0.5f, settings.WithRadiusSteps(-100, rules).Radius);
        Assert.Equal(3f, settings.WithRadiusSteps(8, rules).Radius);
        Assert.Equal(3f, settings.WithRadiusSteps(100, rules).Radius);
        var reached = new List<float>();
        for (var s = settings.WithRadiusSteps(-10, rules); s.Radius < 3f; s = s.WithRadiusSteps(1, rules))
        {
            reached.Add(s.Radius);
        }

        Assert.Equal([0.5f, 0.75f, 1f, 1.25f, 1.5f, 1.75f, 2f, 2.25f, 2.5f, 2.75f], reached);
    }

    [Fact]
    public void BlockPalette_FromHello_ExcludesMissingOrderedById()
    {
        var hello = Hello(
            [new RegistryEntry(3, "core:glass"), new RegistryEntry(5, BlockTypeRegistry.MissingKey), new RegistryEntry(1, "core:stone_block"), new RegistryEntry(2, "core:planks")],
            []);

        var palette = BlockPalette.FromHello(hello);

        Assert.Equal<ushort>([1, 2, 3], palette.Entries.Select(e => e.Id).ToArray());
        Assert.DoesNotContain(palette.Entries, e => e.Key == BlockTypeRegistry.MissingKey);
        Assert.Equal((ushort)1, palette.First());
        Assert.Equal((ushort)3, palette.Step(1, -1));
        Assert.Equal((ushort)1, palette.Step(3, 1));
        Assert.Equal((ushort)2, palette.Step(1, 4));
    }

    [Fact]
    public void MaterialPalette_FromHello_AppliesExclusions()
    {
        var materials = new[] { new RegistryEntry(2, "core:dirt"), new RegistryEntry(5, "core:bedrock"), new RegistryEntry(1, "core:stone"), new RegistryEntry(4, "core:ore") };
        var hello = Hello([], materials);

        var byDefault = MaterialPalette.FromHello(hello);
        var custom = MaterialPalette.FromHello(hello, ["core:ore", "core:bedrock"]);
        var none = MaterialPalette.FromHello(hello, []);

        Assert.Equal<ushort>([1, 2, 4], byDefault.Entries.Select(e => e.Id).ToArray());
        Assert.Equal<ushort>([1, 2], custom.Entries.Select(e => e.Id).ToArray());
        Assert.Equal<ushort>([1, 2, 4, 5], none.Entries.Select(e => e.Id).ToArray());
        Assert.Equal((byte)2, byDefault.Step(1, 1));
        Assert.Equal((byte)1, byDefault.Step(4, 1));
    }

    [Fact]
    public void Palettes_Empty_StepReturnsZero()
    {
        var palette = new BlockPalette([]);
        var materials = new MaterialPalette([]);

        Assert.Equal((ushort)0, palette.Step(0, 3));
        Assert.Equal((byte)0, materials.Step(0, 3));
        Assert.Equal((ushort)0, palette.First());
        Assert.Equal((byte)0, materials.First());
    }

    [Property(MaxTest = 200)]
    public bool OrientationPicker_FourYawSteps_ReturnsToStart(byte start)
    {
        var orientation = (byte)(start % BlockOrientation.Count);
        var turned = orientation;
        for (var i = 0; i < 4; i++)
        {
            turned = OrientationPicker.Yaw(turned, 1);
        }

        return turned == orientation && OrientationPicker.Up(orientation, 6) == orientation;
    }

    [Fact]
    public void OrientationPicker_AllTwentyFourReachable()
    {
        var seen = new HashSet<byte>();
        for (var up = 0; up < 6; up++)
        {
            var orientation = OrientationPicker.Up(BlockOrientation.Identity, up);
            for (var yaw = 0; yaw < 4; yaw++)
            {
                seen.Add(orientation);
                orientation = OrientationPicker.Yaw(orientation, 1);
            }
        }

        Assert.Equal(24, seen.Count);
        Assert.All(seen, o => Assert.True(BlockOrientation.IsValid(o)));
    }

    [Property(MaxTest = 200)]
    public bool OrientationPicker_AnyStep_StaysValid(byte start, sbyte yaw, sbyte up)
    {
        var orientation = (byte)(start % BlockOrientation.Count);

        var result = OrientationPicker.Up(OrientationPicker.Yaw(orientation, yaw), up);

        return BlockOrientation.IsValid(result);
    }
}
