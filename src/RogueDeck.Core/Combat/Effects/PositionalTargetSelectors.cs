namespace RogueDeck.Core.Combat;

// Positional (2D-grid) target selectors — the P1 spatial targeting vocabulary. Each reads the source combatant's
// grid Position and resolves relative to it. All are strictly opt-in and "graceful on absence": if the source is
// unplaced (null Position) they resolve to EMPTY, and any candidate without a Position is skipped. So in a flat
// combat (no positions — today's default) every one of these resolves to empty and never throws; existing content
// never references them, so they never fire there. X is the column/lane, Y the depth/row; "front/back" is computed
// team-relative along Y (front = the end of the enemy team nearest the source's team). Cells are non-exclusive, so
// several combatants may share a cell. Additive: registered alongside the existing selectors, nothing else changes.
internal static class PositionalTargeting
{
    // Living combatants OTHER than the source that carry a grid position — the universe the "relation to source"
    // selectors (adjacency, same column/row) filter over. Unplaced combatants are invisible to spatial queries.
    public static IEnumerable<CombatantState> PositionedOthers(
        CombatantTargetSelectionContext context, CombatantState source) =>
        context.Combat.Combatants.Where(c =>
            c.IsAlive && c.Position is not null && !ReferenceEquals(c, source));

    // Living combatants (INCLUDING the source) that carry a grid position — the universe the "whole line" selectors
    // (all-in-column / all-in-row) filter over, so the source's own cell is part of the line.
    public static IEnumerable<CombatantState> PositionedAll(CombatantTargetSelectionContext context) =>
        context.Combat.Combatants.Where(c => c.IsAlive && c.Position is not null);

    // Living, positioned combatants on the opposing team of the source — the universe the enemy-facing selectors
    // (frontmost / backmost / nearest / opposing-in-column) filter over.
    public static List<CombatantState> PositionedEnemies(
        CombatantTargetSelectionContext context, CombatantState source) =>
        context.Combat.Combatants
            .Where(c => c.IsAlive && c.Position is not null && c.TeamId != source.TeamId)
            .ToList();

    // Manhattan (orthogonal) grid distance between two cells.
    public static int ManhattanDistance(CombatPosition a, CombatPosition b) =>
        Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);

    // Forward sign along Y for the source's team: +1 when the enemy team sits at greater mean depth, -1 when lesser,
    // +1 as the default when the two teams' mean depth coincides. "Front" of the enemy team is the end with the
    // smallest forward coordinate (nearest the source's team); "back" the largest. Team-relative, so a mirrored
    // combat reads the same.
    public static int ForwardSign(
        CombatantTargetSelectionContext context, CombatantState source, IReadOnlyList<CombatantState> enemies)
    {
        double sourceTeamY = context.Combat.Combatants
            .Where(c => c.IsAlive && c.Position is not null && c.TeamId == source.TeamId)
            .Select(c => (double)c.Position!.Value.Y)
            .DefaultIfEmpty(source.Position!.Value.Y)
            .Average();

        double enemyTeamY = enemies.Select(c => (double)c.Position!.Value.Y).Average();

        int sign = Math.Sign(enemyTeamY - sourceTeamY);
        return sign == 0 ? 1 : sign;
    }
}

// Living combatants (excluding the source) orthogonally adjacent to it — Manhattan grid distance exactly 1.
// Any team, so it composes with team selectors via Union/Except; use OpposingInColumn for the enemy-only case.
public sealed class AdjacentToSourceCombatantTargetSelector : ICombatantTargetSelector
{
    public IReadOnlyCollection<CombatantId> ResolveTargets(CombatantTargetSelectionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Source is not { Position: { } origin } source)
            return Array.Empty<CombatantId>();

        return PositionalTargeting.PositionedOthers(context, source)
            .Where(c => PositionalTargeting.ManhattanDistance(origin, c.Position!.Value) == 1)
            .Select(c => c.Id)
            .ToArray();
    }
}

// Living combatants (EXCLUDING the source) sharing the source's column (same X). "The others in my lane."
public sealed class SameColumnAsSourceCombatantTargetSelector : ICombatantTargetSelector
{
    public IReadOnlyCollection<CombatantId> ResolveTargets(CombatantTargetSelectionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Source is not { Position: { } origin } source)
            return Array.Empty<CombatantId>();

        return PositionalTargeting.PositionedOthers(context, source)
            .Where(c => c.Position!.Value.X == origin.X)
            .Select(c => c.Id)
            .ToArray();
    }
}

// Living combatants (EXCLUDING the source) sharing the source's row (same Y). "The others in my row."
public sealed class SameRowAsSourceCombatantTargetSelector : ICombatantTargetSelector
{
    public IReadOnlyCollection<CombatantId> ResolveTargets(CombatantTargetSelectionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Source is not { Position: { } origin } source)
            return Array.Empty<CombatantId>();

        return PositionalTargeting.PositionedOthers(context, source)
            .Where(c => c.Position!.Value.Y == origin.Y)
            .Select(c => c.Id)
            .ToArray();
    }
}

// Every living combatant in the source's column (same X), INCLUDING the source — a full-lane line for a column AoE.
public sealed class AllInSourceColumnCombatantTargetSelector : ICombatantTargetSelector
{
    public IReadOnlyCollection<CombatantId> ResolveTargets(CombatantTargetSelectionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Source is not { Position: { } origin })
            return Array.Empty<CombatantId>();

        return PositionalTargeting.PositionedAll(context)
            .Where(c => c.Position!.Value.X == origin.X)
            .Select(c => c.Id)
            .ToArray();
    }
}

// Every living combatant in the source's row (same Y), INCLUDING the source — a full-row line for a row AoE.
public sealed class AllInSourceRowCombatantTargetSelector : ICombatantTargetSelector
{
    public IReadOnlyCollection<CombatantId> ResolveTargets(CombatantTargetSelectionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Source is not { Position: { } origin })
            return Array.Empty<CombatantId>();

        return PositionalTargeting.PositionedAll(context)
            .Where(c => c.Position!.Value.Y == origin.Y)
            .Select(c => c.Id)
            .ToArray();
    }
}

// The single enemy at the front of the enemy team (team-relative along Y — the enemy nearest the source's team).
// Ties break by column (X) then combatant id for determinism. Empty when the source or all enemies are unplaced.
public sealed class FrontmostEnemyOfSourceCombatantTargetSelector : ICombatantTargetSelector
{
    public TargetSelectorCardinality Cardinality => TargetSelectorCardinality.ZeroOrOne;

    public IReadOnlyCollection<CombatantId> ResolveTargets(CombatantTargetSelectionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Source is not { Position: not null } source)
            return Array.Empty<CombatantId>();

        var enemies = PositionalTargeting.PositionedEnemies(context, source);
        if (enemies.Count == 0)
            return Array.Empty<CombatantId>();

        int sign = PositionalTargeting.ForwardSign(context, source, enemies);

        return enemies
            .OrderBy(c => sign * c.Position!.Value.Y)
            .ThenBy(c => c.Position!.Value.X)
            .ThenBy(c => c.Id.ToString(), StringComparer.Ordinal)
            .Select(c => c.Id)
            .Take(1)
            .ToArray();
    }
}

// The single enemy at the back of the enemy team (team-relative along Y — the enemy furthest from the source's
// team). Ties break by column (X) then combatant id. Empty when the source or all enemies are unplaced.
public sealed class BackmostEnemyOfSourceCombatantTargetSelector : ICombatantTargetSelector
{
    public TargetSelectorCardinality Cardinality => TargetSelectorCardinality.ZeroOrOne;

    public IReadOnlyCollection<CombatantId> ResolveTargets(CombatantTargetSelectionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Source is not { Position: not null } source)
            return Array.Empty<CombatantId>();

        var enemies = PositionalTargeting.PositionedEnemies(context, source);
        if (enemies.Count == 0)
            return Array.Empty<CombatantId>();

        int sign = PositionalTargeting.ForwardSign(context, source, enemies);

        return enemies
            .OrderByDescending(c => sign * c.Position!.Value.Y)
            .ThenBy(c => c.Position!.Value.X)
            .ThenBy(c => c.Id.ToString(), StringComparer.Ordinal)
            .Select(c => c.Id)
            .Take(1)
            .ToArray();
    }
}

// The single enemy at the smallest grid (Manhattan) distance from the source. Ties break by depth (Y), column (X),
// then combatant id. Empty when the source or all enemies are unplaced.
public sealed class NearestEnemyOfSourceCombatantTargetSelector : ICombatantTargetSelector
{
    public TargetSelectorCardinality Cardinality => TargetSelectorCardinality.ZeroOrOne;

    public IReadOnlyCollection<CombatantId> ResolveTargets(CombatantTargetSelectionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Source is not { Position: { } origin } source)
            return Array.Empty<CombatantId>();

        return PositionalTargeting.PositionedEnemies(context, source)
            .OrderBy(c => PositionalTargeting.ManhattanDistance(origin, c.Position!.Value))
            .ThenBy(c => c.Position!.Value.Y)
            .ThenBy(c => c.Position!.Value.X)
            .ThenBy(c => c.Id.ToString(), StringComparer.Ordinal)
            .Select(c => c.Id)
            .Take(1)
            .ToArray();
    }
}

// Every living enemy sharing the source's column (same X) — the enemies "across the lane" from the source. The
// Inscryption-style lane-duel target. Empty when the source is unplaced or no enemy shares its column.
public sealed class OpposingInColumnCombatantTargetSelector : ICombatantTargetSelector
{
    public IReadOnlyCollection<CombatantId> ResolveTargets(CombatantTargetSelectionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Source is not { Position: { } origin } source)
            return Array.Empty<CombatantId>();

        return PositionalTargeting.PositionedEnemies(context, source)
            .Where(c => c.Position!.Value.X == origin.X)
            .Select(c => c.Id)
            .ToArray();
    }
}
