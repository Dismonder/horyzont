namespace Horyzont.Core.Ids;

/// <summary>Identifier of an item container, assigned from 1 by <see cref="ContainerIdAllocator"/>.</summary>
/// <param name="Value">Numeric value; 0 is never assigned.</param>
public readonly record struct ContainerId(ulong Value)
{
    /// <summary>The unassigned identifier (0).</summary>
    public static ContainerId None => default;

    /// <summary>True for <see cref="None"/>.</summary>
    public bool IsNone => Value == 0;
}
