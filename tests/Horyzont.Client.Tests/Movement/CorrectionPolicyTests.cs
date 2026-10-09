using System.Numerics;

using Horyzont.Client.Movement;
using Horyzont.Core.Motion;

namespace Horyzont.Client.Tests.Movement;

public sealed class CorrectionPolicyTests
{
    private static readonly CorrectionPolicy _policy = new();

    private static PredictedState State(Vector3 feet, Vector3 velocity, MovementMode mode = MovementMode.Walking) =>
        new(feet, velocity, Quaternion.Identity, 0f, mode, true);

    [Fact]
    public void CorrectionPolicy_Defaults_MatchThePlan()
    {
        Assert.Equal(0.02f, _policy.SnapDistance);
        Assert.Equal(0.1f, _policy.VelocityTolerance);
        Assert.Equal(120, _policy.MaxUnacknowledged);
    }

    [Theory]
    [InlineData(0.0f, false)]
    [InlineData(0.019f, false)]
    [InlineData(0.021f, true)]
    [InlineData(0.5f, true)]
    public void CorrectionPolicy_PositionError_UsesSnapDistance(float error, bool expected)
    {
        var predicted = State(new Vector3(4000f, 0f, 0f), Vector3.Zero);
        var server = State(new Vector3(4000f, error, 0f), Vector3.Zero);

        Assert.Equal(expected, _policy.NeedsCorrection(predicted, server));
    }

    [Theory]
    [InlineData(0.09f, false)]
    [InlineData(0.11f, true)]
    public void CorrectionPolicy_VelocityError_UsesTolerance(float error, bool expected)
    {
        var predicted = State(Vector3.UnitX * 4000f, new Vector3(0f, 0f, 5f));
        var server = State(Vector3.UnitX * 4000f, new Vector3(0f, error, 5f));

        Assert.Equal(expected, _policy.NeedsCorrection(predicted, server));
    }

    [Fact]
    public void CorrectionPolicy_DifferentMode_NeedsCorrection()
    {
        var predicted = State(Vector3.UnitX * 4000f, Vector3.Zero, MovementMode.Walking);
        var server = State(Vector3.UnitX * 4000f, Vector3.Zero, MovementMode.Falling);

        Assert.True(_policy.NeedsCorrection(predicted, server));
    }
}
