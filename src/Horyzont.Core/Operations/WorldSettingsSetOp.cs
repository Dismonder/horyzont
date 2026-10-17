using Horyzont.Core.World;

namespace Horyzont.Core.Operations;

/// <summary>Replaces the settings of the world (K6-35 D4, operation 46). The applier checks the ranges, not the rights of the actor.</summary>
/// <param name="Settings">The new settings.</param>
public sealed record WorldSettingsSetOp(WorldSettings Settings) : Operation
{
    /// <inheritdoc />
    public override OperationKind Kind => OperationKind.WorldSettingsSet;
}
