using Horyzont.Core.Ids;

namespace Horyzont.Core.Props;

/// <summary>A frozen rule that places props on a planet from its seed (K6-31 D5). Pure: the same input always gives the same output.</summary>
public interface IPropLayout
{
    /// <summary>Decides what stands in a cell slot.</summary>
    /// <param name="seed">Seed of the planet.</param>
    /// <param name="id">The cell slot.</param>
    /// <param name="candidate">The prop when there is one.</param>
    /// <returns>True when the slot holds a prop.</returns>
    bool TryGetCandidate(ulong seed, PropId id, out PropCandidate candidate);

    /// <summary>Whether a kind may stand at a height above the planet radius.</summary>
    /// <param name="kind">The kind.</param>
    /// <param name="heightOverRadiusMeters">Anchor height minus the planet radius, in metres.</param>
    /// <returns>True when the band of the kind contains the height.</returns>
    bool Accepts(PropKindV1 kind, float heightOverRadiusMeters);
}
