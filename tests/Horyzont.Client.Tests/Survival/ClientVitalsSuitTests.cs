using Horyzont.Client.Survival;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Survival;

namespace Horyzont.Client.Tests.Survival;

/// <summary>The suit tail of message 44 as the client keeps it (K6-34 D20): the resources, their capacity and the environment; before any message the defaults of a plain suit.</summary>
public sealed class ClientVitalsSuitTests
{
    private static readonly SuitResources _capacity = new(100f, 100f, 100f, 100f, 200f);

    [Fact]
    public void Receive_SuitTailExposed()
    {
        var vitals = new ClientVitals();
        Assert.Equal(PlayerVitalsMessage.DefaultSuit, vitals.Suit);
        Assert.Equal(PlayerVitalsMessage.DefaultSuit, vitals.SuitCapacity);

        vitals.Apply(
            new PlayerVitalsMessage(100f, 100f, 100f, 100f, false, false, false, 0, 0, 0, VitalsEnvironment.None, new SuitResources(100f, 50f, 0f, 25f, 75.5f), _capacity), 500);

        Assert.Equal(new SuitResources(100f, 50f, 0f, 25f, 75.5f), vitals.Suit);
        Assert.Equal(_capacity, vitals.SuitCapacity);

        // The last message wins, and a disconnect forgets the suit.
        vitals.Apply(new PlayerVitalsMessage(100f, 100f, 100f, 100f, false, false, false, 0, 0, 0, VitalsEnvironment.None, new SuitResources(1f, 2f, 3f, 4f, 5f), _capacity), 506);
        Assert.Equal(new SuitResources(1f, 2f, 3f, 4f, 5f), vitals.Suit);
        vitals.Clear();
        Assert.Equal(PlayerVitalsMessage.DefaultSuit, vitals.Suit);
    }

    [Fact]
    public void EnvironmentFlags_Exposed()
    {
        var vitals = new ClientVitals();
        Assert.Equal(VitalsEnvironment.None, vitals.Environment);

        var environment = new VitalsEnvironment(Sheltered: true, Generator: false, LifeSupport: true, Present: 0b01001, Active: 0b01000, Exposed: 0b01000, ShelterHits: 23);
        vitals.Apply(new PlayerVitalsMessage(100f, 100f, 100f, 100f, false, false, false, 0, 0, 0, environment, PlayerVitalsMessage.DefaultSuit, _capacity), 500);

        Assert.Equal(environment, vitals.Environment);
        Assert.True(vitals.Environment.Sheltered);
        Assert.True(vitals.Environment.LifeSupport);
        Assert.Equal(23, vitals.Environment.ShelterHits);
    }
}
