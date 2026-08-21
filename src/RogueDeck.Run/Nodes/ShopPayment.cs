namespace RogueDeck.Run;

// Something other than the currency that can settle a price at the till. A Voucher is not Gold: it persists, it
// cannot be lost, and spending it is not spending Gold — which matters, because a whole family of relics pays
// back "a share of the Gold actually paid". Modelling credit as its own resource keeps that distinction true by
// construction rather than by bookkeeping.
//
// ValuePerUnit is what one unit settles. Credit is spent in WHOLE units and never overpays: three 10-Gold
// vouchers against a 25-Gold price spend two and leave the third, rather than burning it for nothing.
public sealed record ShopCreditSource(
    RunResourceId Resource,
    int ValuePerUnit = 1,
    // Which price currency this credit can settle. Null ⇒ any.
    RunResourceId? Currency = null);

// Permission to buy what you cannot afford: the shortfall becomes Debt on a counter, up to Max in total. Debt is
// not negative Gold and is not Gold spent — it is a number the content then decides how to collect (typically a
// relic that skims a share of every Gold gain to pay it down).
public sealed record ShopDebtTerms(
    RunCounterId Counter,
    int Max,
    RunResourceId? Currency = null);

// How one price would actually be settled, given what the player is holding: credit first, then the currency
// itself, then debt for whatever is still owed. Credit goes first because it can pay for nothing else, and debt
// goes last because it is the only part that is a promise rather than a payment.
public sealed record ShopPayment(
    bool Affordable,
    int CurrencyPaid,
    int CreditPaid,
    int DebtTaken,
    IReadOnlyList<IRunEffectRequest> Effects)
{
    public static ShopPayment For(
        RunState run,
        RunResourceId currency,
        int price,
        IReadOnlyList<ShopCreditSource> credit,
        IReadOnlyList<ShopDebtTerms> debt)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(credit);
        ArgumentNullException.ThrowIfNull(debt);

        var effects = new List<IRunEffectRequest>();
        var owed = Math.Max(0, price);
        var creditPaid = 0;

        foreach (var source in credit)
        {
            if (owed == 0)
                break;
            if (source.Currency is { } only && only != currency)
                continue;
            if (source.ValuePerUnit <= 0)
                continue;

            var units = Math.Min(run.GetResource(source.Resource), owed / source.ValuePerUnit);
            if (units <= 0)
                continue;

            var settled = units * source.ValuePerUnit;
            owed -= settled;
            creditPaid += settled;
            effects.Add(new ChangeResourceRunEffect(source.Resource, -units));
        }

        var currencyPaid = Math.Min(run.GetResource(currency), owed);
        if (currencyPaid > 0)
        {
            owed -= currencyPaid;
            effects.Add(new ChangeResourceRunEffect(currency, -currencyPaid));
        }

        // The most any single set of terms still allows. Two relics that each permit 100 Debt do not add up to
        // 200 — the more generous one simply wins, which is the reading that cannot surprise a player.
        var debtTaken = 0;
        if (owed > 0)
        {
            var headroom = 0;
            ShopDebtTerms? chosen = null;
            foreach (var terms in debt)
            {
                if (terms.Currency is { } only && only != currency)
                    continue;
                var room = Math.Max(0, terms.Max - run.GetCounter(terms.Counter));
                if (room > headroom)
                    (headroom, chosen) = (room, terms);
            }

            if (chosen is not null)
            {
                debtTaken = Math.Min(headroom, owed);
                owed -= debtTaken;
                effects.Add(new IncrementCounterRunEffect(chosen.Counter, debtTaken));
            }
        }

        return new ShopPayment(owed == 0, currencyPaid, creditPaid, debtTaken, effects);
    }
}
