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
        // 0.2 m fades at the 1.2 m/s cap (166.7 ms) so the visible step stays within 2 cm per 60 Hz frame.
        Assert.Equal(-0.2f * (1f - 40f / (0.2f / 1.2f * 1000f)), correction.PositionOffset.X, 4);
        correction.Advance(0.2f);
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
        correction.Advance(1f);
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

    [Fact]
    public void Reconcile_ConsecutiveCorrections_SnapWhenVisibleErrorReachesOneMetre()
    {
        var correction = new PresentationCorrection();
        var before = State(Vector3.Zero);
        var after = State(new Vector3(0.6f, 0, 0));
        correction.Reconcile(before, after, false);
        Assert.False(correction.Snapped);
        // Physics moves a further 0.6 m while the visible pose is still 0 m away from the old one: visible error is 1.2 m.
        var next = State(new Vector3(1.2f, 0, 0));
        correction.Reconcile(after, next, false);
        Assert.True(correction.Snapped);
        Assert.Equal(Vector3.Zero, correction.PositionOffset);
    }
}
