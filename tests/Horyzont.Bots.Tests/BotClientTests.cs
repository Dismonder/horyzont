using System.Numerics;

using Horyzont.Bots.Client;
using Horyzont.Client.Tools;
using Horyzont.Core.Ids;
using Horyzont.Core.Simulation;
using Horyzont.Core.World.Terrain;

namespace Horyzont.Bots.Tests;

public sealed class BotClientTests
{
    [Theory]
    [InlineData(0f, 4000f, 0f, 0.3f, -0.7f)]
    [InlineData(1200f, 3800f, -400f, -2.5f, 0.2f)]
    [InlineData(-3000f, -1000f, 2500f, 3.1f, -1.4f)]
    public void AimRay_FromServerFeet_StartsAtServerEye(float x, float y, float z, float yaw, float pitch)
    {
        var rules = new GameRules();
        var feet = new Vector3(x, y, z);

        var aim = BotClient.BuildAim(new PlanetId(1), feet, yaw, pitch, rules);

        Assert.Equal(AimRayBuilder.EyeFromFeet(feet, rules), aim.Eye);
        Assert.Equal(rules.EyeHeight, Vector3.Distance(aim.Eye, feet), 3);
        Assert.Equal(1f, aim.Direction.Length(), 3);
        Assert.Equal(1f, aim.LookForward.Length(), 3);
        Assert.Equal(feet, aim.Feet);

        // The pitch tilts the ray against the local up: negative looks down.
        var up = TerrainMath.UpAt(feet);
        Assert.Equal(MathF.Sin(pitch), Vector3.Dot(aim.Direction, up), 3);
        Assert.Equal(0f, Vector3.Dot(aim.LookForward, up), 3);
    }

    [Fact]
    public void LookDelta_FedToTheOrientationTracker_ReachesTheCommandedYawAndPitch()
    {
        var tracker = new Horyzont.Client.Camera.OrientationTracker(new Horyzont.Core.Motion.MovementRules());
        var up = System.Numerics.Vector3.UnitX;
        tracker.Reset(Horyzont.Core.Motion.SurfaceFrame.FromYaw(up, 2.5f), 0.1f);

        var (yaw, pitch) = Horyzont.Bots.Scripting.BotControls.LookDelta(tracker.BodyRotation, up, tracker.Pitch, -2.9f, -0.7f);
        tracker.Advance(up, yaw, pitch);

        Assert.Equal(-2.9f, tracker.YawFor(up), 3);
        Assert.Equal(-0.7f, tracker.Pitch, 3);
        Assert.True(MathF.Abs(yaw) < MathF.PI, "the turn takes the short way round");
    }

    [Fact]
    public void FrameFor_ToolActions_PressWhatAPlayerPresses()
    {
        var dig = Horyzont.Bots.Scripting.BotControls.FrameFor(Horyzont.Bots.Scripting.BotAction.Dig);
        var place = Horyzont.Bots.Scripting.BotControls.FrameFor(Horyzont.Bots.Scripting.BotAction.PlaceBlock);

        Assert.True(dig.PrimaryPressed && dig.SelectTool == Horyzont.Client.Tools.ToolKind.TerrainManipulator);
        Assert.True(place.SecondaryPressed && place.SelectTool == Horyzont.Client.Tools.ToolKind.Builder);
        Assert.Equal(default, Horyzont.Bots.Scripting.BotControls.FrameFor(Horyzont.Bots.Scripting.BotAction.AbuseBadRadius));
    }
}
