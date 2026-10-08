namespace Horyzont.Net.Transport.InMemory;

/// <summary>
/// Shared "medium" for all <see cref="InMemoryTransport"/> instances of one test or host process.
/// Single-threaded: create and use everything from the game thread.
/// </summary>
public sealed class InMemoryNetwork
{
    private readonly Dictionary<string, InMemoryTransport> _transports = new(StringComparer.Ordinal);
    private readonly Dictionary<(string From, string To), InMemoryLink> _links = [];
    private readonly LinkConditions _defaultConditions;

    /// <summary>Initializes a new instance of the <see cref="InMemoryNetwork"/> class.</summary>
    /// <param name="time">Clock driving all delays; tests pass a fake time provider.</param>
    /// <param name="defaultConditions">Conditions of links without explicit ones; perfect when omitted.</param>
    public InMemoryNetwork(TimeProvider time, LinkConditions? defaultConditions = null)
    {
        ArgumentNullException.ThrowIfNull(time);
        Time = time;
        _defaultConditions = defaultConditions ?? LinkConditions.Perfect;
    }

    internal TimeProvider Time { get; }

    /// <summary>Creates a transport reachable under <paramref name="name"/>.</summary>
    /// <param name="name">Unique name that acts as the transport's address.</param>
    /// <param name="options">Timeout and keep-alive settings.</param>
    /// <param name="fakeSteamId">If set, peers see this transport as a <see cref="SteamEndpoint"/> with this id.</param>
    /// <returns>The new transport.</returns>
    /// <exception cref="ArgumentException">The name is already taken.</exception>
    public InMemoryTransport CreateTransport(string name, TransportOptions options, ulong? fakeSteamId = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(options);
        if (_transports.ContainsKey(name))
        {
            throw new ArgumentException($"A transport named '{name}' already exists.", nameof(name));
        }

        var transport = new InMemoryTransport(this, name, options, fakeSteamId);
        _transports.Add(name, transport);
        return transport;
    }

    /// <summary>Sets the conditions of the link between <paramref name="a"/> and <paramref name="b"/> in both directions.</summary>
    /// <param name="a">First transport name.</param>
    /// <param name="b">Second transport name.</param>
    /// <param name="conditions">New conditions; the random generators restart from their seed.</param>
    public void SetConditions(string a, string b, LinkConditions conditions)
    {
        ArgumentNullException.ThrowIfNull(conditions);
        GetLink(a, b).Apply(conditions);
        GetLink(b, a).Apply(conditions);
    }

    /// <summary>Drops all traffic between <paramref name="a"/> and <paramref name="b"/>, in both directions.</summary>
    /// <param name="a">First transport name.</param>
    /// <param name="b">Second transport name.</param>
    public void Partition(string a, string b)
    {
        GetLink(a, b).Partitioned = true;
        GetLink(b, a).Partitioned = true;
    }

    /// <summary>Ends a <see cref="Partition"/>.</summary>
    /// <param name="a">First transport name.</param>
    /// <param name="b">Second transport name.</param>
    public void Heal(string a, string b)
    {
        GetLink(a, b).Partitioned = false;
        GetLink(b, a).Partitioned = false;
    }

    internal bool TryGetTransport(string name, out InMemoryTransport transport) =>
        _transports.TryGetValue(name, out transport!);

    internal void Remove(string name) => _transports.Remove(name);

    internal InMemoryLink GetLink(string from, string to)
    {
        if (!_links.TryGetValue((from, to), out var link))
        {
            link = new InMemoryLink(_defaultConditions);
            _links.Add((from, to), link);
        }

        return link;
    }
}
