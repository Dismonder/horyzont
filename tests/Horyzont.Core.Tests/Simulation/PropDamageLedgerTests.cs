using Horyzont.Core.Ids;
using Horyzont.Core.Simulation;

namespace Horyzont.Core.Tests.Simulation;

/// <summary>The memory of damaged props (K6-31 D10): expiry after 1800 ticks, the limit of 4096 entries, forgetting on harvest.</summary>
public sealed class PropDamageLedgerTests
{
    private const int Full = 3;
    private static readonly PlanetId _planet = new(1);

    private static PropId Prop(int n) => PropId.Create(0, n, 0, 0);

    private static PropDamageLedger Create() => new(new GameRules { PropDamageResetTicks = 1_000_000 });

    [Fact]
    public void Over4096Props_OldestResets()
    {
        var rules = new GameRules { PropDamageResetTicks = 1_000_000 };
        Assert.Equal(4096, rules.MaxDamagedProps);
        var ledger = new PropDamageLedger(rules);

        // Prop n was hit at tick n; 4097 different props do not fit: the one hit first is dropped, the rest keep their wear.
        for (var n = 0; n <= rules.MaxDamagedProps; n++)
        {
            ledger.Record(_planet, Prop(n), new Tick((ulong)n), 1);
        }

        var now = new Tick((ulong)rules.MaxDamagedProps + 1);
        Assert.Equal(rules.MaxDamagedProps, ledger.Count);
        Assert.Equal(Full, ledger.Remaining(_planet, Prop(0), now, Full));
        Assert.Equal(1, ledger.Remaining(_planet, Prop(1), now, Full));
        Assert.Equal(1, ledger.Remaining(_planet, Prop(rules.MaxDamagedProps), now, Full));
    }

    [Fact]
    public void ResetAfter1800Ticks()
    {
        var ledger = new PropDamageLedger(new GameRules());
        Assert.Equal(1800, new GameRules().PropDamageResetTicks);
        ledger.Record(_planet, Prop(5), new Tick(100), 1);
        ledger.Record(_planet, Prop(6), new Tick(100), 2);

        // A prop without an entry has its full hit points; the entry holds until 1800 ticks have passed since the last hit.
        Assert.Equal(Full, ledger.Remaining(_planet, Prop(7), new Tick(100), Full));
        Assert.Equal(1, ledger.Remaining(_planet, Prop(5), new Tick(100), Full));
        Assert.Equal(1, ledger.Remaining(_planet, Prop(5), new Tick(100 + 1799), Full));
        Assert.Equal(2, ledger.Remaining(_planet, Prop(6), new Tick(100 + 1799), Full));
        Assert.Equal(Full, ledger.Remaining(_planet, Prop(5), new Tick(100 + 1800), Full));
        Assert.Equal(Full, ledger.Remaining(_planet, Prop(6), new Tick(100 + 1800), Full));
    }

    [Fact]
    public void RemoveOnHarvest()
    {
        var ledger = Create();
        ledger.Record(_planet, Prop(5), new Tick(10), 1);
        ledger.Record(_planet, Prop(6), new Tick(10), 2);
        Assert.Equal(2, ledger.Count);
        Assert.Equal(1, ledger.Remaining(_planet, Prop(5), new Tick(11), Full));

        ledger.Remove(_planet, Prop(5));

        // The harvested prop is forgotten (a regrown one starts with full hit points); the other entry stays.
        Assert.Equal(1, ledger.Count);
        Assert.Equal(Full, ledger.Remaining(_planet, Prop(5), new Tick(11), Full));
        Assert.Equal(2, ledger.Remaining(_planet, Prop(6), new Tick(11), Full));

        ledger.Remove(_planet, Prop(5));
        Assert.Equal(1, ledger.Count);
    }
}
