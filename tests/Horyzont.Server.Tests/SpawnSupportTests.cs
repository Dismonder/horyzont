using System.Numerics;

using Horyzont.Game.Physics;
using Horyzont.Game.Server.Characters;

namespace Horyzont.Server.Tests;

public sealed class SpawnSupportTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void CanSettle_TerrainArrivesBeforeRoof_WaitsForBoth(bool terrain, bool constructs, bool expected) =>
        Assert.Equal(expected, SpawnSupport.CanSettle(terrain, constructs));

    [Fact]
    public void CanExpire_RoofCollisionStillPending_DoesNotReleaseBodyThroughFloor() =>
        Assert.False(SpawnSupport.CanExpire(false));

    [Fact]
    public void CollisionMask_SavedRoofPose_SearchesTerrainAndConstructs() =>
        Assert.Equal(CollisionLayers.TerrainMask | CollisionLayers.ConstructsMask, SpawnSupport.CollisionMask);

    [Fact]
    public void FeetOnSupport_SavedPoseAlreadyOnRoof_PreservesPose()
    {
        var roof = new Vector3(0, 4005, 0);
        var saved = roof + (Vector3.UnitY * 0.01f);
        Assert.Equal(saved, SpawnSupport.FeetOnSupport(saved, roof, Vector3.UnitY, 0.05f));
    }

    [Fact]
    public void FeetOnSupport_SavedPoseAboveNearestFloor_SettlesOntoFloor()
    {
        var floor = new Vector3(0, 4005, 0);
        var saved = floor + (Vector3.UnitY * 3);
        Assert.Equal(floor + (Vector3.UnitY * 0.05f), SpawnSupport.FeetOnSupport(saved, floor, Vector3.UnitY, 0.05f));
    }
}
