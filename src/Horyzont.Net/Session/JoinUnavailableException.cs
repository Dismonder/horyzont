namespace Horyzont.Net.Session;

/// <summary>A controlled game refusal before allocating a player or creating ServerHello.</summary>
public sealed class JoinUnavailableException : Exception
{
    /// <summary>Creates a refusal with a bounded, secret-free technical detail.</summary>
    public JoinUnavailableException(byte reason, string detail) : base(detail)
    {
        Reason = reason;
        Detail = detail;
    }
    /// <summary>Gets the availability reason.</summary>
    public byte Reason { get; }
    /// <summary>Gets the plain text detail.</summary>
    public string Detail { get; }
}
