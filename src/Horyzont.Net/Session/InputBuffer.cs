using Horyzont.Core.Protocol.Messages;

namespace Horyzont.Net.Session;

/// <summary>The player input buffer of K3-14: <see cref="SequencedInputBuffer{TInput}"/> of <see cref="PlayerInput"/>.</summary>
/// <param name="capacity">Capacity.</param>
internal sealed class InputBuffer(int capacity) : SequencedInputBuffer<PlayerInput>(capacity);
