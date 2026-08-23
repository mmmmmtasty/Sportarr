using Sportarr.Api.Helpers;
using System.Text.RegularExpressions;
using Sportarr.Api.Models;

namespace Sportarr.Api.Services;

/// <summary>
/// Universal event query service for all sports.
/// Builds search queries based on sport type, league, and teams,
/// using scene naming conventions.
/// </summary>
public class EventQueryService
{
    private readonly ILogger<EventQueryService> _logger;

    public EventQueryService(ILogger<EventQueryService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// The canonical set of tokens BuildQueryFromTemplate substitutes, in the
    /// exact order they are applied. This is the single source of truth
    /// SearchTemplateTokensTests compares against SearchTemplateTokens.All so
    /// the catalog and the builder can never drift apart again (#see
    /// SearchTemplateTokens for the history).
    ///
    /// Order matters: {Round:00} and {Round:0} must be applied before the
    /// bare {Round}, and {Stage:00}/{Stage:0} before the bare {Stage}, so a
    /// formatted token is never left partially matched by its shorter
    /// prefix.
    /// </summary>
    internal static readonly IReadOnlyList<string> SupportedTemplateTokens = new[]
    {
        "{League}", "{Year}", "{Month}", "{Day}",
        "{Round:00}", "{Round:0}", "{Round}",
        "{Stage:00}", "{Stage:0}", "{Stage}",
        "{Week}", "{EventTitle}", "{EventName}",
        "{HomeTeam}", "{AwayTeam}", "{vs}",
        "{Season}", "{Part}", "{EventType}",
    };

    /// <summary>
    /// Build a search query from a custom template.
    /// Supports tokens: {League}, {Year}, {Month}, {Day}, {Round}, {Round:00}, {Round:0}, {Week}, {EventTitle},
    /// {EventName}, {Stage}, {Stage:00}, {Stage:0}, {HomeTeam}, {AwayTeam}, {vs}, {Season}, {Part}, {EventType}
    ///
    /// Round format options:
    /// - {Round} or {Round:00} - Zero-padded to 2 digits (e.g., "01", "22") - default for compatibility
    /// - {Round:0} - No padding (e.g., "1", "22")
    ///
    /// {Stage} is the stage number of a stage race, read from the title
    /// ("Tour de France Stage 16" gives "16"). It is empty when the title
    /// names no stage. Use it to search in another language, for example
    /// "{EventName} {Year} Etappe {Stage} German". {Stage} does not pad by
    /// default because release names write "Stage.16", not "Stage.016".
    ///
    /// {Part} is the part being searched (Prelims, Main Card, ...) and empty
    /// for a whole-event search. {EventType} is the detected fighting event
    /// type in query-friendly spacing (PPV, Fight Night, Contender Series,
    /// Weekly, ...) and empty when the title doesn't classify.
    /// </summary>
    /// <param name="template">The template string with tokens</param>
    /// <param name="evt">The event to extract values from</param>
    /// <param name="part">The part being searched, when a specific part is targeted</param>
    /// <param name="homeTeamName">Override for {HomeTeam} (used for user-alias query variants)</param>
    /// <param name="awayTeamName">Override for {AwayTeam} (used for user-alias query variants)</param>
    /// <param name="leagueNameOverride">Override for {League} (used for league-alias query variants)</param>
    /// <returns>The processed query string with tokens replaced</returns>
    public string BuildQueryFromTemplate(string template, Event evt, string? part = null,
        string? homeTeamName = null, string? awayTeamName = null, string? leagueNameOverride = null)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            _logger.LogWarning("[EventQuery] Empty template provided, falling back to default query");
            return BuildEventQueries(evt).FirstOrDefault() ?? evt.Title;
        }

        var result = template;

        // League name (normalized - remove spaces, use abbreviations)
        var leagueName = evt.League?.Name ?? "";
        // A league-alias expansion supplies the name form verbatim. Passing
        // it through GetNormalizedLeagueNameForTemplate would collapse it
        // back onto the canonical abbreviation and make the expansion moot.
        var normalizedLeague = leagueNameOverride ?? GetNormalizedLeagueNameForTemplate(leagueName);

        // Date components - prefer the broadcast-local date so end-of-day shows
        // (AEW Dec 31 8pm Eastern = Jan 1 UTC) are queried by their broadcast
        // date, matching how indexer releases are named.
        var queryDate = evt.BroadcastDate ?? evt.EventDate.Date;

        // Round number (for motorsports) with format options
        // {Round} or {Round:00} = zero-padded (01, 02, ... 22)
        // {Round:0} = no padding (1, 2, ... 22)
        var round = evt.Round ?? "";
        string roundPadded, roundUnpadded;
        if (int.TryParse(round, out var roundNum))
        {
            roundPadded = roundNum.ToString("D2");
            roundUnpadded = roundNum.ToString();
        }
        else
        {
            // Non-numeric round value - use as-is for all variants
            roundPadded = round;
            roundUnpadded = round;
        }

        // Stage number of a stage race. Round holds a season-wide event
        // index for these leagues, so it can not name a single stage.
        var stage = ExtractStageNumber(evt.Title);
        var stageText = stage?.ToString() ?? "";
        var stagePadded = stage?.ToString("D2") ?? "";

        // Week number (for team sports)
        var weekNumber = GetWeekNumber(evt);

        // Event name with the trailing fighter matchup or stage number
        // stripped. Fighting releases name the card ("ONE Friday Fights 150")
        // but not the fighters. Stage-race releases name the race in the
        // user's own language, so the English "Stage 16" suffix must go.
        var eventName = StripStageFromTitle(StripFightersFromTitle(evt.Title ?? ""));

        // Team names. Reading only the HomeTeam/AwayTeam navigations left
        // these tokens empty for every league without linked Team rows, which
        // is most of them. ResolveTeamNames reads the denormalized name
        // columns first, the same way the reversed-order fallback does.
        var (resolvedHome, resolvedAway) = ResolveTeamNames(evt);

        // Replacement values keyed by the same canonical token constants the
        // catalog uses (SupportedTemplateTokens), applied in that array's
        // order so {Round:00}/{Round:0} and {Stage:00}/{Stage:0} are
        // substituted before their shorter {Round}/{Stage} prefixes could
        // otherwise partially consume them.
        var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["{League}"] = normalizedLeague,
            ["{Year}"] = queryDate.Year.ToString(),
            ["{Month}"] = queryDate.Month.ToString("D2"),
            ["{Day}"] = queryDate.Day.ToString("D2"),
            ["{Round:00}"] = roundPadded,
            ["{Round:0}"] = roundUnpadded,
            ["{Round}"] = roundPadded,
            ["{Stage:00}"] = stagePadded,
            ["{Stage:0}"] = stageText,
            ["{Stage}"] = stageText,
            ["{Week}"] = weekNumber?.ToString() ?? "",
            ["{EventTitle}"] = evt.Title ?? "",
            ["{EventName}"] = eventName,
            ["{HomeTeam}"] = homeTeamName ?? resolvedHome ?? "",
            ["{AwayTeam}"] = awayTeamName ?? resolvedAway ?? "",
            ["{vs}"] = "vs",
            ["{Season}"] = evt.Season ?? "",
            ["{Part}"] = part ?? "",
        };

        // Detected fighting event type, spaced for release-name matching
        // ("FightNight" -> "Fight Night", "ContenderSeries" -> "Contender
        // Series"); empty when the title doesn't classify. Computed lazily -
        // only when the template actually asks for it - since classification
        // does real work.
        if (result.Contains("{EventType}", StringComparison.OrdinalIgnoreCase))
        {
            var typeName = EventPartDetector.DetectFightingEventTypeName(evt.Title ?? "", evt.League?.Name);
            replacements["{EventType}"] = SpacePascalCase(typeName);
        }

        foreach (var token in SupportedTemplateTokens)
        {
            if (replacements.TryGetValue(token, out var value))
            {
                result = result.Replace(token, value, StringComparison.OrdinalIgnoreCase);
            }
        }

        // Clean up any double spaces
        while (result.Contains("  "))
        {
            result = result.Replace("  ", " ");
        }

        _logger.LogInformation("[EventQuery] Built query from template: '{Template}' -> '{Result}' for event '{EventTitle}'",
            template, result.Trim(), evt.Title);

        return result.Trim();
    }

    /// <summary>
    /// Space out a PascalCase identifier for use inside a search query:
    /// "FightNight" -> "Fight Night". All-caps identifiers (PPV, PLE, SNME)
    /// pass through unchanged.
    /// </summary>
    private static string SpacePascalCase(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }
        return System.Text.RegularExpressions.Regex.Replace(value, "(?<=[a-z])(?=[A-Z])", " ");
    }

    /// <summary>
    /// Get normalized league name for template replacement.
    /// Returns abbreviations where appropriate (NFL, NBA, UFC, etc.)
    /// </summary>
    private string GetNormalizedLeagueNameForTemplate(string leagueName)
    {
        if (string.IsNullOrEmpty(leagueName)) return "";

        var lower = leagueName.ToLowerInvariant();

        // Common abbreviations
        if (lower.Contains("national basketball association") || lower == "nba")
            return "NBA";
        if (lower.Contains("national football league") || lower == "nfl")
            return "NFL";
        if (lower.Contains("national hockey league") || lower == "nhl")
            return "NHL";
        if (lower.Contains("major league baseball") || lower == "mlb")
            return "MLB";
        if (lower.Contains("ultimate fighting championship") || lower == "ufc")
            return "UFC";
        if (lower.Contains("formula 1") || lower.Contains("formula one") || lower == "f1")
            return "Formula1";
        if (lower.Contains("formula e") || lower.Contains("formulae"))
            return "FormulaE";
        if (lower.Contains("motogp"))
            return "MotoGP";
        if (lower.Contains("nascar"))
            return "NASCAR";
        if (lower.Contains("indycar"))
            return "IndyCar";

        // Default: remove spaces for cleaner queries
        return leagueName.Replace(" ", "");
    }

    /// <summary>
    /// Build search queries for an event based on its sport type and data.
    ///
    /// TWO-QUERY FALLBACK STRATEGY:
    /// Returns up to 2 queries: a specific primary query + a broader fallback.
    /// The search loop (Program.cs / AutomaticSearchService) iterates through queries
    /// and stops early when sufficient results are found (>=10 manual, >=3 automatic).
    /// This limits API calls to at most 2 per indexer per search.
    ///
    /// Examples:
    /// - F1 Round 2 2026 -> Primary: "Formula1 2026 Round02", Fallback: "Formula1 2026"
    /// - WWE RAW 2026-03-02 -> Primary: "WWE RAW 2026 03 02", Fallback: "WWE RAW 2026 03"
    /// - UFC 299 -> Primary: "UFC 299", Fallback: "UFC 2026"
    /// - NFL Dec 2025 -> Primary: "NFL 2025 12", Fallback: "NFL 2025"
    /// </summary>
    /// <param name="evt">The event to build queries for</param>
    /// <param name="part">Optional - IGNORED. Parts are filtered locally from results.</param>
    /// <param name="customTemplate">Optional custom search query template from league settings</param>
    public List<string> BuildEventQueries(Event evt, string? part = null, string? customTemplate = null) =>
        BuildEventQueryPlan(evt, part, customTemplate).SelectedQueries.Select(query => query.Text).ToList();

    /// <summary>
    /// The alias-expansion budget: how many league-alias variants one event
    /// may add on top of its mandatory baseline. Ordinary and expected to be
    /// hit - a league with three aliases and ten templates drops most of its
    /// candidates.
    /// </summary>
    internal const int MaxAliasExpansionPerEvent = 8;

    /// <summary>
    /// A runaway guard, not a product decision. The largest legitimate
    /// baseline is SearchTemplateList.MaxTemplates (10) x canonical-plus-three
    /// team-alias slots (4) = 40, so exceeding this means a builder regressed.
    /// </summary>
    internal const int HardQueryCeiling = 50;

    /// <summary>
    /// The structured form of <see cref="BuildEventQueries"/>: every query
    /// with the league-name form and provenance that produced it, what was
    /// selected, and what was dropped and why. Provenance is recorded here
    /// rather than reconstructed later by parsing query text.
    /// </summary>
    /// <param name="evt">The event to build queries for</param>
    /// <param name="part">Optional - IGNORED. Parts are filtered locally from results.</param>
    /// <param name="customTemplate">Optional custom search query template from league settings</param>
    /// <param name="options">Unsaved planning inputs (settings preview); each value
    /// falls back to the tracked League when null, so planning never mutates the entity.</param>
    public QueryPlan BuildEventQueryPlan(Event evt, string? part = null, string? customTemplate = null,
        QueryPlanningOptions? options = null)
    {
        var sport = evt.Sport ?? "Fighting";
        var leagueName = evt.League?.Name;
        var nameForms = LeagueQueryForms.Build(
            evt.League,
            [GetNormalizedLeagueNameForTemplate(leagueName ?? "")],
            options);
        // The baseline form is the alias-free spelling the existing builders
        // already use: the built-in query spelling first, the canonical name
        // second. Never an alias - a mandatory query must not claim to have
        // come from one.
        var leagueForm = nameForms.Forms.FirstOrDefault(form => form.Source == LeagueNameFormSource.BuiltIn)
            ?? nameForms.Forms.FirstOrDefault(form => form.Source == LeagueNameFormSource.Canonical)
            ?? nameForms.Forms.FirstOrDefault();
        var formSet = new LeagueFormSet(
            leagueForm,
            nameForms.Forms
                .Where(form => form.Source is LeagueNameFormSource.UserAlias or LeagueNameFormSource.UpstreamAlias)
                .ToList(),
            nameForms.Forms);
        var hasSavedAliasOrder = (options?.AliasSearchOrder ?? evt.League?.AliasSearchOrder) is { Count: > 0 };

        var candidates = new List<QueryCandidate>();

        // If custom template is provided, use it instead of default logic
        // A league may carry several templates, one per line, because release
        // groups name the same event differently. Each is asked in turn and
        // the results merge, so the first line stays the primary query.
        var customTemplates = SearchTemplateList.Parse(options?.SearchQueryTemplate ?? customTemplate);
        if (customTemplates.Count > 0)
        {
            for (var templateIndex = 0; templateIndex < customTemplates.Count; templateIndex++)
            {
                var template = customTemplates[templateIndex];
                candidates.Add(MandatoryCandidate(BuildQueryFromTemplate(template, evt, part), QueryKind.Template,
                    templateIndex, leagueForm, leagueName, templateIndex, teamAliasSlot: 0));

                // User-defined team aliases exist so releases named in another
                // language match - but a query built from the canonical names
                // never RETURNS those releases from the indexer in the first
                // place (a Cyrillic-only rutracker title has no "Portugal" to
                // hit). Re-expand the template once per alias slot so the
                // indexer is also asked in the alias language.
                var teamAliasSlot = 1;
                foreach (var (home, away) in BuildTeamAliasPairs(evt))
                {
                    candidates.Add(MandatoryCandidate(BuildQueryFromTemplate(template, evt, part, home, away),
                        QueryKind.Template, templateIndex, leagueForm, leagueName, templateIndex, teamAliasSlot));
                    teamAliasSlot++;
                }

                // League-alias expansions re-run the same template and the
                // same team slots with only the {League} token replaced, so
                // the alias changes what the indexer is actually asked for.
                foreach (var aliasForm in formSet.Aliases)
                {
                    candidates.Add(ExpansionCandidate(
                        BuildQueryFromTemplate(template, evt, part, leagueNameOverride: aliasForm.Value),
                        QueryKind.Template, templateIndex, aliasForm, templateIndex, teamAliasSlot: 0));

                    var aliasTeamSlot = 1;
                    foreach (var (home, away) in BuildTeamAliasPairs(evt))
                    {
                        candidates.Add(ExpansionCandidate(
                            BuildQueryFromTemplate(template, evt, part, home, away, aliasForm.Value),
                            QueryKind.Template, templateIndex, aliasForm, templateIndex, aliasTeamSlot));
                        aliasTeamSlot++;
                    }
                }
            }

            var templatePlan = BuildPlan(
                ApplyPlanOrdering(candidates, hasSavedAliasOrder), leagueName, nameForms.ExcludedForms, _logger);

            _logger.LogInformation("[EventQuery] Using {TemplateCount} custom template(s) for '{EventTitle}': primary '{Query}' ({Count} query/queries incl. team aliases)",
                customTemplates.Count, evt.Title, templatePlan.SelectedQueries.FirstOrDefault()?.Text,
                templatePlan.SelectedQueries.Count);
            return templatePlan;
        }

        _logger.LogDebug("[EventQuery] Building queries for '{Title}' | Sport: '{Sport}' | League: '{League}'",
            evt.Title, sport, leagueName ?? "(none)");

        var queries = new List<BuilderQuery>();
        string queryType;
        QueryKind queryKind;

        // Check if this is a motorsport event (checks sport, league, AND event title)
        if (IsMotorsport(sport, leagueName, evt.Title))
        {
            BuildMotorsportQueries(evt, leagueName, formSet, queries);
            queryType = "Motorsport";
            queryKind = QueryKind.Motorsport;
        }
        else if (IsWrestling(sport, leagueName))
        {
            BuildWrestlingQueries(evt, leagueName, formSet, queries);
            queryType = "Wrestling";
            queryKind = QueryKind.Wrestling;
        }
        else if (IsFightingSport(sport, leagueName))
        {
            BuildFightingQueries(evt, leagueName, formSet, queries);
            queryType = "Fighting";
            queryKind = QueryKind.Fighting;
        }
        else if (IsTeamSport(sport, leagueName))
        {
            BuildTeamSportQueries(evt, leagueName, formSet, queries);
            queryType = "TeamSport";
            queryKind = QueryKind.TeamSport;
        }
        else
        {
            BuildFallbackQueries(evt, formSet, queries);
            queryType = "Fallback";
            queryKind = QueryKind.Fallback;
            _logger.LogWarning("[EventQuery] Using fallback query for '{Title}' - Sport '{Sport}' / League '{League}' not recognized",
                evt.Title, sport, leagueName ?? "(none)");
        }

        // Every alias-free query a builder emits is mandatory: adding league
        // aliases must never be able to remove an existing query. Each one
        // carries the name form it was actually built from, recorded at
        // emission by the builder itself.
        candidates.AddRange(queries.Select(query => new QueryCandidate(
            Text: query.Text,
            LeagueNameForm: query.Form?.Value ?? leagueName ?? "",
            FormSource: query.Form?.Source ?? LeagueNameFormSource.Canonical,
            Kind: queryKind,
            SpecificityRank: query.SpecificityRank,
            AliasOrderIndex: query.Form?.OrderIndex ?? 0,
            TemplateIndex: null,
            TeamAliasSlot: query.TeamAliasSlot,
            IsMandatory: query.IsMandatory,
            IsSelected: false,
            DropReason: null,
            ContributingForms: query.Form is null ? [] : [query.Form])));

        var plan = BuildPlan(
            ApplyPlanOrdering(candidates, hasSavedAliasOrder), leagueName, nameForms.ExcludedForms, _logger);

        _logger.LogInformation("[EventQuery] Built {Count} {QueryType} queries for '{EventTitle}': {Queries}",
            plan.SelectedQueries.Count, queryType, evt.Title,
            string.Join(" | ", plan.SelectedQueries.Select(query => query.Text)));

        return plan;
    }

    /// <summary>
    /// A baseline query: alias-free, therefore mandatory and never counted
    /// against the alias-expansion budget.
    /// </summary>
    private static QueryCandidate MandatoryCandidate(string text, QueryKind kind, int specificityRank,
        LeagueNameForm? leagueForm, string? leagueName, int? templateIndex, int? teamAliasSlot) =>
        new(
            Text: text,
            LeagueNameForm: leagueForm?.Value ?? leagueName ?? "",
            FormSource: leagueForm?.Source ?? LeagueNameFormSource.Canonical,
            Kind: kind,
            SpecificityRank: specificityRank,
            AliasOrderIndex: leagueForm?.OrderIndex ?? 0,
            TemplateIndex: templateIndex,
            TeamAliasSlot: teamAliasSlot,
            IsMandatory: true,
            IsSelected: false,
            DropReason: null,
            ContributingForms: leagueForm is null ? [] : [leagueForm]);

    /// <summary>
    /// A league-alias expansion: optional, budgeted, and always attributed to
    /// the alias form it was actually built from.
    /// </summary>
    private static QueryCandidate ExpansionCandidate(string text, QueryKind kind, int specificityRank,
        LeagueNameForm aliasForm, int? templateIndex, int? teamAliasSlot) =>
        new(
            Text: text,
            LeagueNameForm: aliasForm.Value,
            FormSource: aliasForm.Source,
            Kind: kind,
            SpecificityRank: specificityRank,
            AliasOrderIndex: aliasForm.OrderIndex,
            TemplateIndex: templateIndex,
            TeamAliasSlot: teamAliasSlot,
            IsMandatory: false,
            IsSelected: false,
            DropReason: null,
            ContributingForms: [aliasForm]);

    /// <summary>
    /// The league-name forms one builder works with: the alias-free form its
    /// mandatory queries are spelled with, the alias forms its expansions are
    /// spelled with, and every planned form so a built-in query spelling can
    /// be matched back to its recorded provenance.
    /// </summary>
    private sealed record LeagueFormSet(
        LeagueNameForm? Baseline,
        IReadOnlyList<LeagueNameForm> Aliases,
        IReadOnlyList<LeagueNameForm> All);

    /// <summary>
    /// One query a default builder emitted, carrying the league-name form it
    /// was actually built from and where it sits on that builder's
    /// specificity scale. Provenance is recorded here, at emission - never
    /// recovered afterwards by parsing the finished query text.
    /// </summary>
    private sealed record BuilderQuery(
        string Text,
        LeagueNameForm? Form,
        int SpecificityRank,
        bool IsMandatory,
        int? TeamAliasSlot = null);

    /// <summary>
    /// The recorded name form behind a league token a builder interpolated:
    /// the matching planned form when the league has one, otherwise the token
    /// itself as a built-in spelling. Query spellings ("Formula1", "SBK") are
    /// query text rather than league identities, so they may legitimately be
    /// absent from the planned form list.
    ///
    /// An unmatched spelling sorts to the END of the saved order
    /// (<c>forms.All.Count</c>), never the front. A form the user never
    /// ordered must not outrank the ones they did: giving "SBK" index 0 would
    /// hoist every SBK query ahead of its WSBK counterpart at every
    /// specificity rank the moment anything was dragged above "WSBK".
    /// </summary>
    private static LeagueNameForm ResolveTokenForm(LeagueFormSet forms, string value) =>
        forms.All.FirstOrDefault(form => string.Equals(form.Value, value, StringComparison.OrdinalIgnoreCase))
            ?? new LeagueNameForm(value, LeagueNameFormSource.BuiltIn, forms.All.Count, [LeagueNameFormSource.BuiltIn]);

    /// <summary>
    /// The form to record for a query that interpolates no league token at
    /// all - an event-title or surname-matchup query. The canonical league
    /// name is the honest answer: the league identifies the query, but no
    /// spelling of it was used to build the text.
    ///
    /// The canonical form is preferred over the built-in query spelling for
    /// exactly that reason. It is not always available: when the built-in
    /// spelling equals the canonical name (league "NFL" normalizes to
    /// "NFL"), LeagueQueryForms collapses the two into a single form whose
    /// winning source is BuiltIn, so the Canonical lookup misses and this
    /// falls back to the baseline - which is that same, identical string.
    /// </summary>
    private static LeagueNameForm? UntokenizedForm(LeagueFormSet forms) =>
        forms.All.FirstOrDefault(form => form.Source == LeagueNameFormSource.Canonical) ?? forms.Baseline;

    /// <summary>
    /// The execution order of the planned candidates.
    ///
    /// Preserve the legacy form-grouped order for leagues whose users have not
    /// customized alias priority. A future migration may make specificity-first
    /// ordering universal; until then, null means retain existing behavior.
    ///
    /// Once an order IS saved, the user has said aliases matter, so the plan
    /// switches to specificity (template index for the template builder) as
    /// the major tier, the saved alias position second, and the team-alias
    /// slot plus emission position as stable tie-breakers. Template index
    /// leads so alias drag order can never move a later user-authored
    /// template ahead of an earlier one. Reordering is presentation only:
    /// <see cref="BuildPlan"/> still puts the whole mandatory baseline first,
    /// and nothing here changes mandatory or budget classification.
    /// </summary>
    private static IReadOnlyList<QueryCandidate> ApplyPlanOrdering(
        List<QueryCandidate> candidates, bool hasSavedAliasOrder)
    {
        if (!hasSavedAliasOrder)
        {
            return candidates;
        }

        return candidates
            .Select((candidate, position) => (candidate, position))
            .OrderBy(entry => entry.candidate.SpecificityRank)
            .ThenBy(entry => entry.candidate.AliasOrderIndex)
            .ThenBy(entry => entry.candidate.TeamAliasSlot ?? 0)
            .ThenBy(entry => entry.position)
            .Select(entry => entry.candidate)
            .ToList();
    }

    /// <summary>
    /// Deduplicate, then select: every mandatory query unconditionally, then
    /// alias expansions up to <see cref="MaxAliasExpansionPerEvent"/> in the
    /// order the builder produced them (the builder owns priority; this only
    /// enforces the budget). Candidate order is otherwise preserved.
    /// </summary>
    internal static QueryPlan BuildPlan(
        IReadOnlyList<QueryCandidate> candidates,
        string? leagueName,
        IReadOnlyList<ExcludedLeagueNameForm> excludedNameForms,
        ILogger logger)
    {
        // Case-insensitive deduplication happens before budgeting. When the
        // same text has several provenances the mandatory one wins, and the
        // other contributing forms are kept for preview diagnostics.
        var deduplicated = new List<QueryCandidate>();
        // Where each surviving entry sits in emission order. An entry is
        // normally anchored where its text first appeared, but a later
        // MANDATORY candidate absorbing an earlier expansion re-anchors to
        // its own emission position: the baseline belongs where the builder
        // emitted it, not in the expansion's earlier slot, which would sort
        // it ahead of the baselines emitted in between.
        var anchors = new List<int>();
        var positions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var emission = 0; emission < candidates.Count; emission++)
        {
            var candidate = candidates[emission];
            if (!positions.TryGetValue(candidate.Text, out var position))
            {
                positions[candidate.Text] = deduplicated.Count;
                deduplicated.Add(candidate);
                anchors.Add(emission);
                continue;
            }

            var kept = deduplicated[position];
            var winner = !kept.IsMandatory && candidate.IsMandatory ? candidate : kept;
            var loser = ReferenceEquals(winner, kept) ? candidate : kept;

            var contributing = winner.ContributingForms.ToList();
            foreach (var form in loser.ContributingForms)
            {
                if (!contributing.Any(existing =>
                        string.Equals(existing.Value, form.Value, StringComparison.OrdinalIgnoreCase)))
                {
                    contributing.Add(form);
                }
            }

            if (!kept.IsMandatory && candidate.IsMandatory)
            {
                anchors[position] = emission;
            }

            deduplicated[position] = winner with
            {
                IsMandatory = kept.IsMandatory || candidate.IsMandatory,
                ContributingForms = contributing,
            };
        }

        // Re-anchoring is the only thing that can disturb emission order, so
        // a stable sort by anchor restores it for everything else.
        var anchored = deduplicated
            .Select((candidate, index) => (candidate, anchor: anchors[index], index))
            .OrderBy(entry => entry.anchor)
            .ThenBy(entry => entry.index)
            .Select(entry => entry.candidate)
            .ToList();

        // The complete alias-free baseline comes first, selected expansions
        // after it. Builders naturally emit an alias variant right next to
        // the baseline query it was derived from, and letting that ordering
        // through would move the preserved legacy strings apart. The
        // invariant is enforced here, once, instead of being re-derived by
        // every builder. Both partitions keep the builder's own relative
        // order, which is where query priority actually lives.
        var ordered = anchored.Where(candidate => candidate.IsMandatory)
            .Concat(anchored.Where(candidate => !candidate.IsMandatory))
            .ToList();

        var planned = new List<QueryCandidate>(ordered.Count);
        var aliasBudgetUsed = 0;
        foreach (var candidate in ordered)
        {
            if (candidate.IsMandatory || aliasBudgetUsed < MaxAliasExpansionPerEvent)
            {
                if (!candidate.IsMandatory)
                {
                    aliasBudgetUsed++;
                }
                planned.Add(candidate with { IsSelected = true, DropReason = null });
                continue;
            }

            planned.Add(candidate with { IsSelected = false, DropReason = QueryDropReason.AliasBudgetExceeded });
        }

        var budgetDropped = planned.Count(candidate => !candidate.IsSelected);
        if (budgetDropped > 0)
        {
            logger.LogWarning("[EventQuery] Alias expansion truncated for league '{League}': {SelectedCount} queries selected, {DroppedCount} dropped",
                leagueName ?? "(none)", planned.Count - budgetDropped, budgetDropped);
        }

        // The hard ceiling applies only after ordinary selection. Reaching it
        // means a builder regressed, so it keeps the first queries in builder
        // order and says so loudly rather than silently trimming.
        var mandatoryInvariantViolated = false;
        var selectedSoFar = 0;
        for (var i = 0; i < planned.Count; i++)
        {
            if (!planned[i].IsSelected)
            {
                continue;
            }

            selectedSoFar++;
            if (selectedSoFar > HardQueryCeiling)
            {
                planned[i] = planned[i] with
                {
                    IsSelected = false,
                    DropReason = QueryDropReason.HardQueryCeilingExceeded,
                };
                mandatoryInvariantViolated = true;
            }
        }

        if (mandatoryInvariantViolated)
        {
            logger.LogError("[EventQuery] Query builder regression for league '{League}': produced {SelectedCount} selected queries, exceeding the hard ceiling of {Ceiling}; keeping the first {KeptCount} in builder order",
                leagueName ?? "(none)", selectedSoFar, HardQueryCeiling, HardQueryCeiling);
        }

        var selected = planned.Where(candidate => candidate.IsSelected).ToList();
        var dropped = planned.Where(candidate => !candidate.IsSelected).ToList();

        return new QueryPlan(
            Candidates: planned,
            SelectedQueries: selected,
            DroppedQueries: dropped,
            ExcludedNameForms: excludedNameForms,
            AliasBudgetUsed: selected.Count(candidate => !candidate.IsMandatory),
            AliasBudgetLimit: MaxAliasExpansionPerEvent,
            HardQueryCeiling: HardQueryCeiling,
            IsTruncated: dropped.Count > 0,
            MandatoryInvariantViolated: mandatoryInvariantViolated);
    }

    /// <summary>
    /// Check if this is a wrestling show (WWE, AEW) — needs date-based queries, not event-number queries.
    /// Must be checked BEFORE IsFightingSport since wrestling was previously grouped with fighting.
    /// </summary>
    private bool IsWrestling(string sport, string? leagueName)
    {
        var wrestlingKeywords = new[] { "wrestling", "wwe", "aew" };
        var sportLower = sport.ToLowerInvariant();
        var leagueLower = leagueName?.ToLowerInvariant() ?? "";

        return wrestlingKeywords.Any(k => sportLower.Contains(k) || leagueLower.Contains(k));
    }

    /// <summary>
    /// Check if this is a fighting sport (UFC, Boxing, Bellator, etc.)
    /// Excludes wrestling (WWE, AEW) which uses date-based queries instead.
    /// </summary>
    private bool IsFightingSport(string sport, string? leagueName)
    {
        // Exclude wrestling — it has its own query builder
        if (IsWrestling(sport, leagueName))
            return false;

        var fightingKeywords = new[] { "fighting", "combat", "ufc", "mma", "boxing", "bellator", "pfl", "one championship" };
        var sportLower = sport.ToLowerInvariant();
        var leagueLower = leagueName?.ToLowerInvariant() ?? "";

        return fightingKeywords.Any(k => sportLower.Contains(k) || leagueLower.Contains(k));
    }

    /// <summary>
    /// Check if this is a team sport (NFL, NBA, NHL, etc.)
    /// </summary>
    private bool IsTeamSport(string sport, string? leagueName)
    {
        var teamSportKeywords = new[] { "football", "basketball", "hockey", "baseball", "soccer", "rugby", "nfl", "nba", "nhl", "mlb", "mls", "nrl", "premier league", "la liga", "bundesliga" };
        var sportLower = sport.ToLowerInvariant();
        var leagueLower = leagueName?.ToLowerInvariant() ?? "";

        return teamSportKeywords.Any(k => sportLower.Contains(k) || leagueLower.Contains(k));
    }

    /// <summary>
    /// Build motorsport queries: specific (series + year + round) then location fallbacks then broad (series + year).
    ///
    /// For Formula 1 the location-based queries are essential to find BILLIE-style releases
    /// (e.g. Formula1.2026.China.Grand.Prix.Qualifying) which do not contain a round number and
    /// are therefore invisible to the primary round query.
    /// </summary>
    /// <summary>
    /// Adjective-form Grand Prix names mapped to the country noun release
    /// groups actually use. "Belgian Grand Prix" ships as
    /// "Formula.1.2026x10.Belgium.Race", so searching only the title's
    /// "Belgian" misses the race entirely while still finding qualifying
    /// releases that happen to use the adjective (#168).
    /// </summary>
    private static readonly Dictionary<string, string> GpDemonymToCountry = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Australian", "Australia" },
        { "Austrian", "Austria" },
        { "Belgian", "Belgium" },
        { "Brazilian", "Brazil" },
        { "British", "Britain" },
        { "Canadian", "Canada" },
        { "Chinese", "China" },
        { "Dutch", "Netherlands" },
        { "Hungarian", "Hungary" },
        { "Italian", "Italy" },
        { "Japanese", "Japan" },
        { "Mexican", "Mexico" },
        { "Saudi Arabian", "Saudi Arabia" },
        { "Spanish", "Spain" },
        { "United States", "USA" },
    };

    /// <summary>
    /// Motorsport specificity, most specific first: the round, the venue, the
    /// location the title names, that location's country noun, then the broad
    /// season fallback.
    /// </summary>
    private const int MotorsportRoundRank = 0;
    private const int MotorsportLocationRank = 1;
    private const int MotorsportTitleLocationRank = 2;
    private const int MotorsportCountryRank = 3;
    private const int MotorsportSeasonRank = 4;

    private void BuildMotorsportQueries(Event evt, string? leagueName, LeagueFormSet forms, List<BuilderQuery> queries)
    {
        var seriesKey = GetMotorsportSeriesPrefix(leagueName);
        var searchPrefixes = GetMotorsportSearchPrefixes(seriesKey);
        var brandingDate = evt.BroadcastDate ?? evt.EventDate;
        int year;
        if (seriesKey == "FormulaE" && !string.IsNullOrEmpty(evt.Season))
        {
            year = ExtractFormulaESeasonYear(evt.Season, brandingDate.Year);
        }
        else
        {
            year = brandingDate.Year;
        }

        // Compute round and title-derived location once; they're independent of the
        // search-name form below.
        int? round = null;
        if (!string.IsNullOrEmpty(evt.Round) && int.TryParse(evt.Round, out var roundNum) && roundNum > 0 && roundNum < 100)
        {
            round = roundNum;
        }

        // Derive a location word from the event title (e.g. "Chinese" from "Chinese Grand Prix")
        string? titleWord = null;
        var titleLocationMatch = Regex.Match(evt.Title ?? "", @"^([\w\s]+?)\s+Grand Prix", RegexOptions.IgnoreCase);
        if (titleLocationMatch.Success)
        {
            var word = titleLocationMatch.Groups[1].Value.Trim();
            if (!string.IsNullOrEmpty(word) &&
                !string.Equals(word, evt.Location, StringComparison.OrdinalIgnoreCase))
            {
                titleWord = word;
            }
        }

        // Emit the full query set for each search-name form (e.g. "Formula 1" then
        // "Formula1"). Spaced form first so its results win the "found enough, stop"
        // optimization, since the dotted/spaced release convention is the common one.
        // Alias forms join the search-prefix list raw. Passing them through
        // GetMotorsportSeriesPrefix would map every alias straight back onto
        // seriesKey and the expansion would produce nothing new - "Формула 1"
        // has to reach the indexer as "Формула 1", not "Формула1".
        var prefixSources = searchPrefixes
            .Select(prefix => (Prefix: prefix, Form: ResolveTokenForm(forms, prefix), Mandatory: true))
            .Concat(forms.Aliases.Select(alias => (Prefix: alias.Value, Form: alias, Mandatory: false)));

        foreach (var (prefix, form, mandatory) in prefixSources)
        {
            // Primary: series + year + round (specific)
            if (round.HasValue)
            {
                queries.Add(new BuilderQuery($"{prefix} {year} Round{round.Value:D2}", form, MotorsportRoundRank, mandatory));
            }

            // Location queries catch releases named after the venue or country
            // rather than the round ("motogp.2026.italy..."), which an indexer
            // can otherwise bury under the broad season query. Every series
            // needs them, not just Formula 1: the guards below keep a series
            // that names events some other way from emitting junk, because a
            // missing location or a title without a Grand Prix simply adds
            // nothing.
            if (!string.IsNullOrEmpty(evt.Location))
            {
                queries.Add(new BuilderQuery($"{prefix} {year} {evt.Location}", form, MotorsportLocationRank, mandatory));
            }
            if (!string.IsNullOrEmpty(titleWord))
            {
                queries.Add(new BuilderQuery($"{prefix} {year} {titleWord}", form, MotorsportTitleLocationRank, mandatory));

                // Also search the country-noun form of an adjective GP name
                // ("Belgian" -> "Belgium") - the two conventions coexist on
                // the same indexer and neither matches the other as text.
                if (GpDemonymToCountry.TryGetValue(titleWord, out var countryName) &&
                    !string.Equals(countryName, evt.Location, StringComparison.OrdinalIgnoreCase))
                {
                    queries.Add(new BuilderQuery($"{prefix} {year} {countryName}", form, MotorsportCountryRank, mandatory));
                }
            }

            // Broad fallback: series + year catches any remaining naming variants
            queries.Add(new BuilderQuery($"{prefix} {year}", form, MotorsportSeasonRank, mandatory));
        }
    }

    /// <summary>
    /// The promotion tokens a wrestling or fighting query may legitimately
    /// lead with. A league alias replaces this token and nothing else.
    /// </summary>
    private static readonly Regex WrestlingOrgToken =
        new(@"^(?:WWE|AEW)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex FightingOrgToken =
        new(@"^(?:UFC|Bellator|PFL|ONE|Boxing)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// The form behind a fighting query's own leading promotion token, when
    /// it has one. A card query is spelled with the promotion the title
    /// names, which is not always the spelling the league carries. Named for
    /// the regex it applies: wrestling has its own token set, so a wrestling
    /// caller needs its own helper rather than this one.
    /// </summary>
    private static LeagueNameForm? FightingOrgTokenForm(LeagueFormSet forms, string query)
    {
        var match = FightingOrgToken.Match(query);
        return match.Success ? ResolveTokenForm(forms, match.Value) : UntokenizedForm(forms);
    }

    /// <summary>
    /// Add one alias variant per baseline query that leads with a recognized
    /// organization token, replacing only that token and keeping everything
    /// after it - the show and date, the card number and type, the year
    /// suffix. A query with no leading organization token gets nothing: an
    /// arbitrary alias is not evidence that a WWE show is really an AEW show,
    /// and a pure surname matchup names no promotion to substitute for.
    /// </summary>
    private static void AddLeadingOrgTokenVariants(
        List<BuilderQuery> queries, IReadOnlyList<LeagueNameForm> aliasForms, Regex orgToken)
    {
        if (aliasForms.Count == 0)
        {
            return;
        }

        var baseline = queries.Where(query => query.IsMandatory).ToList();
        foreach (var alias in aliasForms)
        {
            foreach (var query in baseline)
            {
                var match = orgToken.Match(query.Text);
                if (!match.Success)
                {
                    continue;
                }

                queries.Add(query with
                {
                    Text = alias.Value + query.Text[match.Length..],
                    Form = alias,
                    IsMandatory = false,
                });
            }
        }
    }

    /// <summary>
    /// Build wrestling queries (WWE, AEW).
    /// Weekly shows use date-based queries; PPVs use event name queries.
    /// </summary>
    private void BuildWrestlingQueries(Event evt, string? leagueName, LeagueFormSet forms, List<BuilderQuery> queries)
    {
        var title = evt.Title ?? "";

        // Determine organization prefix
        var org = "WWE";
        if (leagueName?.Contains("AEW", StringComparison.OrdinalIgnoreCase) == true ||
            title.StartsWith("AEW", StringComparison.OrdinalIgnoreCase))
        {
            org = "AEW";
        }

        // The promotion token is what actually goes into the query, and it is
        // read from the title as often as from the league name, so the form
        // recorded is the one for that token - not whatever spelling the
        // league happens to carry.
        var orgForm = ResolveTokenForm(forms, org);

        // Known weekly shows
        var weeklyShows = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "WWE", new[] { "Raw", "Monday Night Raw", "SmackDown", "Friday Night SmackDown", "NXT", "Main Event" } },
            { "AEW", new[] { "Dynamite", "Rampage", "Collision", "Dark", "Elevation" } }
        };

        // Check if this is a weekly show
        string? matchedShow = null;
        if (weeklyShows.TryGetValue(org, out var shows))
        {
            foreach (var show in shows)
            {
                if (title.Contains(show, StringComparison.OrdinalIgnoreCase))
                {
                    // Use the canonical short name
                    matchedShow = show switch
                    {
                        "Monday Night Raw" => "RAW",
                        "Friday Night SmackDown" => "SmackDown",
                        _ => show
                    };
                    break;
                }
            }
        }

        if (matchedShow != null)
        {
            // Weekly show: date-based queries.
            // Use broadcast-local date so end-of-day Eastern shows like AEW
            // Dynamite "Dec 31, 2025 8pm Eastern" query as 2025-12-31, not the
            // UTC-rolled-over 2026-01-01 that nothing publishes.
            var date = evt.BroadcastDate ?? evt.EventDate.Date;
            queries.Add(new BuilderQuery($"{org} {matchedShow} {date.Year} {date.Month:D2} {date.Day:D2}",
                orgForm, 0, IsMandatory: true));
            // Fallback: "WWE RAW 2026 03" (month-level)
            queries.Add(new BuilderQuery($"{org} {matchedShow} {date.Year} {date.Month:D2}",
                orgForm, 1, IsMandatory: true));

            _logger.LogDebug("[EventQuery] Wrestling weekly show: {Org} {Show} on {Date:yyyy-MM-dd}",
                org, matchedShow, date);
        }
        else
        {
            // PPV or special event: name-based queries
            // Extract event name (strip org prefix and year)
            var eventName = Regex.Replace(title, @"^(?:WWE|AEW)\s+", "", RegexOptions.IgnoreCase).Trim();
            eventName = Regex.Replace(eventName, @"\s+\d{4}$", "").Trim();

            if (!string.IsNullOrEmpty(eventName))
            {
                var brandingYear = (evt.BroadcastDate ?? evt.EventDate).Year;
                // Primary: "WWE WrestleMania 2026"
                queries.Add(new BuilderQuery($"{org} {eventName} {brandingYear}", orgForm, 0, IsMandatory: true));
                // Fallback: "WWE WrestleMania"
                queries.Add(new BuilderQuery($"{org} {eventName}", orgForm, 1, IsMandatory: true));
            }
            else
            {
                queries.Add(new BuilderQuery(NormalizeEventTitle(title), UntokenizedForm(forms), 0, IsMandatory: true));
            }

            _logger.LogDebug("[EventQuery] Wrestling PPV/special: {Org} {EventName}", org, eventName);
        }

        // Alias variants swap the leading promotion token only, so the show
        // and date (or the event name and year) survive intact.
        AddLeadingOrgTokenVariants(queries, forms.Aliases, WrestlingOrgToken);
    }

    /// <summary>
    /// Build fighting sport queries (UFC, Bellator, PFL, ONE, Boxing).
    /// Primary: event number query. Fallback: org + year.
    /// </summary>
    private void BuildFightingQueries(Event evt, string? leagueName, LeagueFormSet forms, List<BuilderQuery> queries)
    {
        var title = evt.Title ?? "";

        // Try to extract org + event number (e.g., "UFC 299", "UFC Fight Night 240")
        var patterns = new[]
        {
            (@"(UFC|Bellator|PFL|ONE)\s+Fight\s+Night\s*(\d+)", "$1 Fight Night $2"),
            (@"(UFC|Bellator|PFL|ONE)\s*(\d+)", "$1 $2"),
        };

        string? primaryQuery = null;
        string? org = null;

        foreach (var (pattern, replacement) in patterns)
        {
            var match = Regex.Match(title, pattern, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                primaryQuery = Regex.Replace(match.Value, pattern, replacement, RegexOptions.IgnoreCase);
                org = match.Groups[1].Value.ToUpperInvariant();
                break;
            }
        }

        if (primaryQuery == null)
        {
            // No org+number pattern matched. Indexer releases name the card,
            // not the fighters - "ONE Friday Fights 150 Kompetch vs Attachai"
            // is published as "ONE Friday Fights 150". Strip the trailing
            // matchup so we query the card name.
            var stripped = StripFightersFromTitle(title);
            if (!string.Equals(stripped, title, StringComparison.Ordinal))
            {
                primaryQuery = stripped;
                var orgMatch = Regex.Match(stripped, @"^(UFC|Bellator|PFL|ONE|Boxing)", RegexOptions.IgnoreCase);
                if (orgMatch.Success) org = orgMatch.Value.ToUpperInvariant();
            }
        }

        var brandingYear = (evt.BroadcastDate ?? evt.EventDate).Year;
        // Surname matchup query ("Wardley vs Dubois"). Fight releases almost
        // never carry first names - "Boxing.2026.05.09.Wardley.vs.Dubois..."
        // is the dominant convention - so a full-name title query returns
        // nothing for matchup-titled events (boxing especially, where the
        // matchup IS the whole title and there's no card number to fall
        // back on).
        string? surnameQuery = null;
        string? reversedSurnameQuery = null;
        if (EventPartDetector.TryExtractFighterSurnames(title, out var surnameA, out var surnameB))
        {
            surnameQuery = $"{surnameA} vs {surnameB}";
            // Billing order isn't stable across sources: promoters, databases,
            // and release groups disagree on who leads the marquee (boxing
            // especially - "Usyk vs Fury" and "Fury vs Usyk" both circulate).
            // Same failure class as the reversed team-sport pairing: an
            // ordered-substring tracker search misses the flipped form.
            reversedSurnameQuery = $"{surnameB} vs {surnameA}";
        }

        if (primaryQuery != null)
        {
            // Primary: "UFC 299" or "ONE Friday Fights 150"
            queries.Add(new BuilderQuery(primaryQuery, FightingOrgTokenForm(forms, primaryQuery), 0, IsMandatory: true));
            // Supplementary: the headline matchup by surname, both orders
            if (surnameQuery != null)
                queries.Add(new BuilderQuery(surnameQuery, UntokenizedForm(forms), 1, IsMandatory: true));
            if (reversedSurnameQuery != null)
                queries.Add(new BuilderQuery(reversedSurnameQuery, UntokenizedForm(forms), 2, IsMandatory: true));
            // Fallback: "UFC 2026"
            if (!string.IsNullOrEmpty(org))
                queries.Add(new BuilderQuery($"{org} {brandingYear}", ResolveTokenForm(forms, org), 3, IsMandatory: true));
        }
        else
        {
            // Couldn't identify the card. For a pure matchup title the surname
            // query is the most specific form that matches release naming, so
            // it leads; the normalized full title stays as a fallback.
            if (surnameQuery != null)
                queries.Add(new BuilderQuery(surnameQuery, UntokenizedForm(forms), 0, IsMandatory: true));
            if (reversedSurnameQuery != null)
                queries.Add(new BuilderQuery(reversedSurnameQuery, UntokenizedForm(forms), 1, IsMandatory: true));
            var normalizedTitle = NormalizeEventTitle(title);
            queries.Add(new BuilderQuery(normalizedTitle, FightingOrgTokenForm(forms, normalizedTitle), 2, IsMandatory: true));

            // Season 10 Contender Series releases are named "UFC Tuesday
            // Night Contender Series S10W01", a different show title and a
            // W where the metadata numbering says episode, so the SxxExx
            // query above finds nothing on full-text indexers.
            var dwcs = Regex.Match(title,
                @"(?:dana\s*white|dwcs|contender\s*series).*?season\s*(\d+)\s*(week|episode|ep\.?)\s*(\d+)",
                RegexOptions.IgnoreCase);
            if (dwcs.Success)
            {
                var s = int.Parse(dwcs.Groups[1].Value);
                var e = int.Parse(dwcs.Groups[3].Value);
                queries.Add(new BuilderQuery($"UFC Tuesday Night Contender Series S{s}W{e:D2}",
                    ResolveTokenForm(forms, "UFC"), 3, IsMandatory: true));
                if (dwcs.Groups[2].Value.StartsWith("w", StringComparison.OrdinalIgnoreCase))
                {
                    // Some groups keep the classic show title with the week
                    // numbering, so that pairing gets its own query too.
                    queries.Add(new BuilderQuery($"Dana Whites Contender Series S{s}W{e:D2}",
                        UntokenizedForm(forms), 4, IsMandatory: true));
                }
            }

            var orgMatch = Regex.Match(title, @"^(UFC|Bellator|PFL|ONE|Boxing)", RegexOptions.IgnoreCase);
            if (orgMatch.Success)
            {
                queries.Add(new BuilderQuery($"{orgMatch.Value.ToUpperInvariant()} {brandingYear}",
                    ResolveTokenForm(forms, orgMatch.Value.ToUpperInvariant()), 5, IsMandatory: true));
            }
        }

        // Only queries that lead with a recognized promotion token gain an
        // alias variant; the surname matchup queries name no promotion, so
        // they stay alias-free.
        AddLeadingOrgTokenVariants(queries, forms.Aliases, FightingOrgToken);
    }

    /// <summary>
    /// Team-sport specificity in the order the builder emits it: the most
    /// specific league query (year plus month, or the event title), its
    /// second form (the broad season query, or the reversed title), then the
    /// team-name pairings.
    /// </summary>
    private const int TeamSportPrimaryRank = 0;
    private const int TeamSportSecondaryRank = 1;
    private const int TeamSportTeamPairRank = 2;

    /// <summary>
    /// Build team sport queries (NFL, NBA, NHL, MLB, etc.).
    /// Primary: league + year + month. Fallback: league + year.
    /// </summary>
    private void BuildTeamSportQueries(Event evt, string? leagueName, LeagueFormSet forms, List<BuilderQuery> queries)
    {
        var leaguePrefix = GetTeamSportLeaguePrefix(leagueName);
        var queryDate = evt.BroadcastDate ?? evt.EventDate.Date;
        var year = queryDate.Year;

        // Record the form for the token this branch actually interpolates -
        // the mapped prefix ("NFL"), or the RAW league name the unmapped
        // branch hands to AddTeamAliasQueries ("Premiership Rugby"). Stamping
        // one baseline form on everything would attribute
        // "Premiership Rugby 2026 Bath Rugby Saracens" to the space-stripped
        // built-in spelling "PremiershipRugby", which appears nowhere in the
        // query and was never used to build it.
        var leagueToken = string.IsNullOrEmpty(leaguePrefix) ? leagueName : leaguePrefix;
        var tokenForm = string.IsNullOrWhiteSpace(leagueToken) ? null : ResolveTokenForm(forms, leagueToken);
        // Event-title queries carry no league token at all.
        var titleForm = UntokenizedForm(forms);

        if (string.IsNullOrEmpty(leaguePrefix))
        {
            queries.Add(new BuilderQuery(NormalizeEventTitle(evt.Title), titleForm, TeamSportPrimaryRank, IsMandatory: true));

            // Some indexers (college sports rip groups especially) title releases in
            // broadcast order rather than the schedule's home/away designation, e.g.
            // "Old Dominion vs South Florida" for a game Sportarr's own data calls
            // "South Florida vs Old Dominion". A literal-title-only query never
            // matches those, so add the reversed pairing as a fallback query.
            //
            // Team names come from the denormalized name columns first: sync
            // writes those for every event, while the HomeTeam/AwayTeam
            // navigations require linked Team rows that many leagues (college
            // sports especially - the very case this fallback exists for)
            // never get. The canonical "Home vs Away" title is the last resort
            // when both are absent.
            var homeName = evt.HomeTeamName ?? evt.HomeTeam?.Name;
            var awayName = evt.AwayTeamName ?? evt.AwayTeam?.Name;
            string? reversed = null;
            if (!string.IsNullOrWhiteSpace(homeName) && !string.IsNullOrWhiteSpace(awayName))
            {
                reversed = $"{awayName} vs {homeName}";
            }
            else
            {
                var parts = evt.Title?.Split(" vs ", 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                if (parts is { Length: 2 })
                {
                    reversed = $"{parts[1]} vs {parts[0]}";
                }
            }

            if (reversed != null && !ContainsQuery(queries, reversed))
            {
                queries.Add(new BuilderQuery(reversed, titleForm, TeamSportSecondaryRank, IsMandatory: true));
            }

            AddTeamAliasQueries(evt, leagueName, year, tokenForm, queries);
            AddLeagueAliasTeamQueries(evt, year, forms, queries);
            return;
        }

        // Prefer broadcast-local date over UTC EventDate so games right at the
        // month boundary aren't queried for the wrong month.
        var month = queryDate.Month;

        // Primary: "NFL 2025 12" (year + month)
        queries.Add(new BuilderQuery($"{leaguePrefix} {year} {month:D2}", tokenForm, TeamSportPrimaryRank, IsMandatory: true));
        // Fallback: "NFL 2025" (year only)
        queries.Add(new BuilderQuery($"{leaguePrefix} {year}", tokenForm, TeamSportSecondaryRank, IsMandatory: true));
        AddTeamAliasQueries(evt, leaguePrefix, year, tokenForm, queries);
        AddLeagueAliasTeamQueries(evt, year, forms, queries);
    }

    private static bool ContainsQuery(List<BuilderQuery> queries, string text) =>
        queries.Any(query => string.Equals(query.Text, text, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// League-alias variants for a team sport, shaped like the team-alias
    /// queries the builder already emits: "{league alias} {year} {home}
    /// {away}", reusing each existing team-alias slot's names rather than
    /// pairing every home alias with every away alias. When the teams cannot
    /// be resolved at all the alias still gets a broad "{alias} {year}".
    ///
    /// This is the only league-alias query a league whose canonical builder
    /// falls back to event titles (Premiership Rugby and friends) ever gets:
    /// its baseline queries carry no league token to substitute into.
    /// </summary>
    private static void AddLeagueAliasTeamQueries(
        Event evt, int year, LeagueFormSet forms, List<BuilderQuery> queries)
    {
        if (forms.Aliases.Count == 0)
        {
            return;
        }

        var (home, away) = ResolveTeamNames(evt);
        var pairs = new List<(string Home, string Away)>();
        if (!string.IsNullOrWhiteSpace(home) && !string.IsNullOrWhiteSpace(away))
        {
            pairs.Add((home, away));
            pairs.AddRange(BuildTeamAliasPairs(evt));
        }

        foreach (var alias in forms.Aliases)
        {
            if (pairs.Count == 0)
            {
                queries.Add(new BuilderQuery($"{alias.Value} {year}", alias, TeamSportSecondaryRank, IsMandatory: false));
                continue;
            }

            for (var slot = 0; slot < pairs.Count; slot++)
            {
                var (pairHome, pairAway) = pairs[slot];
                queries.Add(new BuilderQuery($"{alias.Value} {year} {pairHome} {pairAway}",
                    alias, TeamSportTeamPairRank, IsMandatory: false, TeamAliasSlot: slot));
            }
        }
    }

    /// <summary>
    /// Generic fallback: the normalized event title stays mandatory, and each
    /// league alias adds "{alias} {year} {title}" - or "{alias} {title}" when
    /// the event carries no usable year - so an unrecognized sport still gets
    /// something out of having aliases.
    /// </summary>
    private void BuildFallbackQueries(Event evt, LeagueFormSet forms, List<BuilderQuery> queries)
    {
        var title = NormalizeEventTitle(evt.Title);
        queries.Add(new BuilderQuery(title, UntokenizedForm(forms), 0, IsMandatory: true));

        var year = (evt.BroadcastDate ?? evt.EventDate).Year;
        foreach (var alias in forms.Aliases)
        {
            queries.Add(new BuilderQuery(
                year > 1 ? $"{alias.Value} {year} {title}" : $"{alias.Value} {title}",
                alias, 0, IsMandatory: false));
        }
    }

    /// <summary>
    /// Extra team-sport queries built from user-defined team aliases, so
    /// releases titled in another language are actually RETURNED by the
    /// indexer (matching already understood the aliases; searching did not).
    /// Shape mirrors what works on the trackers those aliases target:
    /// "FIFA World Cup 2026 Португалия Испания".
    /// </summary>
    private void AddTeamAliasQueries(
        Event evt, string? leagueToken, int year, LeagueNameForm? tokenForm, List<BuilderQuery> queries)
    {
        var slot = 1;
        foreach (var (home, away) in BuildTeamAliasPairs(evt))
        {
            var query = string.IsNullOrWhiteSpace(leagueToken)
                ? $"{home} {away} {year}"
                : $"{leagueToken} {year} {home} {away}";
            if (!ContainsQuery(queries, query))
            {
                queries.Add(new BuilderQuery(query, tokenForm, TeamSportTeamPairRank, IsMandatory: true, TeamAliasSlot: slot));
            }
            slot++;
        }
    }

    /// <summary>
    /// Pair the two teams' user aliases slot by slot: alias N of the home
    /// team goes with alias N of the away team, falling back to the
    /// canonical name when one side has fewer aliases. Users naturally list
    /// aliases in the same language order on both teams ("Португалия" and
    /// "Испания" both first), so slot pairing keeps queries single-language
    /// instead of emitting a wasteful full cartesian product. Slots where
    /// both sides fall back to canonical are skipped (that query already
    /// exists), and slots are capped to keep indexers unhammered.
    /// </summary>
    /// <summary>
    /// Home and away names with one precedence everywhere. The denormalized
    /// name columns come first, because sync writes them for every event. The
    /// Team navigations come second, because they need linked Team rows that
    /// many leagues never get. The canonical "Home vs Away" title is the last
    /// resort, and fills only the side that is still missing.
    /// </summary>
    internal static (string? Home, string? Away) ResolveTeamNames(Event evt)
    {
        var home = evt.HomeTeamName ?? evt.HomeTeam?.Name;
        var away = evt.AwayTeamName ?? evt.AwayTeam?.Name;

        if (string.IsNullOrWhiteSpace(home) || string.IsNullOrWhiteSpace(away))
        {
            var parts = evt.Title?.Split(" vs ", 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts is { Length: 2 })
            {
                if (string.IsNullOrWhiteSpace(home)) home = parts[0];
                if (string.IsNullOrWhiteSpace(away)) away = parts[1];
            }
        }

        return (home, away);
    }

    private static IEnumerable<(string Home, string Away)> BuildTeamAliasPairs(Event evt)
    {
        const int maxSlots = 3;

        var homeName = evt.HomeTeam?.Name;
        var awayName = evt.AwayTeam?.Name;
        if (string.IsNullOrWhiteSpace(homeName) || string.IsNullOrWhiteSpace(awayName))
        {
            yield break;
        }

        var homeAliases = ParseUserAliases(evt.HomeTeam?.UserAliases);
        var awayAliases = ParseUserAliases(evt.AwayTeam?.UserAliases);
        var slots = Math.Min(maxSlots, Math.Max(homeAliases.Count, awayAliases.Count));

        for (var i = 0; i < slots; i++)
        {
            var home = i < homeAliases.Count ? homeAliases[i] : homeName;
            var away = i < awayAliases.Count ? awayAliases[i] : awayName;
            if (home == homeName && away == awayName)
            {
                continue;
            }
            yield return (home, away);
        }
    }

    /// <summary>
    /// Same separators the release matcher accepts for the alias field
    /// (comma, pipe, slash) so searching and matching read the field the
    /// same way.
    /// </summary>
    private static List<string> ParseUserAliases(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new List<string>();
        }
        return raw.Split(new[] { ',', '|', '/' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }


    /// <summary>
    /// Extract the second (ending) year from a Formula E season string.
    /// Formula E seasons span two calendar years (e.g., "2019-20", "2024-2025")
    /// and indexer releases use the ending year.
    /// </summary>
    private int ExtractFormulaESeasonYear(string season, int fallbackYear)
    {
        // Handle formats: "2019-20", "2019-2020", "2024-25", "2024-2025"
        var match = Regex.Match(season, @"(\d{4})-(\d{2,4})");
        if (match.Success)
        {
            var startYear = int.Parse(match.Groups[1].Value);
            var endYearStr = match.Groups[2].Value;

            int endYear;
            if (endYearStr.Length == 2)
            {
                // "2019-20" -> 2020 (assume same century as start year)
                var century = (startYear / 100) * 100;
                endYear = century + int.Parse(endYearStr);

                // Handle century rollover (e.g., 1999-00 -> 2000)
                if (endYear <= startYear)
                    endYear += 100;
            }
            else
            {
                // "2019-2020" -> 2020
                endYear = int.Parse(endYearStr);
            }

            return endYear;
        }

        // Single year format (e.g., "2025") - use as-is
        if (int.TryParse(season, out var singleYear))
        {
            return singleYear;
        }

        // Fallback to event date year
        return fallbackYear;
    }



    /// <summary>
    /// Build search queries for a week/round pack release.
    /// Used when individual event releases aren't available.
    /// Example: "NFL-2025-Week15" or "NBA.2025.Week.10"
    /// </summary>
    public List<string> BuildPackQueries(Event evt)
    {
        var queries = new List<string>();
        var leagueName = evt.League?.Name;
        var leaguePrefix = GetTeamSportLeaguePrefix(leagueName);

        if (string.IsNullOrEmpty(leaguePrefix))
        {
            _logger.LogDebug("[EventQuery] Cannot build pack query - no league prefix for {League}", leagueName);
            return queries;
        }

        // Calculate week number from event date
        var weekNumber = GetWeekNumber(evt);
        var year = (evt.BroadcastDate ?? evt.EventDate).Year;

        if (weekNumber.HasValue)
        {
            // Multiple formats for better compatibility - spaces preferred
            queries.Add($"{leaguePrefix} {year} Week{weekNumber}");
            queries.Add($"{leaguePrefix} {year} Week {weekNumber}");
            queries.Add($"{leaguePrefix} {year} W{weekNumber:D2}");

            _logger.LogInformation("[EventQuery] Built pack queries for {League} Week {Week}: {Queries}",
                leaguePrefix, weekNumber, string.Join(" | ", queries));
        }
        else
        {
            _logger.LogDebug("[EventQuery] Cannot determine week number for {Title}", evt.Title);
        }

        return queries;
    }

    /// <summary>
    /// Get the week number for an event based on its date and league season.
    /// For NFL: Week 1 starts first Thursday after Labor Day
    /// For NBA/NHL/MLB: Based on season start date
    /// </summary>
    private int? GetWeekNumber(Event evt)
    {
        var leagueName = evt.League?.Name?.ToLowerInvariant() ?? "";
        // Anchor week math to the broadcast-local date when available.
        // A Sunday-night NFL game whose UTC instant rolls into Monday
        // still belongs to the broadcaster's Sunday week, and a Thursday
        // night game right around Labor Day mustn't slip into the wrong
        // NFL season year just because the UTC clock crossed midnight.
        var eventDate = evt.BroadcastDate ?? evt.EventDate;

        // Try to extract week from event title first (e.g., "Week 15" in title)
        var weekMatch = System.Text.RegularExpressions.Regex.Match(
            evt.Title, @"Week\s*(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (weekMatch.Success && int.TryParse(weekMatch.Groups[1].Value, out var titleWeek))
        {
            return titleWeek;
        }

        // Try to extract from Round field
        if (!string.IsNullOrEmpty(evt.Round))
        {
            var roundMatch = System.Text.RegularExpressions.Regex.Match(evt.Round, @"(\d+)");
            if (roundMatch.Success && int.TryParse(roundMatch.Groups[1].Value, out var roundNum))
            {
                return roundNum;
            }
        }

        // Calculate based on league season start dates
        DateTime seasonStart;

        if (leagueName.Contains("nfl") || leagueName.Contains("national football league"))
        {
            // NFL: Season starts first Thursday after Labor Day (first Monday of September)
            seasonStart = GetNflSeasonStart(eventDate.Year);
        }
        else if (leagueName.Contains("nba") || leagueName.Contains("national basketball association"))
        {
            // NBA: Season typically starts mid-October
            seasonStart = new DateTime(eventDate.Year, 10, 15);
            if (eventDate < seasonStart) seasonStart = new DateTime(eventDate.Year - 1, 10, 15);
        }
        else if (leagueName.Contains("nhl") || leagueName.Contains("national hockey league"))
        {
            // NHL: Season typically starts early October
            seasonStart = new DateTime(eventDate.Year, 10, 1);
            if (eventDate < seasonStart) seasonStart = new DateTime(eventDate.Year - 1, 10, 1);
        }
        else
        {
            // Default: assume calendar year weeks
            return (int)Math.Ceiling((eventDate.DayOfYear) / 7.0);
        }

        var daysSinceStart = (eventDate - seasonStart).Days;
        if (daysSinceStart < 0) return null;

        return (daysSinceStart / 7) + 1;
    }

    /// <summary>
    /// Get NFL season start date (first Thursday after Labor Day)
    /// </summary>
    private DateTime GetNflSeasonStart(int year)
    {
        // Labor Day is first Monday of September
        var laborDay = new DateTime(year, 9, 1);
        while (laborDay.DayOfWeek != DayOfWeek.Monday)
            laborDay = laborDay.AddDays(1);

        // First Thursday after Labor Day
        var firstThursday = laborDay.AddDays(3);
        return firstThursday;
    }

    /// <summary>
    /// Check if this is a motorsport event.
    /// Checks sport, league name, and event title for motorsport indicators.
    /// </summary>
    private bool IsMotorsport(string sport, string? leagueName, string? eventTitle = null)
    {
        var motorsportKeywords = new[] { "motorsport", "racing", "formula", "nascar", "indycar", "motogp", "f1", "grand prix", "gp" };
        var sportLower = sport.ToLowerInvariant();
        var leagueLower = leagueName?.ToLowerInvariant() ?? "";
        var titleLower = eventTitle?.ToLowerInvariant() ?? "";

        // Check sport and league first
        if (motorsportKeywords.Any(k => sportLower.Contains(k) || leagueLower.Contains(k)))
            return true;

        // Also check event title as fallback - catches "Qatar Grand Prix" even if sport/league is generic
        if (!string.IsNullOrEmpty(titleLower))
        {
            // Grand Prix is a strong indicator of motorsport
            if (titleLower.Contains("grand prix") || titleLower.Contains("gp sprint") ||
                titleLower.Contains("gp qualifying") || titleLower.Contains("gp race"))
                return true;
        }

        return false;
    }

    private string GetTeamSportLeaguePrefix(string? leagueName)
    {
        if (string.IsNullOrEmpty(leagueName)) return "";

        var lower = leagueName.ToLowerInvariant();

        if (lower.Contains("national basketball association") || lower.Contains("nba"))
            return "NBA";
        if (lower.Contains("national football league") || lower.Contains("nfl"))
            return "NFL";
        if (lower.Contains("national hockey league") || lower.Contains("nhl"))
            return "NHL";
        if (lower.Contains("major league baseball") || lower.Contains("mlb"))
            return "MLB";
        if (lower.Contains("major league soccer") || lower.Contains("mls"))
            return "MLS";
        // TheSportsDB names the league "Australian AFL" to disambiguate from
        // US leagues, but no release has ever been tagged that way - scene
        // and KAYO releases are uniformly "AFL 2026 Round 7 ..." so the
        // prefix must be the bare abbreviation.
        if (lower.Contains("afl") || lower.Contains("australian football"))
            return "AFL";
        // Same story as AFL: the metadata name is "Australian National Rugby
        // League" but every KAYO/scene release is "NRL 2026 Round 18 ...",
        // so searching with the full name returned zero results everywhere.
        if (lower.Contains("national rugby league") || lower.Contains("nrl"))
            return "NRL";

        return "";
    }

    private string GetMotorsportSeriesPrefix(string? leagueName)
    {
        if (string.IsNullOrEmpty(leagueName)) return "";

        var lower = leagueName.ToLowerInvariant();

        // IMPORTANT: Check Formula E BEFORE Formula 1 because:
        // 1. "formula e" must be checked before generic "f1" substring match
        // 2. Prevents false positives if league name contains both terms
        if (lower.Contains("formula e") || lower.Contains("formulae"))
            return "FormulaE";

        // Formula 1 check - now safe since Formula E was already checked
        if (lower.Contains("formula 1") || lower.Contains("formula one") || lower.Contains("f1"))
            return "Formula1";

        if (lower.Contains("motogp"))
            return "MotoGP";
        if (lower.Contains("nascar"))
            return "NASCAR";
        if (lower.Contains("indycar"))
            return "IndyCar";
        if (lower.Contains("wrc") || lower.Contains("world rally"))
            return "WRC";

        // British Superbike, checked before World Superbike so the shared
        // "superbike" word cannot pull it into the wrong series. Releases
        // use the BSB abbreviation, never the sponsored league name that
        // the metadata source carries ("Bennetts British Superbike").
        if (lower.Trim() == "bsb" || lower.Contains("british superbike"))
            return "BSB";

        // World Superbike: TheSportsDB names the league literally "SBK",
        // while releases are almost always tagged WSBK (WorldSBK branding).
        if (lower.Trim() == "sbk" || lower.Contains("world superbike") ||
            lower.Contains("superbike world") || lower.Contains("worldsbk") || lower.Contains("wsbk"))
            return "WSBK";

        return leagueName.Replace(" ", "");
    }

    /// <summary>
    /// The series-name forms to actually search for, given the canonical series key.
    /// Formula 1 / Formula E releases appear on trackers both spaced/dotted
    /// ("Formula.1.2026x11.Austria.Race", which tokenizes to "Formula 1") and
    /// concatenated ("formula1 2026 ..."). Searching only "Formula1" misses every
    /// dotted release - including the actual Race - so both forms are returned, spaced
    /// first because the dotted convention is the more common one. Series that are a
    /// single token in release names (MotoGP, NASCAR, IndyCar, WRC) need only one form.
    /// </summary>
    private static List<string> GetMotorsportSearchPrefixes(string seriesKey)
    {
        return seriesKey switch
        {
            "Formula1" => new List<string> { "Formula 1", "Formula1" },
            "FormulaE" => new List<string> { "Formula E", "FormulaE" },
            // Releases overwhelmingly use WSBK; SBK appears from some groups
            // and matches the league's own TheSportsDB name.
            "WSBK" => new List<string> { "WSBK", "SBK" },
            _ => new List<string> { seriesKey }
        };
    }

    /// <summary>
    /// Normalize league name for search queries.
    /// Handles common abbreviations and variations.
    /// </summary>
    private string NormalizeLeagueName(string leagueName)
    {
        // Strip trailing year from league name (e.g., "English Premier League 1997" -> "English Premier League")
        // This handles seasonal league names in the database
        var yearPattern = new Regex(@"\s+(19|20)\d{2}(-\d{2,4})?$", RegexOptions.IgnoreCase);
        var cleanedName = yearPattern.Replace(leagueName, "").Trim();

        // Common league name mappings for searches
        var mappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Ultimate Fighting Championship", "UFC" },
            { "National Basketball Association", "NBA" },
            { "National Football League", "NFL" },
            { "National Hockey League", "NHL" },
            { "Major League Baseball", "MLB" },
            { "English Premier League", "EPL" },
            { "Premier League", "EPL" },
            { "UEFA Champions League", "UCL" },
            { "Formula 1", "F1" },
            { "Formula One", "F1" },
            { "La Liga", "La Liga" },
            { "Bundesliga", "Bundesliga" },
            { "Serie A", "Serie A" },
            { "Ligue 1", "Ligue 1" },
        };

        if (mappings.TryGetValue(cleanedName, out var abbreviated))
        {
            return abbreviated;
        }

        return cleanedName;
    }

    /// <summary>
    /// Strip the trailing "fighter1 vs fighter2" portion from a fighting event
    /// title so the result matches what indexers actually publish. ONE/UFC/Bellator
    /// releases name the card, not the fighters: "ONE Friday Fights 150" not
    /// "ONE Friday Fights 150 Kompetch vs Attachai".
    ///
    /// Only strips when at least two words precede the matchup so titles like
    /// "Real Madrid vs Barcelona" - where the matchup IS the identity - are kept
    /// intact.
    /// </summary>
    public string StripFightersFromTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return title ?? string.Empty;

        // Trailing "name vs name" where each side is 1-3 words. \bvs\.?\b tolerates
        // both "vs" and "vs." as separators.
        var match = Regex.Match(title,
            @"^(.{2,}?)\s+\S+(?:\s+\S+){0,2}\s+vs\.?\s+\S+(?:\s+\S+){0,2}\s*$",
            RegexOptions.IgnoreCase);

        if (!match.Success) return title.Trim();

        var prefix = match.Groups[1].Value.Trim();
        // Require at least 2 prefix words so soccer-style "Lakers vs Celtics" isn't
        // collapsed to "Lakers".
        var prefixWordCount = prefix.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
        return prefixWordCount >= 2 ? prefix : title.Trim();
    }

    // Trailing stage designator of a stage race, for example
    // "Tour de France Stage 16". "Etappe" and "Leg" cover the same idea in
    // other feeds. "Round" is excluded on purpose: golf and motorsport
    // titles use it, and {Round} already serves them.
    private static readonly Regex StageSuffixPattern = new(
        @"\s+(?:Stage|Etappe|Leg)\s*(\d{1,3})\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Read the stage number from a stage-race title. Returns null when the
    /// title names no stage.
    /// </summary>
    public static int? ExtractStageNumber(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;

        var match = StageSuffixPattern.Match(title);
        return match.Success && int.TryParse(match.Groups[1].Value, out var stage) ? stage : null;
    }

    /// <summary>
    /// Remove the trailing stage designator from a stage-race title, so
    /// "Tour de France Stage 16" becomes "Tour de France". The caller can
    /// then name the stage in its own language.
    /// </summary>
    public static string StripStageFromTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return title ?? string.Empty;

        return StageSuffixPattern.Replace(title, string.Empty).Trim();
    }

    private string NormalizeEventTitle(string title)
    {
        var seasonEpisodeMatch = Regex.Match(title,
            @"(.+?)\s+[Ss]eason\s+(\d+)\s+(?:Week|Episode|Ep\.?)\s*(\d+)",
            RegexOptions.IgnoreCase);

        if (seasonEpisodeMatch.Success)
        {
            var showName = seasonEpisodeMatch.Groups[1].Value.Trim();
            var season = int.Parse(seasonEpisodeMatch.Groups[2].Value);
            var episode = int.Parse(seasonEpisodeMatch.Groups[3].Value);
            var shortName = GetShowShortName(showName);
            var normalizedQuery = $"{shortName} S{season:D2}E{episode:D2}";
            _logger.LogDebug("[EventQuery] Converted TV-style title '{Original}' to '{Normalized}'",
                title, normalizedQuery);
            return normalizedQuery;
        }

        var weekOnlyMatch = Regex.Match(title,
            @"(.+?)\s+Week\s*(\d+)$",
            RegexOptions.IgnoreCase);

        if (weekOnlyMatch.Success)
        {
            var showName = weekOnlyMatch.Groups[1].Value.Trim();
            var week = int.Parse(weekOnlyMatch.Groups[2].Value);
            var shortName = GetShowShortName(showName);
            return $"{shortName} Week {week}";
        }

        var prefixes = new[] { "UFC ", "Bellator ", "PFL ", "ONE ", "WWE ", "AEW " };
        foreach (var prefix in prefixes)
        {
            if (title.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return title;
            }
        }

        return title.Trim();
    }

    private string GetShowShortName(string showName)
    {
        var sceneNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Dana White's Contender Series", "Dana Whites Contender Series" },
            { "Dana Whites Contender Series", "Dana Whites Contender Series" },
            { "The Ultimate Fighter", "The Ultimate Fighter" },
            { "Road to UFC", "Road to UFC" },
            { "UFC Ultimate Insider", "UFC Ultimate Insider" },
        };

        foreach (var (full, sceneName) in sceneNames)
        {
            if (showName.Contains(full, StringComparison.OrdinalIgnoreCase))
            {
                return sceneName;
            }
        }

        return showName.Replace("'", "");
    }

    /// <summary>
    /// Detect content type from release name (universal - works for all sports)
    /// Examples: "Highlights" vs "Full Game" for team sports, "Full Event" for combat sports
    /// </summary>
    public string DetectContentType(Event evt, string releaseName)
    {
        var lower = releaseName.ToLower();

        // Universal content detection
        if (lower.Contains("highlight") || lower.Contains("extended highlight"))
        {
            return "Highlights";
        }

        if (lower.Contains("condensed") || lower.Contains("recap"))
        {
            return "Condensed";
        }

        if (lower.Contains("full") || lower.Contains("complete"))
        {
            return "Full Event";
        }

        // Default: assume full event
        return "Full Event";
    }
}
