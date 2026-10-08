namespace Horyzont.Net.Tests.Transport;

/// <summary>Creates a fresh <see cref="ITransportWorld"/> per test.</summary>
public interface ITransportWorldFactory
{
    static abstract ITransportWorld Create();
}
