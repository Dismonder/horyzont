using System.Numerics;

using Horyzont.Client.Movement;
using Horyzont.Core.Motion;

namespace Horyzont.Client.Tests.Movement;

public sealed class PresentationCorrectionTests
{
    private static PredictedState State(Vector3 feet) => new(feet, Vector3.Zero, Quaternion.Identity, 0, MovementMode.Walking, true);

    [Fact]
    public void Reconcile_SmallError_PhysicsCorrectsAndPresentationStaysContinuous()
    {
        var correction = new PresentationCorrection();
        var before = State(Vector3.Zero);
        var after = State(new Vector3(0.2f, 0, 0));
        correction.Reconcile(before, after, false);
        Assert.Equal(before.Feet, after.Feet + correction.PositionOffset);
        correction.Advance(0.04f);
        Assert.Equal(-0.12f, correction.PositionOffset.X, 5);
        correction.Advance(0.06f);
        Assert.Equal(Vector3.Zero, correction.PositionOffset);
        Assert.Equal(Quaternion.Identity, correction.RotationOffset);
    }

    [Fact]
    public void Reconcile_RepeatedCorrection_PreservesCurrentVisualPose()
    {
        var correction = new PresentationCorrection();
        var before = State(Vector3.Zero);
        var after = State(new Vector3(0.2f, 0, 0));
        correction.Reconcile(before, after, false);
        correction.Advance(0.04f);
        var visible = after.Feet + correction.PositionOffset;
        var next = after with { Feet = new Vector3(0.3f, 0, 0), BodyRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.2f) };
        correction.Reconcile(after, next, false);
        Assert.Equal(visible.X, (next.Feet + correction.PositionOffset).X, 6);
        Assert.True(MathF.Abs(Quaternion.Dot(Quaternion.Identity, correction.RotationOffset * next.BodyRotation)) > 0.99999f);
        correction.Advance(0.1f);
        Assert.Equal(Vector3.Zero, correction.PositionOffset);
    }

    [Theory]
    [InlineData(1f, 0f, false, false)]
    [InlineData(0f, 45f, false, false)]
    [InlineData(0.1f, 0f, true, false)]
    [InlineData(0.1f, 0f, false, true)]
    public void Reconcile_LargeErrorOrFrameChange_SnapsImmediately(float distance, float degrees, bool discontinuity, bool frozen)
    {
        var correction = new PresentationCorrection();
        var before = State(Vector3.Zero);
        var after = before with
        {
            Feet = new Vector3(distance, 0, 0),
            BodyRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, degrees * MathF.PI / 180),
            Mode = frozen ? MovementMode.Frozen : MovementMode.Walking,
        };
        correction.Reconcile(before, after, discontinuity);
        Assert.Equal(Vector3.Zero, correction.PositionOffset);
        Assert.Equal(Quaternion.Identity, correction.RotationOffset);
        Assert.True(correction.Snapped);
    }
}
