using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.World.Terrain;

namespace Horyzont.Client.Terrain;

/// <summary>A terrain edit the client shows before the server confirms it. Only the ledger changes the state fields.</summary>
/// <param name="ClientSeq">Client sequence of the intent.</param>
/// <param name="Planet">Planet.</param>
/// <param name="Center">Centre of the sphere, planet frame.</param>
/// <param name="Radius">Radius in metres.</param>
/// <param name="Mode">Add or remove.</param>
/// <param name="MaterialId">Material of an add.</param>
/// <param name="Keys">The LOD zero blocks the edit may change, in key order.</param>
public sealed record PredictedEdit(
    uint ClientSeq, PlanetId Planet, Vector3 Center, float Radius, TerrainEditMode Mode, byte MaterialId, IReadOnlyList<TerrainBlockKey> Keys)
{
    /// <summary>Gets the operation the server applied for the intent; none until it accepted.</summary>
    public OpId Operation { get; internal set; }

    /// <summary>Gets the timestamp of the acceptance (clock of the ledger), for the barrier timeout.</summary>
    public long AcceptedAt { get; internal set; }

    /// <summary>Gets a value indicating whether the server accepted the intent; the edit then waits for its barrier.</summary>
    public bool IsAccepted => Operation.Value != 0;
}
