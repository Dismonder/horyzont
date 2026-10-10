using GdUnit4;

using Horyzont.Core.Ids;
using Horyzont.Game.Frames;

using static GdUnit4.Assertions;

using NumQuaternion = System.Numerics.Quaternion;
using NumVector3 = System.Numerics.Vector3;

namespace Horyzont.Game.Tests.Character;

[TestSuite]
public class PlanetFramesTests
{
    private static readonly PlanetId _planet = new(7);

    [TestCase]
    [RequireGodotRuntime]
    public void PlanetFrames_PositionRoundTrips()
    {
        var godot = PlanetFrames.ToGodot(_planet, new NumVector3(1.5f, -4000.25f, 12.75f));
        var back = PlanetFrames.ToPlanetLocal(_planet, godot);

        AssertThat(back.X).IsEqual(1.5f);
        AssertThat(back.Y).IsEqual(-4000.25f);
        AssertThat(back.Z).IsEqual(12.75f);
    }

    [TestCase]
    [RequireGodotRuntime]
    public void PlanetFrames_RotationRoundTrips_AndStaysNormalized()
    {
        var rotation = NumQuaternion.Normalize(NumQuaternion.CreateFromYawPitchRoll(0.4f, -0.3f, 0.2f));

        var godot = PlanetFrames.ToGodot(_planet, rotation);
        var basisBack = PlanetFrames.ToNumerics(_planet, PlanetFrames.ToGodotBasis(_planet, rotation));
        var back = PlanetFrames.ToNumerics(_planet, godot);

        AssertThat(Math.Abs(NumQuaternion.Dot(back, rotation))).IsGreater(0.999999f);
        AssertThat(Math.Abs(NumQuaternion.Dot(basisBack, rotation))).IsGreater(0.99999f);
        AssertThat(back.Length()).IsBetween(0.999999f, 1.000001f);
    }

    [TestCase]
    [RequireGodotRuntime]
    public void PlanetFrames_UnknownPlanetOrNonFinite_Throws()
    {
        AssertThrown(() => PlanetFrames.ToPlanetLocal(default, new Godot.Vector3(1, 2, 3))).IsInstanceOf<ArgumentException>();
        AssertThrown(() => PlanetFrames.ToPlanetLocal(_planet, new Godot.Vector3(double.NaN, 0, 0))).IsInstanceOf<ArgumentException>();
        AssertThrown(() => PlanetFrames.ToPlanetLocal(_planet, new Godot.Vector3(0, double.PositiveInfinity, 0))).IsInstanceOf<ArgumentException>();
        AssertThrown(() => PlanetFrames.ToPlanetLocal(_planet, new Godot.Vector3(0, 0, 1e9))).IsInstanceOf<ArgumentException>();
        AssertThrown(() => PlanetFrames.ToGodot(_planet, new NumVector3(float.NaN, 0, 0))).IsInstanceOf<ArgumentException>();
        AssertThrown(() => PlanetFrames.ToGodot(_planet, new NumQuaternion(0, 0, float.NaN, 1))).IsInstanceOf<ArgumentException>();
    }
}
