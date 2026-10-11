using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Operations;
using Horyzont.Core.World;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.Simulation;

/// <summary>Turns a validated intent into an operation. Randomness, if any, is resolved here and stored in the operation.</summary>
internal static class IntentResolver
{
    internal static Operation Resolve(WorldState state, Intent intent, IntentFacts facts) => intent switch
    {
        TerrainToolIntent tool => new TerrainEditOp(facts.Planet, TerrainShape.Sphere, facts.Point, tool.Radius, tool.Mode, tool.MaterialId),
        PlaceBlockIntent { Target: ConstructFaceTarget face } place =>
            new BlockSetOp(face.Construct, facts.Cell, BlockState.Create(place.BlockTypeId, place.Orientation)),
        PlaceBlockIntent { Target: TerrainTarget } place => new ConstructCreateOp(
            state.EntityIds.PeekNext(), FrameRef.Of(facts.Planet), facts.Point, facts.Rotation, new CellPos(0, 0, 0),
            BlockState.Create(place.BlockTypeId, place.Orientation)),
        RemoveBlockIntent remove => new BlockClearOp(remove.Construct, remove.Cell),
        ShipModeIntent ship => ResolveShipMode(state, ship),
        RefuelIntent refuel => new ShipFuelAddOp(refuel.Ship, facts.Amount, facts.Amount),
        _ => throw new InvalidOperationException($"Intent {intent.GetType().Name} passed validation but has no resolution."),
    };

    private static ShipModeSetOp ResolveShipMode(WorldState state, ShipModeIntent intent)
    {
        state.Constructs.TryGet(intent.Construct, out var construct);
        return new ShipModeSetOp(intent.Construct, intent.TargetMode, construct!.Parent, construct.ParentSeq, construct.Origin, construct.Rotation);
    }
}
