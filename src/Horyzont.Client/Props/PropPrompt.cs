using Horyzont.Core.Props;

namespace Horyzont.Client.Props;

/// <summary>The hint under the crosshair for the prop that is aimed at.</summary>
/// <param name="Kind">What the press does, or that the tool is too weak.</param>
/// <param name="Prop">Kind of the prop.</param>
/// <param name="RequiredTier">Tool tier the prop needs.</param>
public readonly record struct PropPrompt(PropPromptKind Kind, PropKindV1 Prop, int RequiredTier);
