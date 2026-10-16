using System.Numerics;

using Horyzont.Core.Props;
using Horyzont.Core.World.Terrain;

namespace Horyzont.Client.Tests.Props;

/// <summary>Which prop is under the crosshair (K6-31 D19): the stand-in body of the layout, before the terrain, within reach, not harvested.</summary>
public sealed class PropPickerTests
{
    [Fact]
    public void RayHitsTrunkBeforeTerrain_PicksProp()
    {
        var rig = new PropRig();
        var tree = rig.Add(PropKindV1.Pine, 3f);
        var far = rig.Add(PropKindV1.Pine, 6f);

        Assert.True(rig.Picker.TryPick(rig.AimAt(tree, 1.5f), rig.World, rig.World.Rules, out var pick));

        Assert.Equal(tree.Id, pick.Prop.Id);
        Assert.Equal(FakeWorldPlanet, pick.Planet);
        Assert.InRange(pick.Distance, 2.5f, 3.2f);
        Assert.NotEqual(far.Id, pick.Prop.Id);
    }

    [Fact]
    public void TerrainBeforeTrunk_NoProp()
    {
        var rig = new PropRig();
        var tree = rig.Add(PropKindV1.Pine, 4f);
        rig.World.TerrainOverride = new TerrainHit(rig.Feet + (rig.Side * 1.5f), rig.Up, 1.5f);

        Assert.False(rig.Picker.TryPick(rig.AimAt(tree, 1.5f), rig.World, rig.World.Rules, out _));

        // Behind the prop the same terrain does not hide it.
        rig.World.TerrainOverride = new TerrainHit(rig.Feet + (rig.Side * 6f), rig.Up, 6f);
        Assert.True(rig.Picker.TryPick(rig.AimAt(tree, 1.5f), rig.World, rig.World.Rules, out var pick));
        Assert.Equal(tree.Id, pick.Prop.Id);
    }

    [Fact]
    public void HarvestedProp_NotPicked()
    {
        var rig = new PropRig();
        var tree = rig.Add(PropKindV1.Pine, 3f);
        var aim = rig.AimAt(tree, 1.5f);
        Assert.True(rig.Picker.TryPick(aim, rig.World, rig.World.Rules, out _));

        rig.MarkHarvested(tree);

        Assert.False(rig.Picker.TryPick(aim, rig.World, rig.World.Rules, out _));
    }

    [Fact]
    public void Beyond8m_NoProp()
    {
        var rig = new PropRig();
        var tree = rig.Add(PropKindV1.PineTall, 9f);

        Assert.False(rig.Picker.TryPick(rig.AimAt(tree, 2f), rig.World, rig.World.Rules, out _));

        // The same tree within reach is picked: it is the distance that decides.
        var near = rig.Add(PropKindV1.PineTall, 5f);
        Assert.True(rig.Picker.TryPick(rig.AimAt(near, 2f), rig.World, rig.World.Rules, out var pick));
        Assert.Equal(near.Id, pick.Prop.Id);
    }

    [Fact]
    public void RockSphere_Picked()
    {
        var rig = new PropRig();
        var rock = rig.Add(PropKindV1.Boulder, 2.5f);

        Assert.True(rig.Picker.TryPick(rig.AimAt(rock, 0.7f), rig.World, rig.World.Rules, out var pick));

        Assert.Equal(rock.Id, pick.Prop.Id);
        Assert.Equal(PropKindV1.Boulder, pick.Prop.Kind);

        // A ray that passes beside the body misses it.
        Assert.False(rig.Picker.TryPick(rig.AimAt(rock, 0.7f) with { Direction = Vector3.Normalize(rig.AimAt(rock, 0.7f).Direction + (rig.Side * 0.5f)) }, rig.World, rig.World.Rules, out _));
    }

    private static Horyzont.Core.Ids.PlanetId FakeWorldPlanet => Horyzont.Client.Tests.Tools.FakeToolWorldView.Planet;
}
