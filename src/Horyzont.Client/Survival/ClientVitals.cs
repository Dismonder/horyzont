using Horyzont.Core.Protocol.Messages;

namespace Horyzont.Client.Survival;

/// <summary>
/// What the server told the local player about the health, the stamina and the rest (K6-33 D20, message 44, unreliable, about 10 times a second). The channel may lose a
/// message but never repeats an old one, so the last message received wins. The remaining time of the rest is relative to the moment of the message, so
/// <see cref="RestRemainingTicks"/> counts it down with the tick of the server that the HUD reads from the same source as every other time. Main thread only.
/// </summary>
public sealed class ClientVitals
{
    private PlayerVitalsMessage? _message;
    private ulong _receivedAtTick;

    /// <summary>Raised after a message replaced the state or a disconnect cleared it.</summary>
    public event Action? Changed;

    /// <summary>Gets a value indicating whether a message has arrived since the connection.</summary>
    public bool HasState => _message is not null;

    /// <summary>Gets the health, 0 before the first message.</summary>
    public float Health => _message?.Health ?? 0f;

    /// <summary>Gets the maximum health, 0 before the first message.</summary>
    public float HealthMax => _message?.HealthMax ?? 0f;

    /// <summary>Gets the stamina, 0 before the first message.</summary>
    public float Stamina => _message?.Stamina ?? 0f;

    /// <summary>Gets the maximum stamina, 0 before the first message.</summary>
    public float StaminaMax => _message?.StaminaMax ?? 0f;

    /// <summary>Gets a value indicating whether the player is exhausted (cannot sprint or jump).</summary>
    public bool Exhausted => _message?.Exhausted ?? false;

    /// <summary>Gets a value indicating whether the conditions of rest hold and the warm-up is running.</summary>
    public bool Resting => _message?.RestConditionsMet ?? false;

    /// <summary>Gets the progress of the warm-up in ticks, 0 to 1200.</summary>
    public int RestWarmupTicks => _message?.RestWarmupTicks ?? 0;

    /// <summary>Gets the comfort level of the surroundings, 0 to 10.</summary>
    public int Comfort => _message?.Comfort ?? 0;

    /// <summary>Gets the server tick the latest message was received at.</summary>
    public ulong ReceivedAtTick => _receivedAtTick;

    /// <summary>Replaces the state with a message; the last message wins.</summary>
    /// <param name="message">Message 44.</param>
    /// <param name="serverTick">The tick of the server at the moment of reception; 0 when it is not known yet.</param>
    public void Apply(PlayerVitalsMessage message, ulong serverTick)
    {
        ArgumentNullException.ThrowIfNull(message);
        _message = message;
        _receivedAtTick = serverTick;
        Changed?.Invoke();
    }

    /// <summary>The remaining ticks of the rest.</summary>
    /// <param name="serverTick">The current tick of the server.</param>
    /// <returns>0 when the player is not rested; else the ticks of the message minus those that passed since it was received.</returns>
    public ulong RestRemainingTicks(ulong serverTick)
    {
        if (_message is not { Rested: true } message)
        {
            return 0;
        }

        var passed = serverTick > _receivedAtTick ? serverTick - _receivedAtTick : 0UL;
        return message.RestRemainingTicks > passed ? message.RestRemainingTicks - passed : 0UL;
    }

    /// <summary>Forgets everything (disconnect).</summary>
    public void Clear()
    {
        _message = null;
        _receivedAtTick = 0;
        Changed?.Invoke();
    }
}
