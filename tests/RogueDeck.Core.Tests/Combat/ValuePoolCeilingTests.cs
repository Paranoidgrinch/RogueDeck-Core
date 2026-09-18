using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// The ceiling on a value pool, stated as the four things it must and must not do. The guard exists to stop a
// pool being FILLED past its maximum; it was also stopping an already-overfilled pool from being SPENT, which
// is the opposite of what a ceiling is for. Each test names which of the two it is about, because the pair is
// the whole point: a guard that refuses both looks correct in every test that only ever fills.
public class ValuePoolCeilingTests
{
    [Fact]
    public void A_pool_may_not_be_filled_past_its_ceiling()
    {
        var pool = new ValuePoolState(current: 2, max: 3);

        Assert.Throws<ArgumentOutOfRangeException>(() => pool.SetCurrent(4));
        Assert.Equal(2, pool.Current);
    }

    [Fact]
    public void A_caller_that_says_so_may_fill_past_the_ceiling()
    {
        var pool = new ValuePoolState(current: 3, max: 3);

        pool.SetCurrent(6, allowExceedingMax: true);

        // The ceiling is not changed by the overfill — it is suspended for that one write.
        Assert.Equal(6, pool.Current);
        Assert.Equal(3, pool.Max);
    }

    [Fact]
    public void An_overfilled_pool_may_be_spent_from_while_still_above_its_ceiling()
    {
        var pool = new ValuePoolState(current: 3, max: 3);
        pool.SetCurrent(6, allowExceedingMax: true);

        // 5 is still over the ceiling of 3 — and this write does not CREATE that, it is paying it down.
        // Refusing it is what broke the first card played on a carried-energy turn.
        pool.SetCurrent(5);

        Assert.Equal(5, pool.Current);
    }

    [Fact]
    public void An_overfilled_pool_may_not_be_pushed_higher_still()
    {
        var pool = new ValuePoolState(current: 3, max: 3);
        pool.SetCurrent(6, allowExceedingMax: true);

        // The permission above is for coming DOWN. Going further up is the thing the ceiling is for, and an
        // overfill must not become a licence to keep filling.
        Assert.Throws<ArgumentOutOfRangeException>(() => pool.SetCurrent(7));
        Assert.Equal(6, pool.Current);
    }

    [Fact]
    public void A_pool_that_may_exceed_its_ceiling_by_declaration_is_unaffected()
    {
        var pool = new ValuePoolState(current: 3, max: 3, canExceedMax: true);

        pool.SetCurrent(9);

        Assert.Equal(9, pool.Current);
    }

    [Fact]
    public void A_pool_with_no_ceiling_takes_any_value()
    {
        var pool = new ValuePoolState(current: 0);

        pool.SetCurrent(1000);

        Assert.Equal(1000, pool.Current);
    }
}
