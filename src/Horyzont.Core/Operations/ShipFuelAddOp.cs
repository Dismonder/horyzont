using Horyzont.Core.Ids;

namespace Horyzont.Core.Operations;

/// <summary>
/// Adds fuel to a ship (K5-24 decision 13): the refuel station path. The preconditions read only the hashed state (the construct
/// exists and is a ship), never <c>ShipState</c>, because fuel is outside the state hash. The effect clips to <paramref name="Capacity"/>,
/// which the operation carries: the applier reads no block content.
/// </summary>
/// <param name="Construct">The ship.</param>
/// <param name="Units">Fuel units to add; finite and positive.</param>
/// <param name="Capacity">Tank capacity of the ship when the operation was resolved; finite and non-negative.</param>
public sealed record ShipFuelAddOp(EntityId Construct, float Units, float Capacity) : Operation
{
    /// <inheritdoc />
    public override OperationKind Kind => OperationKind.ShipFuelAdd;
}
