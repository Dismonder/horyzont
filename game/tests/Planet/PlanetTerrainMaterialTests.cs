using GdUnit4;

using Godot;

using Horyzont.Game.Planet.Impostors;
using Horyzont.Game.Planet.Rendering;

using static GdUnit4.Assertions;

namespace Horyzont.Game.Tests.Planet;

/// <summary>K5-22 decisions 13–14: triplanar in model space, complementary dithering of terrain and impostor (engine, shader source).</summary>
[TestSuite]
public class PlanetTerrainMaterialTests
{
    private const string BayerMatrix = "0.0, 8.0, 2.0, 10.0, 12.0, 4.0, 14.0, 6.0, 3.0, 11.0, 1.0, 9.0, 15.0, 7.0, 13.0, 5.0";

    private static readonly string[] _worldOrView =
        ["MODEL_MATRIX", "VIEW_MATRIX", "INV_VIEW_MATRIX", "NODE_POSITION_WORLD", "CAMERA_POSITION_WORLD", "INV_PROJECTION_MATRIX"];

    [TestCase]
    [RequireGodotRuntime]
    public void PlanetTerrainMaterial_TriplanarUsesNodeLocalCoordinates()
    {
        var terrain = Source(PlanetTerrainMaterial.ShaderPath);
        var impostor = Source(PlanetImpostorMaterial.ShaderPath);

        // Model space only: the block-local vertex and normal (a block origin is a multiple of 16 m = 4 texture periods), so a terrain
        // node 108 km from the origin samples exactly the same texels as one at the origin.
        AssertThat(terrain.Contains("v_pos = VERTEX;", StringComparison.Ordinal)).IsTrue();
        AssertThat(terrain.Contains("v_normal = normalize(NORMAL);", StringComparison.Ordinal)).IsTrue();
        foreach (var name in _worldOrView)
        {
            AssertThat(Code(terrain).Contains(name, StringComparison.Ordinal)).OverrideFailureMessage($"terrain uses {name}").IsFalse();
            AssertThat(Code(impostor).Contains(name, StringComparison.Ordinal)).OverrideFailureMessage($"impostor uses {name}").IsFalse();
        }

        // 16 m mesh blocks × 2^lod × texture scale 0.25 are whole texture periods.
        AssertThat(Horyzont.Game.Planet.PlanetTerrainLayout.MeshBlockSize * 0.25f % 1f).IsEqual(0f);
    }

    [TestCase]
    [RequireGodotRuntime]
    public void PlanetMaterials_DitherIsComplementary_AndCompiles()
    {
        var terrain = Source(PlanetTerrainMaterial.ShaderPath);
        var impostor = Source(PlanetImpostorMaterial.ShaderPath);
        AssertThat(terrain.Contains(BayerMatrix, StringComparison.Ordinal)).IsTrue();
        AssertThat(impostor.Contains(BayerMatrix, StringComparison.Ordinal)).IsTrue();
        AssertThat(terrain.Contains("if (bayer4(FRAGCOORD.xy) < u_blend)", StringComparison.Ordinal)).IsTrue();
        AssertThat(impostor.Contains("if (bayer4(FRAGCOORD.xy) >= u_blend)", StringComparison.Ordinal)).IsTrue();

        // The same matrix as the pure model of the client (ImpostorBlend.ImpostorDraws).
        var values = BayerMatrix.Split(", ").Select(v => (int)float.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        for (var y = 0; y < 4; y++)
        {
            for (var x = 0; x < 4; x++)
            {
                var threshold = (values[(y * 4) + x] + 0.5f) / 16f;
                AssertThat(Horyzont.Client.Space.ImpostorBlend.ImpostorDraws(x, y, threshold + 0.001f)).IsTrue();
                AssertThat(Horyzont.Client.Space.ImpostorBlend.ImpostorDraws(x, y, threshold - 0.001f)).IsFalse();
            }
        }

        // Both shaders compile (their uniforms are listed only after a successful compile).
        AssertThat(Uniforms(PlanetTerrainMaterial.ShaderPath).Contains("u_blend")).IsTrue();
        AssertThat(Uniforms(PlanetImpostorMaterial.ShaderPath).Contains("u_blend")).IsTrue();
    }

    private static string Source(string path) => GD.Load<Shader>(path).Code;

    /// <summary>The source without comment lines.</summary>
    private static string Code(string source) =>
        string.Join('\n', source.Split('\n').Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    private static HashSet<string> Uniforms(string path)
    {
        var names = new HashSet<string>();
        foreach (var entry in GD.Load<Shader>(path).GetShaderUniformList(true))
        {
            names.Add((string)entry.AsGodotDictionary()["name"]);
        }

        return names;
    }
}
