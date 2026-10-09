namespace Horyzont.Core.Tests.World.Constructs;

/// <summary>
/// Records the golden files of <see cref="ConstructPlacementTests"/> from the K1 code. Run only on purpose, with
/// HORYZONT_GOLDEN_PLACEMENT_OUT set to the output directory, on the code before K2-13 W1. A recorded file never changes.
/// </summary>
public sealed class PlacementGoldenRecorder
{
    [Fact]
    [Trait("Category", "Manual")]
    public void Record_WritesGoldenFiles()
    {
        var output = Environment.GetEnvironmentVariable(PlacementGolden.RecorderVariable);
        if (string.IsNullOrEmpty(output))
        {
            return;
        }

        System.IO.Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "on_terrain.txt"), PlacementGolden.Join(PlacementGolden.RecordPlacement()));
        File.WriteAllText(Path.Combine(output, "player_capsule.txt"), PlacementGolden.Join(PlacementGolden.RecordCapsule()));
    }
}
