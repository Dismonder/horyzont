namespace Horyzont.Core.Content;

/// <summary>A suit module worn in a module slot (K6-34, D8): one to five effects on different resources.</summary>
/// <param name="Effects">The effects.</param>
public sealed record SuitModuleDef(IReadOnlyList<SuitModuleEffect> Effects);
