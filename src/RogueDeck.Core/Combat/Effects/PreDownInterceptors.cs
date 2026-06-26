namespace RogueDeck.Core.Combat;

// Structured result returned by IPreDownInterceptor.Intercept when a hit would reduce a living
// combatant to 0 HP. Sealed hierarchy allows exhaustive matching and extension without interface
// changes.
//   Allow — let the down happen (proceed with the lethal hit as normal).
//   Prevent(survivingHealth) — cancel the down; the target survives at survivingHealth (clamped to
//                              [1, max]). The canonical "death-prevention" shape (e.g. Phoenix).
//   Redirect(redirectTo) — the original target is spared (takes no HP loss) and the full lethal hit
//                          is dealt to redirectTo instead (e.g. Martyr taking an ally's killing blow).
//
// Redirect is loop-safe: DealDamageEffectRequest carries RedirectionDepth; the handler increments it
// for the redirected hit and stops consulting interceptors once it reaches the depth limit.
public abstract class PreDownInterceptionResult
{
    public static readonly PreDownInterceptionResult Allow = new AllowResult();

    public static PreDownInterceptionResult Prevent(int survivingHealth) =>
        new PreventResult(survivingHealth);

    public static PreDownInterceptionResult Redirect(CombatantId redirectTo) =>
        new RedirectResult(redirectTo);

    private PreDownInterceptionResult() { }

    internal sealed class AllowResult : PreDownInterceptionResult { }

    internal sealed class PreventResult : PreDownInterceptionResult
    {
        public int SurvivingHealth { get; }
        public PreventResult(int survivingHealth) => SurvivingHealth = survivingHealth;
    }

    internal sealed class RedirectResult : PreDownInterceptionResult
    {
        public CombatantId RedirectTo { get; }
        public RedirectResult(CombatantId redirectTo) => RedirectTo = redirectTo;
    }
}

// Context for a pre-down interception: a hit on Target would drop it from a living state to 0 HP.
public sealed record PreDownInterceptionContext(
    CombatState Combat,
    CombatDefinitionRegistry Registry,
    CombatantState Target,
    // Post-block, post-modifier damage that would land on HP (the lethal amount).
    int LethalAmount,
    CombatantId? SourceCombatantId);

// Fires when a DealDamage would down a living combatant, before the down happens. Registered on the
// registry (ordered by Priority then InterceptorId) and consulted in order; the first non-Allow result
// wins. An interceptor may mutate state (e.g. consume a charge) inside Intercept, mirroring
// IStatusApplicationInterceptor. Phoenix (prevent + heal), Martyr (redirect to the wearer) and similar
// effects are interceptors over this hook.
public interface IPreDownInterceptor
{
    string InterceptorId { get; }

    int Priority { get; }

    PreDownInterceptionResult Intercept(PreDownInterceptionContext context);
}
