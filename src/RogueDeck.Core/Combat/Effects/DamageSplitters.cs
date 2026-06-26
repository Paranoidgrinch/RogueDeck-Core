namespace RogueDeck.Core.Combat;

// One recipient of a redistributed hit: a combatant and the amount of the split that lands on it.
public readonly record struct DamageShare(CombatantId CombatantId, int Amount);

// Structured result returned by IDamageSplitter.Split. Sealed hierarchy mirrors the other damage-path
// pipelines (amount modifiers, pre-down interceptors).
//   None — no split; the hit lands on the original target as normal.
//   Split(shares) — the post-modifier amount is redistributed across the listed recipients instead of
//                   hitting the original target directly. Each share is then dealt as its own hit
//                   (block + HP + events apply per recipient), but as a redistributed share it neither
//                   re-runs the amount-modifier pipeline (the amounts are already final) nor re-enters
//                   the splitter (so symmetric links do not cascade).
public abstract class DamageSplitResult
{
    public static readonly DamageSplitResult None = new NoneResult();

    public static DamageSplitResult Split(IReadOnlyList<DamageShare> shares) => new SplitResult(shares);

    private DamageSplitResult() { }

    internal sealed class NoneResult : DamageSplitResult { }

    internal sealed class SplitResult : DamageSplitResult
    {
        public IReadOnlyList<DamageShare> Shares { get; }
        public SplitResult(IReadOnlyList<DamageShare> shares)
        {
            ArgumentNullException.ThrowIfNull(shares);
            Shares = shares;
        }
    }
}

// Context for a damage-split decision: a hit on Target carrying the post-modifier Amount (before block)
// is about to be applied.
public sealed record DamageSplitContext(
    CombatState Combat,
    CombatDefinitionRegistry Registry,
    CombatantState Target,
    int Amount,
    CombatantId? SourceCombatantId);

// Redistributes an incoming hit across multiple combatants at resolve time, before block/HP. Registered
// on the registry (ordered by Priority then SplitterId) and consulted in order on the *original* hit
// only; the first non-None result wins. Symmetric "linked"/"shared damage" effects (e.g. Symbiosis,
// 50/50 split across two bonded units) are splitters over this hook. Mirrors IDamageAmountModifier /
// IPreDownInterceptor so the damage path stays one consistent, modular pipeline family.
public interface IDamageSplitter
{
    string SplitterId { get; }

    int Priority { get; }

    DamageSplitResult Split(DamageSplitContext context);
}
