namespace Horyzont.Game.Constructs;

/// <summary>One time budget shared by all worker joins.</summary>
internal sealed class ShutdownBudget(TimeProvider time, TimeSpan timeout)
{
    private readonly long _started = time.GetTimestamp();

    internal TimeSpan Remaining
    {
        get
        {
            var remaining = timeout - time.GetElapsedTime(_started);
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
    }
}
