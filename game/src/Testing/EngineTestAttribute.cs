namespace Horyzont.Game.Testing;

/// <summary>Marks a method of an <see cref="EngineTestBase"/> subclass as an engine test (see <see cref="EngineTestRunner"/>).</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class EngineTestAttribute : Attribute
{
    /// <summary>Runs only with <c>--slow</c> (frame-time tests; CI passes the flag).</summary>
    public bool Slow { get; init; }

    /// <summary>Runs only with <c>--manual</c> (needs a window and a GPU; the result goes to the PR).</summary>
    public bool Manual { get; init; }
}
