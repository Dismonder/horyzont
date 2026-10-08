using Horyzont.Net.Transport;

namespace Horyzont.Net.Tests.Transport;

/// <summary>A transport under test together with the sink recording its events.</summary>
public sealed record TransportNode(ITransport Transport, RecordingEvents Events);
