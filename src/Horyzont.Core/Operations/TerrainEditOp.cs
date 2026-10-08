using System.Numerics;

using Horyzont.Core.Ids;

namespace Horyzont.Core.Operations;

/// <summary>Terrain edit with a sphere, positions in the planet frame.</summary>
/// <param name="Planet">Planet.</param>
/// <param name="Shape">Edit shape.</param>
/// <param name="Center">Centre in the planet frame.</param>
/// <param name="Radius">Radius in metres.</param>
/// <param name="Mode">Add or remove.</param>
/// <param name="MaterialId">Material for <see cref="TerrainEditMode.Add"/>; 0 for <see cref="TerrainEditMode.Remove"/>.</param>
public sealed record TerrainEditOp(
    PlanetId Planet, TerrainShape Shape, Vector3 Center, float Radius, TerrainEditMode Mode, byte MaterialId) : Operation
{
    /// <inheritdoc />
    public override OperationKind Kind => OperationKind.TerrainEdit;
}
