using Horyzont.Core.Intents;

namespace Horyzont.Client.Tools;

/// <summary>An intent that waits for the reply of the server.</summary>
/// <param name="ClientSeq">Client sequence number.</param>
/// <param name="Intent">The intent.</param>
/// <param name="Preview">Preview at the time of sending.</param>
/// <param name="AddedTick">Client tick of sending.</param>
public readonly record struct PendingIntent(uint ClientSeq, Intent Intent, ToolPreview Preview, long AddedTick);
