namespace Sportarr.Api.Models;

/// <summary>
/// Which builder produced a query, and therefore which specificity scale
/// <see cref="QueryCandidate.SpecificityRank"/> is measured on. The values
/// mirror the query types EventQueryService already logs.
/// </summary>
public enum QueryKind
{
    Template,
    Motorsport,
    Wrestling,
    Fighting,
    TeamSport,
    Fallback
}

/// <summary>
/// Why a candidate query, or a league name form, did not make it into the
/// executed search. Every dropped item carries one - a query that vanishes
/// without a reason is a bug, not a budget decision.
/// </summary>
public enum QueryDropReason
{
    /// <summary>More alias-expansion queries than MaxAliasExpansionPerEvent. Ordinary and expected.</summary>
    AliasBudgetExceeded,

    /// <summary>Above HardQueryCeiling. A runaway guard - reaching it means a builder regressed.</summary>
    HardQueryCeilingExceeded,

    /// <summary>A valid alias beyond the three-form cap. Reported so the UI can say it was not searched.</summary>
    AliasFormLimit
}

/// <summary>
/// One effective league-name form after case-insensitive deduplication.
/// <paramref name="Value"/> is the text that goes into queries;
/// <paramref name="Source"/> is the highest-priority provenance and
/// <paramref name="ContributingSources"/> lists every source that produced
/// this same text, so the preview can explain a form the user typed that
/// upstream also publishes.
/// </summary>
public sealed record LeagueNameForm(
    string Value,
    LeagueNameFormSource Source,
    int OrderIndex,
    IReadOnlyList<LeagueNameFormSource> ContributingSources);

/// <summary>
/// A league name form that was valid but not searched, with the reason.
/// </summary>
public sealed record ExcludedLeagueNameForm(
    LeagueNameForm Form,
    QueryDropReason Reason);

/// <summary>
/// One query the plan considered, with the provenance needed to explain it
/// later. Provenance is recorded here, never reconstructed by parsing the
/// final query text.
/// </summary>
public sealed record QueryCandidate(
    string Text,
    string LeagueNameForm,
    LeagueNameFormSource FormSource,
    QueryKind Kind,
    int SpecificityRank,
    int AliasOrderIndex,
    int? TemplateIndex,
    int? TeamAliasSlot,
    bool IsMandatory,
    bool IsSelected,
    QueryDropReason? DropReason,
    IReadOnlyList<LeagueNameForm> ContributingForms);

/// <summary>
/// The whole planned search for one event: every candidate, what was
/// selected, what was dropped and why, which name forms were excluded, and
/// the two budget bounds. Drives execution, logging, and preview alike.
/// </summary>
public sealed record QueryPlan(
    IReadOnlyList<QueryCandidate> Candidates,
    IReadOnlyList<QueryCandidate> SelectedQueries,
    IReadOnlyList<QueryCandidate> DroppedQueries,
    IReadOnlyList<ExcludedLeagueNameForm> ExcludedNameForms,
    int AliasBudgetUsed,
    int AliasBudgetLimit,
    int HardQueryCeiling,
    bool IsTruncated,
    bool MandatoryInvariantViolated);

/// <summary>
/// Planning inputs that are not (yet) saved on the League - what the league
/// settings form is currently showing. Every value falls back to the tracked
/// League when null, so planning a preview never has to mutate the entity.
/// </summary>
public sealed record QueryPlanningOptions(
    string? UserAliases,
    IReadOnlyList<LeagueAliasOrderEntry>? AliasSearchOrder,
    string? SearchQueryTemplate);
