using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Helpers;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// The query plan is the structured form of what the search builders already
/// produce: every query carries its league-name form, its provenance, and
/// whether it survived selection. This stage adds the structure only - a
/// league with no saved alias order must still produce exactly the same
/// query strings, in exactly the same order, as before the plan existed.
/// </summary>
public class QueryPlanTests
{
    private readonly EventQueryService _service = new(NullLogger<EventQueryService>.Instance);

    private static Event F1Event() => new()
    {
        Title = "Dutch Grand Prix Race",
        Sport = "Motorsport",
        Season = "2026",
        Round = "15",
        EventDate = new DateTime(2026, 8, 30),
        League = new League { Name = "Formula 1", Sport = "Motorsport" },
    };

    private static Event NflEvent() => new()
    {
        Title = "Chiefs vs Ravens",
        Sport = "American Football",
        EventDate = new DateTime(2026, 9, 12),
        League = new League { Name = "NFL", Sport = "American Football" },
        HomeTeam = new Team { Name = "Chiefs", UserAliases = "Вожди" },
        AwayTeam = new Team { Name = "Ravens", UserAliases = "Вороны" },
    };

    private static League AliasLeague() => new()
    {
        Name = "Formula 1",
        Sport = "Motorsport",
        AlternateName = "F1 World Championship",
        UserAliases = "Формула 1, Formule 1",
    };

    private static QueryCandidate Candidate(string text, bool mandatory, int rank = 0,
        string form = "Formula 1", LeagueNameFormSource source = LeagueNameFormSource.Canonical) => new(
        Text: text,
        LeagueNameForm: form,
        FormSource: source,
        Kind: QueryKind.Motorsport,
        SpecificityRank: rank,
        AliasOrderIndex: 0,
        TemplateIndex: null,
        TeamAliasSlot: null,
        IsMandatory: mandatory,
        IsSelected: false,
        DropReason: null,
        ContributingForms: [new LeagueNameForm(form, source, 0, [source])]);

    // ---- Legacy parity -------------------------------------------------

    [Fact]
    public void NullSavedOrder_MotorsportPlan_ReturnsTheLegacyQueriesInLegacyOrder()
    {
        var plan = _service.BuildEventQueryPlan(F1Event());

        plan.SelectedQueries.Select(query => query.Text).Should().Equal(
            "Formula 1 2026 Round15",
            "Formula 1 2026 Dutch",
            "Formula 1 2026 Netherlands",
            "Formula 1 2026",
            "Formula1 2026 Round15",
            "Formula1 2026 Dutch",
            "Formula1 2026 Netherlands",
            "Formula1 2026");
        plan.SelectedQueries.Should().OnlyContain(query => query.IsMandatory && query.IsSelected);
        plan.DroppedQueries.Should().BeEmpty();
        plan.IsTruncated.Should().BeFalse();
        plan.MandatoryInvariantViolated.Should().BeFalse();
    }

    [Fact]
    public void NullSavedOrder_TeamSportPlan_KeepsTeamAliasQueriesAfterTheCanonicalBaseline()
    {
        var plan = _service.BuildEventQueryPlan(NflEvent());

        plan.SelectedQueries.Select(query => query.Text).Should().Equal(
            "NFL 2026 09",
            "NFL 2026",
            "NFL 2026 Вожди Вороны");
    }

    [Fact]
    public void BuildEventQueries_ReturnsTheSelectedPlanText()
    {
        var evt = F1Event();

        _service.BuildEventQueries(evt).Should()
            .Equal(_service.BuildEventQueryPlan(evt).SelectedQueries.Select(query => query.Text));
    }

    [Fact]
    public void TemplateOptions_OverrideTheLeagueTemplateWithoutMutatingTheLeague()
    {
        var evt = F1Event();
        var options = new QueryPlanningOptions(null, null, "preview {Year} R{Round:0}");

        var plan = _service.BuildEventQueryPlan(evt, null, "saved {Year}", options);

        plan.SelectedQueries.Select(query => query.Text).Should().Equal("preview 2026 R15");
        evt.League!.SearchQueryTemplate.Should().BeNull();
    }

    [Fact]
    public void PureMatchupBoxingEvent_CollapsesTheDuplicateSurnameAndTitleQueries()
    {
        // The fighting builder emits the surname matchup, its reverse, and
        // the normalized title. For a title that IS the matchup, the first
        // and third are the same string - previously emitted twice, because
        // only the template path deduplicated. The plan deduplicates every
        // path, so this is the one place "byte-for-byte legacy output" means
        // "modulo exact duplicates".
        var evt = new Event
        {
            Title = "Usyk vs Fury",
            Sport = "Boxing",
            EventDate = new DateTime(2026, 5, 9),
            League = new League { Name = "Boxing", Sport = "Boxing" },
        };

        var plan = _service.BuildEventQueryPlan(evt);

        plan.SelectedQueries.Select(query => query.Text).Should().Equal("Usyk vs Fury", "Fury vs Usyk");
        plan.SelectedQueries.Select(query => query.Text).Should().OnlyHaveUniqueItems();
    }

    // ---- Deduplication and provenance ----------------------------------

    [Fact]
    public void DuplicateText_KeepsMandatoryProvenanceAndRecordsBothContributingForms()
    {
        var mandatory = Candidate("Formula 1 2026", mandatory: true);
        var expansion = Candidate("formula 1 2026", mandatory: false,
            form: "Formule 1", source: LeagueNameFormSource.UserAlias);

        var plan = EventQueryService.BuildPlan(
            [mandatory, expansion], "Formula 1", [], NullLogger<EventQueryService>.Instance);

        plan.Candidates.Should().ContainSingle();
        var merged = plan.Candidates[0];
        merged.Text.Should().Be("Formula 1 2026");
        merged.IsMandatory.Should().BeTrue();
        merged.FormSource.Should().Be(LeagueNameFormSource.Canonical);
        merged.ContributingForms.Select(form => form.Value).Should().Equal("Formula 1", "Formule 1");
    }

    // ---- Budget and ceiling --------------------------------------------

    [Fact]
    public void MoreThanEightExpansions_SelectsTheFirstEightAndDropsTheRestWithAReason()
    {
        var candidates = new List<QueryCandidate> { Candidate("F1 2026", mandatory: true) };
        for (var i = 0; i < 12; i++)
        {
            candidates.Add(Candidate($"alias{i} 2026", mandatory: false, rank: i));
        }
        var logger = new CapturingLogger();

        var plan = EventQueryService.BuildPlan(candidates, "Formula 1", [], logger);

        plan.SelectedQueries.Should().HaveCount(9);
        plan.SelectedQueries[0].Text.Should().Be("F1 2026");
        plan.SelectedQueries.Skip(1).Select(query => query.Text).Should()
            .Equal(Enumerable.Range(0, 8).Select(i => $"alias{i} 2026"));
        plan.AliasBudgetUsed.Should().Be(8);
        plan.AliasBudgetLimit.Should().Be(8);
        plan.IsTruncated.Should().BeTrue();
        plan.MandatoryInvariantViolated.Should().BeFalse();

        plan.DroppedQueries.Should().HaveCount(4);
        plan.DroppedQueries.Should().OnlyContain(query => query.DropReason == QueryDropReason.AliasBudgetExceeded);
        plan.DroppedQueries.Should().OnlyContain(query => !query.IsSelected);

        var warning = logger.Entries.Should().ContainSingle(entry => entry.Level == LogLevel.Warning).Subject;
        warning.Message.Should().Contain("Formula 1").And.Contain("9").And.Contain("4");
    }

    [Fact]
    public void MandatoryQueries_AreNeverDroppedByOrdinaryTruncation()
    {
        var candidates = Enumerable.Range(0, 12).Select(i => Candidate($"mandatory{i}", mandatory: true))
            .Concat(Enumerable.Range(0, 3).Select(i => Candidate($"alias{i}", mandatory: false)))
            .ToList();

        var plan = EventQueryService.BuildPlan(candidates, "Formula 1", [], NullLogger<EventQueryService>.Instance);

        plan.SelectedQueries.Should().HaveCount(15);
        plan.DroppedQueries.Should().BeEmpty();
        plan.IsTruncated.Should().BeFalse();
    }

    [Fact]
    public void BuilderRegressionAboveTheHardCeiling_KeepsTheFirstFiftyAndFlagsTheInvariant()
    {
        var candidates = Enumerable.Range(0, 60)
            .Select(i => Candidate($"runaway{i:D2}", mandatory: true))
            .ToList();
        var logger = new CapturingLogger();

        var plan = EventQueryService.BuildPlan(candidates, "Formula 1", [], logger);

        plan.HardQueryCeiling.Should().Be(50);
        plan.SelectedQueries.Should().HaveCount(50);
        plan.SelectedQueries.Select(query => query.Text).Should()
            .Equal(Enumerable.Range(0, 50).Select(i => $"runaway{i:D2}"));
        plan.MandatoryInvariantViolated.Should().BeTrue();
        plan.IsTruncated.Should().BeTrue();
        plan.DroppedQueries.Should().HaveCount(10);
        plan.DroppedQueries.Should().OnlyContain(query => query.DropReason == QueryDropReason.HardQueryCeilingExceeded);
        logger.Entries.Should().Contain(entry => entry.Level == LogLevel.Error);
    }

    [Fact]
    public void ExpansionsEmittedBetweenBaselineQueries_AreStillAppendedAfterTheWholeBaseline()
    {
        // Builders naturally emit an alias variant right after the baseline
        // query it came from. That must not push the two legacy baseline
        // strings apart.
        var candidates = new List<QueryCandidate>
        {
            Candidate("Formula 1 2026 Round15", mandatory: true),
            Candidate("Formule 1 2026 Round15", mandatory: false,
                form: "Formule 1", source: LeagueNameFormSource.UserAlias),
            Candidate("Formula 1 2026", mandatory: true),
            Candidate("Formule 1 2026", mandatory: false,
                form: "Formule 1", source: LeagueNameFormSource.UserAlias),
        };

        var plan = EventQueryService.BuildPlan(
            candidates, "Formula 1", [], NullLogger<EventQueryService>.Instance);

        plan.SelectedQueries.Select(query => query.Text).Should().Equal(
            "Formula 1 2026 Round15",
            "Formula 1 2026",
            "Formule 1 2026 Round15",
            "Formule 1 2026");
    }

    // ---- League name forms ---------------------------------------------

    [Fact]
    public void NoSavedOrder_PutsCanonicalAndBuiltInFirstThenUserAliasesThenUpstreamAliases()
    {
        var forms = LeagueQueryForms.Build(AliasLeague(), ["Formula1"]);

        forms.Forms.Select(form => form.Value).Should()
            .Equal("Formula1", "Formula 1", "Формула 1", "Formule 1", "F1 World Championship");
        forms.Forms.Select(form => form.Source).Should().Equal(
            LeagueNameFormSource.BuiltIn,
            LeagueNameFormSource.Canonical,
            LeagueNameFormSource.UserAlias,
            LeagueNameFormSource.UserAlias,
            LeagueNameFormSource.UpstreamAlias);
        forms.ExcludedForms.Should().BeEmpty();
    }

    [Fact]
    public void SavedOrder_InterleavesEverySourceAndRenumbersOrderIndexes()
    {
        var league = AliasLeague();
        league.AliasSearchOrder =
        [
            new LeagueAliasOrderEntry { Source = LeagueNameFormSource.UserAlias, Value = "Formule 1" },
            new LeagueAliasOrderEntry { Source = LeagueNameFormSource.BuiltIn, Value = "Formula1" },
            new LeagueAliasOrderEntry { Source = LeagueNameFormSource.UpstreamAlias, Value = "F1 World Championship" },
            new LeagueAliasOrderEntry { Source = LeagueNameFormSource.Canonical, Value = "Formula 1" },
        ];

        var forms = LeagueQueryForms.Build(league, ["Formula1"]);

        forms.Forms.Select(form => form.Value).Should()
            .Equal("Formule 1", "Formula1", "F1 World Championship", "Formula 1", "Формула 1");
        forms.Forms.Select(form => form.OrderIndex).Should().Equal(0, 1, 2, 3, 4);
    }

    [Fact]
    public void SavedOrder_IgnoresStoredFormsTheLeagueNoLongerHas()
    {
        var league = AliasLeague();
        league.UserAliases = "Formule 1";
        league.AliasSearchOrder =
        [
            new LeagueAliasOrderEntry { Source = LeagueNameFormSource.UserAlias, Value = "Формула 1" },
            new LeagueAliasOrderEntry { Source = LeagueNameFormSource.UserAlias, Value = "Formule 1" },
        ];

        var forms = LeagueQueryForms.Build(league, ["Formula1"]);

        forms.Forms.Select(form => form.Value).Should().NotContain("Формула 1");
        forms.Forms.Select(form => form.Value).Should()
            .Equal("Formule 1", "Formula1", "Formula 1", "F1 World Championship");
    }

    [Fact]
    public void SavedOrder_AppendsFormsThatAreNotStoredYet()
    {
        var league = AliasLeague();
        league.AliasSearchOrder =
        [
            new LeagueAliasOrderEntry { Source = LeagueNameFormSource.UserAlias, Value = "Formule 1" },
        ];

        var forms = LeagueQueryForms.Build(league, ["Formula1"]);

        forms.Forms[0].Value.Should().Be("Formule 1");
        forms.Forms.Skip(1).Select(form => form.Value).Should()
            .Equal("Formula1", "Formula 1", "Формула 1", "F1 World Championship");
    }

    [Fact]
    public void ClearedSavedOrder_RestoresTheLegacyFormOrder()
    {
        var league = AliasLeague();
        league.AliasSearchOrder = null;

        var forms = LeagueQueryForms.Build(league, ["Formula1"]);

        forms.Forms.Select(form => form.Value).Should()
            .Equal("Formula1", "Formula 1", "Формула 1", "Formule 1", "F1 World Championship");
    }

    [Fact]
    public void OnlyTheFirstThreeAliasForms_Survive_AndTheRestAreReportedAsAliasFormLimit()
    {
        var league = AliasLeague();
        league.UserAliases = "Формула 1, Formule 1, Formel 1, Fórmula 1";

        var forms = LeagueQueryForms.Build(league, ["Formula1"]);

        // Built-in and canonical forms do not consume alias slots.
        forms.Forms.Select(form => form.Value).Should()
            .Equal("Formula1", "Formula 1", "Формула 1", "Formule 1", "Formel 1");
        forms.ExcludedForms.Select(excluded => excluded.Form.Value).Should()
            .Equal("Fórmula 1", "F1 World Championship");
        forms.ExcludedForms.Should()
            .OnlyContain(excluded => excluded.Reason == QueryDropReason.AliasFormLimit);
    }

    [Fact]
    public void DuplicateFormText_BecomesOneFormCarryingEveryContributingSource()
    {
        var league = AliasLeague();
        league.UserAliases = "formula 1";

        var forms = LeagueQueryForms.Build(league, ["Formula1"]);

        forms.Forms.Should().HaveCount(3);
        var canonical = forms.Forms.Single(form => form.Value == "Formula 1");
        canonical.Source.Should().Be(LeagueNameFormSource.Canonical);
        canonical.ContributingSources.Should()
            .Equal(LeagueNameFormSource.Canonical, LeagueNameFormSource.UserAlias);
    }

    [Fact]
    public void UnsavedOptionAliases_AreUsedInsteadOfThePersistedOnes()
    {
        var league = AliasLeague();
        var options = new QueryPlanningOptions("Formule 1", null, null);

        var forms = LeagueQueryForms.Build(league, ["Formula1"], options);

        forms.Forms.Select(form => form.Value).Should().NotContain("Формула 1");
        forms.Forms.Select(form => form.Value).Should()
            .Equal("Formula1", "Formula 1", "Formule 1", "F1 World Championship");
        league.UserAliases.Should().Be("Формула 1, Formule 1");
    }

    [Fact]
    public void ExcludedNameForms_AreReportedOnThePlan()
    {
        var evt = F1Event();
        evt.League!.UserAliases = "Формула 1, Formule 1, Formel 1, Fórmula 1";

        var plan = _service.BuildEventQueryPlan(evt);

        plan.ExcludedNameForms.Select(excluded => excluded.Form.Value).Should().Contain("Fórmula 1");
        plan.ExcludedNameForms.Should()
            .OnlyContain(excluded => excluded.Reason == QueryDropReason.AliasFormLimit);
    }

    [Fact]
    public void EverySearchedAliasForm_IsAFormLeagueAliasHelperWillAlsoMatch()
    {
        // LeagueQueryForms and LeagueAliasHelper are deliberately different
        // lists (query spellings vs league identity), but they may only
        // differ in one direction: a release found through a searched alias
        // form must still be able to pass league-identity matching. Built-in
        // forms are the documented exception - they are query spellings, not
        // identities.
        var league = AliasLeague();
        var matching = LeagueAliasHelper.GetMatchingAliases(league);

        var searched = LeagueQueryForms.Build(league, ["Formula1"]).Forms
            .Where(form => form.Source != LeagueNameFormSource.BuiltIn)
            .Select(form => form.Value)
            .ToList();

        searched.Should().Contain(["Formula 1", "Формула 1", "Formule 1", "F1 World Championship"]);
        searched.Should().BeSubsetOf(matching);
        // The abbreviation path is exercised: it is matched but never searched.
        matching.Should().Contain("F1");
    }

    private sealed record LogEntry(LogLevel Level, string Message);

    private sealed class CapturingLogger : ILogger<EventQueryService>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add(new LogEntry(logLevel, formatter(state, exception)));
    }
}
