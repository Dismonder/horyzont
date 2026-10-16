using Horyzont.Core.Ids;

namespace Horyzont.Core.Operations;

/// <summary>A container after an operation that changed it.</summary>
/// <param name="Container">Container.</param>
/// <param name="Version">Version after the operation (one more than before it, however many components touched it).</param>
/// <param name="Slots">Changed slot indexes in ascending order; every slot for a container created by the operation.</param>
/// <param name="Removed">True when the operation removed the container (K6-30).</param>
/// <param name="Configured">True when the operation changed the size or access of the container (operation 37), also when it changed slots too.</param>
public sealed record ContainerChange(ContainerId Container, uint Version, IReadOnlyList<ushort> Slots, bool Removed = false, bool Configured = false);
