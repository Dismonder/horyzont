namespace Horyzont.Testing;

/// <summary>What <see cref="FakeTerrainService.MeasureSphere"/> reports: solid voxels by world material id and empty voxels.</summary>
/// <param name="Solid">Solid voxel counts by material id.</param>
/// <param name="Air">Empty voxels.</param>
public sealed record MeasuredSphere(IReadOnlyDictionary<byte, int> Solid, int Air)
{
    /// <summary>Only solid voxels of one material.</summary>
    public static MeasuredSphere SolidOf(byte material, int count) => new(new Dictionary<byte, int> { [material] = count }, 0);

    /// <summary>Only empty voxels.</summary>
    public static MeasuredSphere AirOf(int count) => new(new Dictionary<byte, int>(), count);
}
