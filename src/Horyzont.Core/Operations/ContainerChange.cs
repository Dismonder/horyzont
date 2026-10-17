using Horyzont.Core.Ids;

namespace Horyzont.Core.Operations;

/// <summary>A container after an operation that changed it.</summary>
/// <param name="Container">Container.</param>
/// <param name="Version">Version after the operation (one more than before it, however many components touched it).</param>
/// <param name="Slots">Changed slot indexes in ascending order; every slot for a container created by the operation.</param>
public sealed record ContainerChange(ContainerId Container, uint Version, IReadOnlyList<ushort> Slots);
