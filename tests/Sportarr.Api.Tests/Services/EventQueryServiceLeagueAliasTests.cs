using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// League aliases must actually change what each builder asks the indexer
/// for. Re-invoking a builder with a different league name is not enough:
/// the alias has to be woven into the query shapes that builder emits.
///
/// Every test here pairs an alias-expansion assertion with the guarantee
/// that guards it - a league with no saved alias order still produces the
/// legacy query strings, in the legacy order, byte for byte.
/// </summary>
public class EventQueryServiceLeagueAliasTests
{
    private readonly EventQueryService _service = new(NullLogger<EventQueryService>.Instance);

    private static List<string> Texts(QueryPlan plan) =>
        plan.SelectedQueries.Select(query => query.Text).ToList();

    // ---- Motorsport ----------------------------------------------------

    private static Event F1Event(string? userAliases = null, string? alternateName = null) => new()
    {
        Title = "Dutch Grand Prix Race",
        Sport = "Motorsport",
        Season = "2026",
        Round = "15",
        EventDate = new DateTime(2026, 8, 30),
        League = new League
        {
            Name = "Formula 1",
            Sport = "Motorsport",
            UserAliases = userAliases,
            AlternateName = alternateName,
        },
    };

    [Fact]
    public void Motorsport_AliasFormsAreSearchedRaw_NotCollapsedOntoTheSeriesPrefix()
    {
        var plan = _service.BuildEventQueryPlan(F1Event("Формула 1"));

        // The built-in prefixes keep their legacy block, byte for byte...
        Texts(plan).Take(8).Should().Equal(
            "Formula 1 2026 Round15",
            "Formula 1 2026 Dutch",
            "Formula 1 2026 Netherlands",
            "Formula 1 2026",
            "Formula1 2026 Round15",
            "Formula1 2026 Dutch",
            "Formula1 2026 Netherlands",
            "Formula1 2026");

        // ...and the alias gets the same query shapes, spelled exactly as the
        // user typed it. Passing it through GetMotorsportSeriesPrefix would
        // have produced "Формула1" and collapsed the expansion back onto the
        // canonical series key.
        Texts(plan).Skip(8).Should().Equal(
            "Формула 1 2026 Round15",
            "Формула 1 2026 Dutch",
            "Формула 1 2026 Netherlands",
            "Формула 1 2026");
        plan.SelectedQueries.Skip(8).Should().OnlyContain(query => !query.IsMandatory);
        plan.SelectedQueries.Take(8).Should().OnlyContain(query => query.IsMandatory);
    }

    [Fact]
    public void Motorsport_BuiltInPrefixQueries_RecordTheBuiltInFormTheyWereBuiltFrom()
    {
        // Provenance is recorded at emission, never reconstructed from query
        // text - so the four queries built from the built-in "Formula1"
        // prefix must say so, not claim to be the canonical "Formula 1".
        var plan = _service.BuildEventQueryPlan(F1Event("Формула 1"));

        var byText = plan.SelectedQueries.ToDictionary(query => query.Text);

        byText["Formula 1 2026 Round15"].LeagueNameForm.Should().Be("Formula 1");
        byText["Formula 1 2026 Round15"].FormSource.Should().Be(LeagueNameFormSource.Canonical);

        byText["Formula1 2026 Round15"].LeagueNameForm.Should().Be("Formula1");
        byText["Formula1 2026 Round15"].FormSource.Should().Be(LeagueNameFormSource.BuiltIn);

        byText["Формула 1 2026"].LeagueNameForm.Should().Be("Формула 1");
        byText["Формула 1 2026"].FormSource.Should().Be(LeagueNameFormSource.UserAlias);
    }

    [Fact]
    public void Motorsport_UserAndUpstreamAliasesInterleaveInFormOrder()
    {
        var plan = _service.BuildEventQueryPlan(F1Event("Формула 1", "F1 World Championship"));

        // User alias block before upstream alias block.
        Texts(plan).Skip(8).Should().Equal(
            "Формула 1 2026 Round15",
            "Формула 1 2026 Dutch",
            "Формула 1 2026 Netherlands",
            "Формула 1 2026",
            "F1 World Championship 2026 Round15",
            "F1 World Championship 2026 Dutch",
            "F1 World Championship 2026 Netherlands",
            "F1 World Championship 2026");
    }

    [Fact]
    public void Motorsport_ThreeAliases_ExceedTheBudget_AndTheOverflowIsDroppedWithAReason()
    {
        var plan = _service.BuildEventQueryPlan(F1Event("Формула 1, Formule 1, Formel 1"));

        plan.AliasBudgetUsed.Should().Be(8);
        plan.SelectedQueries.Count(query => !query.IsMandatory).Should().Be(8);
        plan.SelectedQueries.Count(query => query.IsMandatory).Should().Be(8);
        plan.DroppedQueries.Should().HaveCount(4);
        plan.DroppedQueries.Should().OnlyContain(query => query.DropReason == QueryDropReason.AliasBudgetExceeded);
        plan.DroppedQueries.Select(query => query.Text).Should().Equal(
            "Formel 1 2026 Round15",
            "Formel 1 2026 Dutch",
            "Formel 1 2026 Netherlands",
            "Formel 1 2026");
        plan.IsTruncated.Should().BeTrue();
        plan.MandatoryInvariantViolated.Should().BeFalse();
    }

    [Fact]
    public void Motorsport_SavedOrder_GroupsBySpecificityFirstThenAliasPosition()
    {
        var evt = F1Event("Формула 1");
        evt.League!.AliasSearchOrder =
        [
            new LeagueAliasOrderEntry { Source = LeagueNameFormSource.UserAlias, Value = "Формула 1" },
            new LeagueAliasOrderEntry { Source = LeagueNameFormSource.Canonical, Value = "Formula 1" },
            new LeagueAliasOrderEntry { Source = LeagueNameFormSource.BuiltIn, Value = "Formula1" },
        ];

        var plan = _service.BuildEventQueryPlan(evt);

        // Mandatory baseline still first and still complete, but now ordered
        // round-before-location across every form rather than form by form.
        Texts(plan).Take(8).Should().Equal(
            "Formula 1 2026 Round15",
            "Formula1 2026 Round15",
            "Formula 1 2026 Dutch",
            "Formula1 2026 Dutch",
            "Formula 1 2026 Netherlands",
            "Formula1 2026 Netherlands",
            "Formula 1 2026",
            "Formula1 2026");
        plan.SelectedQueries.Take(8).Should().OnlyContain(query => query.IsMandatory);

        // The alias keeps its expansion classification: reordering never
        // promotes an alias query into the mandatory baseline.
        Texts(plan).Skip(8).Should().Equal(
            "Формула 1 2026 Round15",
            "Формула 1 2026 Dutch",
            "Формула 1 2026 Netherlands",
            "Формула 1 2026");
        plan.AliasBudgetUsed.Should().Be(4);
    }

    // ---- Team sports ---------------------------------------------------

    private static Event NflEvent(string? userAliases = null) => new()
    {
        Title = "Chiefs vs Ravens",
        Sport = "American Football",
        EventDate = new DateTime(2026, 9, 12),
        League = new League { Name = "NFL", Sport = "American Football", UserAliases = userAliases },
        HomeTeam = new Team { Name = "Chiefs", UserAliases = "Вожди" },
        AwayTeam = new Team { Name = "Ravens", UserAliases = "Вороны" },
        HomeTeamName = "Chiefs",
        AwayTeamName = "Ravens",
    };

    [Fact]
    public void MappedTeamSport_NullOrder_KeepsTheLegacyQueriesByteForByte()
    {
        Texts(_service.BuildEventQueryPlan(NflEvent())).Should().Equal(
            "NFL 2026 09",
            "NFL 2026",
            "NFL 2026 Вожди Вороны");
    }

    [Fact]
    public void MappedTeamSport_AliasReusesEveryExistingTeamSlot_WithNoCartesianProduct()
    {
        var plan = _service.BuildEventQueryPlan(NflEvent("Национальная футбольная лига"));

        Texts(plan).Should().Equal(
            "NFL 2026 09",
            "NFL 2026",
            "NFL 2026 Вожди Вороны",
            "Национальная футбольная лига 2026 Chiefs Ravens",
            "Национальная футбольная лига 2026 Вожди Вороны");

        // No "alias 2026 Chiefs Вороны" / "alias 2026 Вожди Ravens" pairings.
        Texts(plan).Should().NotContain(text => text.Contains("Chiefs Вороны") || text.Contains("Вожди Ravens"));
    }

    private static Event PremiershipRugbyEvent(string? userAliases = null) => new()
    {
        Title = "Bath vs Saracens",
        Sport = "Rugby",
        EventDate = new DateTime(2026, 3, 14),
        League = new League { Name = "Premiership Rugby", Sport = "Rugby", UserAliases = userAliases },
        HomeTeamName = "Bath",
        AwayTeamName = "Saracens",
    };

    [Fact]
    public void UnmappedTeamSport_NullOrder_KeepsTheLegacyQueriesByteForByte()
    {
        Texts(_service.BuildEventQueryPlan(PremiershipRugbyEvent())).Should().Equal(
            "Bath vs Saracens",
            "Saracens vs Bath");
    }

    [Fact]
    public void UnmappedTeamSport_GetsALeagueAliasQueryEvenThoughItsBuilderFallsBackToEventTitles()
    {
        // Premiership Rugby has no league prefix, so its canonical queries
        // are event titles with no league token at all. Without an explicit
        // alias shape it would gain nothing from having aliases.
        var plan = _service.BuildEventQueryPlan(PremiershipRugbyEvent("Gallagher Premiership"));

        Texts(plan).Should().Equal(
            "Bath vs Saracens",
            "Saracens vs Bath",
            "Gallagher Premiership 2026 Bath Saracens");
    }

    // ---- Wrestling -----------------------------------------------------

    private static Event RawEvent(string? userAliases = null) => new()
    {
        Title = "WWE Monday Night Raw",
        Sport = "Wrestling",
        EventDate = new DateTime(2026, 3, 2),
        League = new League { Name = "WWE", Sport = "Wrestling", UserAliases = userAliases },
    };

    [Fact]
    public void Wrestling_NullOrder_KeepsTheLegacyQueriesByteForByte()
    {
        Texts(_service.BuildEventQueryPlan(RawEvent())).Should().Equal(
            "WWE Raw 2026 03 02",
            "WWE Raw 2026 03");
    }

    [Fact]
    public void Wrestling_AliasReplacesOnlyTheLeadingOrganizationToken()
    {
        var plan = _service.BuildEventQueryPlan(RawEvent("World Wrestling Entertainment"));

        Texts(plan).Should().Equal(
            "WWE Raw 2026 03 02",
            "WWE Raw 2026 03",
            "World Wrestling Entertainment Raw 2026 03 02",
            "World Wrestling Entertainment Raw 2026 03");
    }

    [Fact]
    public void Wrestling_AliasNeverInfersTheOtherPromotion()
    {
        // An arbitrary alias must not be read as evidence that a WWE show is
        // really an AEW show, or vice versa.
        var plan = _service.BuildEventQueryPlan(RawEvent("AEW Dynamite"));

        Texts(plan).Should().Equal(
            "WWE Raw 2026 03 02",
            "WWE Raw 2026 03",
            "AEW Dynamite Raw 2026 03 02",
            "AEW Dynamite Raw 2026 03");
        Texts(plan).Should().NotContain("AEW Dynamite 2026 03 02");
    }

    [Fact]
    public void WrestlingSpecialEvent_AliasKeepsTheEventNameAndYearSuffixes()
    {
        var evt = new Event
        {
            Title = "WWE WrestleMania 42",
            Sport = "Wrestling",
            EventDate = new DateTime(2026, 4, 5),
            League = new League { Name = "WWE", Sport = "Wrestling", UserAliases = "World Wrestling Entertainment" },
        };

        Texts(_service.BuildEventQueryPlan(evt)).Should().Equal(
            "WWE WrestleMania 42 2026",
            "WWE WrestleMania 42",
            "World Wrestling Entertainment WrestleMania 42 2026",
            "World Wrestling Entertainment WrestleMania 42");
    }

    // ---- Fighting ------------------------------------------------------

    private static Event UfcEvent(string? userAliases = null) => new()
    {
        Title = "UFC 299",
        Sport = "Fighting",
        EventDate = new DateTime(2026, 3, 9),
        League = new League { Name = "UFC", Sport = "Fighting", UserAliases = userAliases },
    };

    [Fact]
    public void Fighting_NullOrder_KeepsTheLegacyQueriesByteForByte()
    {
        Texts(_service.BuildEventQueryPlan(UfcEvent())).Should().Equal("UFC 299", "UFC 2026");
    }

    [Fact]
    public void Fighting_AliasPreservesTheCardNumberAndTheYearFallback()
    {
        var plan = _service.BuildEventQueryPlan(UfcEvent("Ultimate Fighting Championship"));

        Texts(plan).Should().Equal(
            "UFC 299",
            "UFC 2026",
            "Ultimate Fighting Championship 299",
            "Ultimate Fighting Championship 2026");
    }

    [Fact]
    public void Fighting_PureSurnameMatchupQueries_GainNoLeagueVariant()
    {
        var evt = new Event
        {
            Title = "Usyk vs Fury",
            Sport = "Boxing",
            EventDate = new DateTime(2026, 5, 9),
            League = new League { Name = "Boxing", Sport = "Boxing", UserAliases = "Бокс" },
        };

        var plan = _service.BuildEventQueryPlan(evt);

        Texts(plan).Should().Equal("Usyk vs Fury", "Fury vs Usyk");
        plan.SelectedQueries.Should().OnlyContain(query => query.IsMandatory);
        plan.AliasBudgetUsed.Should().Be(0);
    }

    // ---- Generic fallback ----------------------------------------------

    private static Event CyclingEvent(string? userAliases = null) => new()
    {
        Title = "Paris Roubaix",
        Sport = "Cycling",
        EventDate = new DateTime(2026, 4, 12),
        League = new League { Name = "UCI World Tour", Sport = "Cycling", UserAliases = userAliases },
    };

    [Fact]
    public void GenericFallback_NullOrder_KeepsTheLegacyQueryByteForByte()
    {
        Texts(_service.BuildEventQueryPlan(CyclingEvent())).Should().Equal("Paris Roubaix");
    }

    [Fact]
    public void GenericFallback_AliasQueriesPrefixTheNormalizedTitleWithTheAliasAndYear()
    {
        var plan = _service.BuildEventQueryPlan(CyclingEvent("Мировой тур UCI"));

        Texts(plan).Should().Equal(
            "Paris Roubaix",
            "Мировой тур UCI 2026 Paris Roubaix");
    }

    // ---- Custom templates ----------------------------------------------

    [Fact]
    public void CustomTemplate_AliasReplacesTheLeagueTokenWithTheRawAliasText()
    {
        var evt = F1Event("Формула 1");
        evt.League!.SearchQueryTemplate = "{League} {Year} Round {Round:0}";

        var plan = _service.BuildEventQueryPlan(evt, null, evt.League.SearchQueryTemplate);

        Texts(plan).Should().Equal(
            "Formula1 2026 Round 15",
            "Формула 1 2026 Round 15");
    }

    [Fact]
    public void CustomTemplates_TenTemplatesTimesThreeTeamSlots_KeepAllFortyBaselineQueriesFree()
    {
        var evt = new Event
        {
            Title = "Chiefs vs Ravens",
            Sport = "American Football",
            EventDate = new DateTime(2026, 9, 12),
            League = new League { Name = "NFL", Sport = "American Football" },
            HomeTeam = new Team { Name = "Chiefs", UserAliases = "Вожди, Kansas City, KC" },
            AwayTeam = new Team { Name = "Ravens", UserAliases = "Вороны, Baltimore, BAL" },
        };
        var templates = string.Join("\n", Enumerable.Range(0, 10)
            .Select(i => $"t{i} {{League}} {{HomeTeam}} {{AwayTeam}}"));

        var plan = _service.BuildEventQueryPlan(evt, null, templates);

        plan.SelectedQueries.Should().HaveCount(40);
        plan.SelectedQueries.Should().OnlyContain(query => query.IsMandatory);
        plan.AliasBudgetUsed.Should().Be(0);
        plan.DroppedQueries.Should().BeEmpty();
        plan.MandatoryInvariantViolated.Should().BeFalse();
        Texts(plan).Take(4).Should().Equal(
            "t0 NFL Chiefs Ravens",
            "t0 NFL Вожди Вороны",
            "t0 NFL Kansas City Baltimore",
            "t0 NFL KC BAL");
    }

    [Fact]
    public void CustomTemplates_SavedOrder_NeverMovesALaterTemplateAheadOfAnEarlierOne()
    {
        var evt = NflEvent("Лига");
        evt.League!.AliasSearchOrder =
        [
            new LeagueAliasOrderEntry { Source = LeagueNameFormSource.UserAlias, Value = "Лига" },
            new LeagueAliasOrderEntry { Source = LeagueNameFormSource.BuiltIn, Value = "NFL" },
        ];

        var plan = _service.BuildEventQueryPlan(evt, null, "a {League} {Year}\nb {League} {Year}");

        Texts(plan).Should().Equal(
            "a NFL 2026",
            "b NFL 2026",
            "a Лига 2026",
            "b Лига 2026");
    }
}
