namespace Horyzont.Core.Ids;

/// <summary>Identifier of a map marker in a world (K5-27), assigned by <see cref="MarkerIdAllocator"/> from 1.</summary>
/// <param name="Value">Numeric value; 0 is never assigned.</param>
public readonly record struct MarkerId(ulong Value)
{
    /// <summary>The unassigned identifier (0).</summary>
    public static MarkerId None => default;

    /// <summary>True for <see cref="None"/>.</summary>
    public bool IsNone => Value == 0;
}
