using Horyzont.Core.Ids;
using Horyzont.Core.Protocol.Messages;

namespace Horyzont.Client.Navigation;

/// <summary>A player or a ship the client knows, placed in the system frame.</summary>
/// <param name="Entity">The entity.</param>
/// <param name="Kind">Player or construct.</param>
/// <param name="Owner">Owning player.</param>
/// <param name="Name">Name from the naming function, or null when none is known.</param>
/// <param name="X">System frame X, metres.</param>
/// <param name="Y">System frame Y, metres.</param>
/// <param name="Z">System frame Z, metres.</param>
public readonly record struct SystemMapEntity(EntityId Entity, EntityKind Kind, PlayerId? Owner, string? Name, double X, double Y, double Z);
