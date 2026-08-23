using Sportarr.Api.Models;

namespace Sportarr.Api.Helpers;

/// <summary>
/// The effective league name forms of one league, in the order searches
/// should use them, plus the valid forms the alias cap excluded.
/// </summary>
public sealed record LeagueQueryFormSet(
    IReadOnlyList<LeagueNameForm> Forms,
    IReadOnlyList<ExcludedLeagueNameForm> ExcludedForms);

/// <summary>
/// Builds the ordered league name forms a search may use.
///
/// Order without a saved preference is built-in forms, the canonical name,
/// user aliases, then upstream aliases: explicit user intent should not be
/// crowded out by poor upstream metadata. A saved AliasSearchOrder reorders
/// what remains, matched by normalized value rather than by source, so an
/// alias that moves between sources keeps its saved position.
///
/// This is deliberately NOT the same list as
/// <see cref="LeagueAliasHelper.GetMatchingAliases"/>, and neither derives
/// from the other. That list answers "does this release name identify this
/// league?" and may therefore be generous - it adds a generated abbreviation
/// ("Formula 1" -> "F1") that would be a terrible thing to search for on its
/// own. This one answers "what do we type into an indexer?" and must keep
/// alias strings exactly as entered, because passing them through
/// canonical-name recognition would collapse them back onto the same series
/// key and make the expansion pointless.
///
/// The relationship that must hold is one-directional: every alias form this
/// searches with must be a form GetMatchingAliases will match, or a release
/// found only through that form could never pass league identity. See
/// QueryPlanTests.EverySearchedAliasForm_IsAFormLeagueAliasHelperWillAlsoMatch.
/// Built-in forms are the known exception - they are query spellings
/// ("Formula1"), not league identities.
///
/// Alias forms are capped at <see cref="MaxAliasForms"/> after
/// deduplication. Built-in and canonical forms are free - "Formula 1" and
/// "Formula1" are the same league, not two of the user's alias slots.
/// </summary>
public static class LeagueQueryForms
{
    /// <summary>How many user/upstream alias forms a league may search with.</summary>
    public const int MaxAliasForms = 3;

    public static LeagueQueryFormSet Build(
        League? league,
        IReadOnlyList<string>? builtInForms = null,
        QueryPlanningOptions? options = null)
    {
        // Aliases are trimmed and otherwise used exactly as entered. Passing
        // them through canonical-name recognition would collapse them back
        // onto the same series key and make the expansion pointless.
        var candidates = new List<(string Value, LeagueNameFormSource Source)>();

        foreach (var form in builtInForms ?? [])
        {
            if (!string.IsNullOrWhiteSpace(form))
            {
                candidates.Add((form.Trim(), LeagueNameFormSource.BuiltIn));
            }
        }

        if (!string.IsNullOrWhiteSpace(league?.Name))
        {
            candidates.Add((league.Name.Trim(), LeagueNameFormSource.Canonical));
        }

        foreach (var alias in AliasField.Parse(options?.UserAliases ?? league?.UserAliases))
        {
            candidates.Add((alias, LeagueNameFormSource.UserAlias));
        }

        foreach (var alias in AliasField.Parse(league?.AlternateName))
        {
            candidates.Add((alias, LeagueNameFormSource.UpstreamAlias));
        }

        // Deduplicate first: the highest-priority source wins the form, and
        // every other source that produced the same text is recorded so the
        // preview can explain it.
        var effective = new List<(string Value, LeagueNameFormSource Source, List<LeagueNameFormSource> Sources)>();
        foreach (var (value, source) in candidates)
        {
            var existing = effective.FindIndex(form =>
                string.Equals(form.Value, value, StringComparison.OrdinalIgnoreCase));
            if (existing >= 0)
            {
                if (!effective[existing].Sources.Contains(source))
                {
                    effective[existing].Sources.Add(source);
                }
                continue;
            }

            effective.Add((value, source, [source]));
        }

        // Reconcile the saved order by normalized value AFTER deduplication:
        // saved entries the league no longer has are ignored (but stay in
        // storage), and forms the saved order never saw append at the end
        // without disturbing saved positions.
        var savedOrder = options?.AliasSearchOrder ?? league?.AliasSearchOrder;
        if (savedOrder is { Count: > 0 })
        {
            var positions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < savedOrder.Count; i++)
            {
                var value = savedOrder[i].Value?.Trim();
                if (!string.IsNullOrEmpty(value))
                {
                    positions.TryAdd(value, i);
                }
            }

            effective = effective
                .Select((form, naturalIndex) => (form, naturalIndex))
                .OrderBy(entry => positions.TryGetValue(entry.form.Value, out var saved) ? saved : int.MaxValue)
                .ThenBy(entry => entry.naturalIndex)
                .Select(entry => entry.form)
                .ToList();
        }

        var forms = new List<LeagueNameForm>();
        var excluded = new List<ExcludedLeagueNameForm>();
        var aliasFormsUsed = 0;

        foreach (var (value, source, sources) in effective)
        {
            var isAlias = source is LeagueNameFormSource.UserAlias or LeagueNameFormSource.UpstreamAlias;
            if (isAlias && aliasFormsUsed >= MaxAliasForms)
            {
                excluded.Add(new ExcludedLeagueNameForm(
                    new LeagueNameForm(value, source, excluded.Count, sources),
                    QueryDropReason.AliasFormLimit));
                continue;
            }

            if (isAlias)
            {
                aliasFormsUsed++;
            }

            forms.Add(new LeagueNameForm(value, source, forms.Count, sources));
        }

        return new LeagueQueryFormSet(forms, excluded);
    }
}
