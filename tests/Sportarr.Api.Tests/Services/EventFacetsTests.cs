using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Xunit;

namespace Sportarr.Api.Tests.Services;

public class EventFacetsTests
{
    private static FacetSchema Schema => EventFacets.GetSchema("Skiing", "FIS Alpine Ski World Cup")!;

    private static readonly string[] FisAlpineTitles = new[]
    {
        "Mens Downhill 2 at Beaver Creek",
        "Mens Downhill 2 at Val Gardena",
        "Mens Downhill at Beaver Creek",
        "Mens Downhill at Courchevel",
        "Mens Downhill at Crans-Montana",
        "Mens Downhill at Garmisch-Partenkirchen",
        "Mens Downhill at Kitzbühel",
        "Mens Downhill at Lillehammer",
        "Mens Downhill at Val Gardena",
        "Mens Downhill at Wengen",
        "Mens Giant Slalom at Adelboden",
        "Mens Giant Slalom at Alta Badia",
        "Mens Giant Slalom at Beaver Creek",
        "Mens Giant Slalom at Copper Mt",
        "Mens Giant Slalom at Kranjska Gora",
        "Mens Giant Slalom at Lillehammer",
        "Mens Giant Slalom at Schladming",
        "Mens Giant Slalom at Sölden",
        "Mens Giant Slalom at Val d'Isère",
        "Mens Slalom at Adelboden",
        "Mens Slalom at Alta Badia",
        "Mens Slalom at Gurgl",
        "Mens Slalom at Kitzbühel",
        "Mens Slalom at Kranjska Gora",
        "Mens Slalom at Levi",
        "Mens Slalom at Lillehammer",
        "Mens Slalom at Madonna di Campiglio",
        "Mens Slalom at Schladming",
        "Mens Slalom at Val d'Isère",
        "Mens Slalom at Wengen",
        "Mens Super G 2 at Courchevel",
        "Mens Super G at Beaver Creek",
        "Mens Super G at Copper Mt",
        "Mens Super G at Courchevel",
        "Mens Super G at Kitzbühel",
        "Mens Super G at Lillehammer",
        "Mens Super G at Livigno",
        "Mens Super G at Val Gardena",
        "Mens Super G at Wengen",
        "Womens Downhill 2 at St. Moritz",
        "Womens Downhill 2 at Val di Fassa",
        "Womens Downhill at Lillehammer",
        "Womens Downhill at Soldeu",
        "Womens Downhill at St. Moritz",
        "Womens Downhill at Tarvisio",
        "Womens Downhill at Val d'Isère",
        "Womens Downhill at Val di Fassa",
        "Womens Downhill at Zauchensee",
        "Womens Giant Slalom 2 at Tremblant",
        "Womens Giant Slalom at Copper Mt",
        "Womens Giant Slalom at Kranjska Gora",
        "Womens Giant Slalom at Kronplatz",
        "Womens Giant Slalom at Lillehammer",
        "Womens Giant Slalom at Semmering",
        "Womens Giant Slalom at Sölden",
        "Womens Giant Slalom at Tremblant",
        "Womens Giant Slalom at Åre",
        "Womens Giant Slalom at Špindlerův Mlýn",
        "Womens Slalom at Copper Mt",
        "Womens Slalom at Courchevel",
        "Womens Slalom at Flachau",
        "Womens Slalom at Gurgl",
        "Womens Slalom at Kranjska Gora",
        "Womens Slalom at Levi",
        "Womens Slalom at Lillehammer",
        "Womens Slalom at Semmering",
        "Womens Slalom at Åre",
        "Womens Slalom at Špindlerův Mlýn",
        "Womens Super G 2 at Soldeu",
        "Womens Super G at Crans-Montana",
        "Womens Super G at Lillehammer",
        "Womens Super G at Soldeu",
        "Womens Super G at St. Moritz",
        "Womens Super G at Tarvisio",
        "Womens Super G at Val d'Isère",
        "Womens Super G at Val di Fassa"
    };

    [Theory]
    [InlineData("Mens Downhill 2 at Val Gardena", "Men", "Downhill")]
    [InlineData("Womens Downhill at Zauchensee", "Women", "Downhill")]
    [InlineData("Mens Giant Slalom at Sölden", "Men", "Giant Slalom")]
    [InlineData("Womens Giant Slalom at Kronplatz", "Women", "Giant Slalom")]
    [InlineData("Mens Slalom at Levi", "Men", "Slalom")]
    [InlineData("Womens Slalom at Flachau", "Women", "Slalom")]
    [InlineData("Mens Super G at Wengen", "Men", "Super G")]
    [InlineData("Womens Super G at St. Moritz", "Women", "Super G")]
    [InlineData("Mens Downhill at Kitzbühel", "Men", "Downhill")]
    [InlineData("Womens Downhill at St. Moritz", "Women", "Downhill")]
    [InlineData("Mens Slalom at Madonna di Campiglio", "Men", "Slalom")]
    [InlineData("Womens Super G at Val d'Isère", "Women", "Super G")]
    public void RepresentativeTitlesParseExpectedFacets(string title, string expectedGender, string expectedDiscipline)
    {
        var facets = EventFacets.ParseTitle(Schema, title);

        facets.ContainsKey("gender").Should().BeTrue(title);
        facets["gender"].Should().Be(expectedGender);

        facets.ContainsKey("discipline").Should().BeTrue(title);
        facets["discipline"].Should().Be(expectedDiscipline);
    }

    [Fact]
    public void AllFixtureTitlesParseGenderAndDiscipline()
    {
        FisAlpineTitles.Should().HaveCount(76);
        FisAlpineTitles.Distinct().Should().HaveCount(76);

        foreach (var title in FisAlpineTitles)
        {
            var facets = EventFacets.ParseTitle(Schema, title);

            // Every hub title starts with the gender word and then the discipline.
            var expectedGender = title.StartsWith("Womens ") ? "Women" : "Men";
            var expectedDiscipline = new[] { "Giant Slalom", "Super G", "Downhill", "Slalom" }
                .First(d => title.Contains(d));
            facets.Should().ContainKey("gender").WhoseValue.Should().Be(expectedGender, title);
            facets.Should().ContainKey("discipline").WhoseValue.Should().Be(expectedDiscipline, title);
        }
    }

    [Fact]
    public void WomensTitleParsesWomenNotMen()
    {
        var facets = EventFacets.ParseTitle(Schema, "Womens Downhill at Zauchensee");

        facets["gender"].Should().Be("Women");
        facets["discipline"].Should().Be("Downhill");
    }

    [Fact]
    public void MensTitleParsesMen()
    {
        var facets = EventFacets.ParseTitle(Schema, "Mens Slalom at Levi");

        facets["gender"].Should().Be("Men");
        facets["discipline"].Should().Be("Slalom");
    }

    [Fact]
    public void TitleWordOrderDoesNotAffectParsedFacets()
    {
        var expected = EventFacets.ParseTitle(Schema, "Womens Super G at St. Moritz");
        var actual = EventFacets.ParseTitle(Schema, "St. Moritz Womens Super G");

        actual.Count.Should().Be(expected.Count);
        actual["gender"].Should().Be(expected["gender"]);
        actual["discipline"].Should().Be(expected["discipline"]);
    }

    [Fact]
    public void NumberedDownhillParsesAsDownhill()
    {
        var facets = EventFacets.ParseTitle(Schema, "Mens Downhill 2 at Val Gardena");

        facets["discipline"].Should().Be("Downhill");
    }

    [Fact]
    public void GiantSlalomIsNotSlalom()
    {
        var facets = EventFacets.ParseTitle(Schema, "Mens Giant Slalom at Sölden");

        facets["discipline"].Should().Be("Giant Slalom");
        facets["discipline"].Should().NotBe("Slalom");
    }

    [Theory]
    [InlineData("Womens Super-G at Zauchensee")]
    [InlineData("Womens SuperG at Soldeu")]
    public void SuperGVariantsParseAsSuperG(string title)
    {
        var facets = EventFacets.ParseTitle(Schema, title);

        facets["gender"].Should().Be("Women");
        facets["discipline"].Should().Be("Super G");
    }

    [Fact]
    public void GetSchemaReturnsFisAlpineOnlyForSkiing()
    {
        var schema = EventFacets.GetSchema("Skiing", "FIS Alpine Ski World Cup");
        schema.Should().NotBeNull();
        schema!.Id.Should().Be("fis-alpine");

        EventFacets.GetSchema("skiing", "  FIS ALPINE SKI WORLD CUP  ").Should().NotBeNull();
        EventFacets.GetSchema("Skiing", "FIS Alpine Masters").Should().BeNull();
        EventFacets.GetSchema("Motorsport", "Formula 1").Should().BeNull();
        EventFacets.GetSchema("Biathlon", "Biathlon World Cup").Should().BeNull();
    }

    [Fact]
    public void NullMonitoredFacetsMatchesEverything()
    {
        EventFacets
            .IsMonitored("Skiing", "FIS Alpine Ski World Cup", null, "Mens Downhill at Val Gardena")
            .Should()
            .BeTrue();
    }

    [Fact]
    public void EmptyRuleListMatchesNothing()
    {
        EventFacets
            .IsMonitored("Skiing", "FIS Alpine Ski World Cup", "[]", "Mens Downhill at Val Gardena")
            .Should()
            .BeFalse();
    }

    [Fact]
    public void WildcardRuleMatchesParsedFacets()
    {
        const string rules = @"[{""gender"":""*""}]";

        EventFacets
            .IsMonitored("Skiing", "FIS Alpine Ski World Cup", rules, "Mens Downhill at Val Gardena")
            .Should()
            .BeTrue();

        EventFacets
            .IsMonitored("Skiing", "FIS Alpine Ski World Cup", rules, "Womens Slalom at Levi")
            .Should()
            .BeTrue();

        EventFacets
            .IsMonitored("Skiing", "FIS Alpine Ski World Cup", rules, "Team Event at Are")
            .Should()
            .BeTrue();
    }

    [Fact]
    public void RuleValueListsMatchCaseInsensitively()
    {
        const string rules = @"[{""gender"":[""men"",""women""],""discipline"":[""Downhill"",""Slalom""]}]";

        EventFacets
            .IsMonitored("Skiing", "FIS Alpine Ski World Cup", rules, "Mens Downhill at Val Gardena")
            .Should()
            .BeTrue();

        EventFacets
            .IsMonitored("Skiing", "FIS Alpine Ski World Cup", rules, "Womens Slalom at Levi")
            .Should()
            .BeTrue();

        EventFacets
            .IsMonitored("Skiing", "FIS Alpine Ski World Cup", rules, "Mens Super G at Val Gardena")
            .Should()
            .BeFalse();
    }

    [Fact]
    public void ExactCombinationMatchesMenSpeedAndWomenSlalom()
    {
        const string rules =
            @"[{""gender"":""Men"",""discipline"":[""Downhill"",""Super G""]},{""gender"":""Women"",""discipline"":""Slalom""}]";

        EventFacets
            .IsMonitored("Skiing", "FIS Alpine Ski World Cup", rules, "Mens Downhill at Val Gardena")
            .Should()
            .BeTrue();

        EventFacets
            .IsMonitored("Skiing", "FIS Alpine Ski World Cup", rules, "Mens Super G at Val Gardena")
            .Should()
            .BeTrue();

        EventFacets
            .IsMonitored("Skiing", "FIS Alpine Ski World Cup", rules, "Mens Slalom at Levi")
            .Should()
            .BeFalse();

        EventFacets
            .IsMonitored("Skiing", "FIS Alpine Ski World Cup", rules, "Womens Slalom at Levi")
            .Should()
            .BeTrue();

        EventFacets
            .IsMonitored("Skiing", "FIS Alpine Ski World Cup", rules, "Womens Downhill at Zauchensee")
            .Should()
            .BeFalse();
    }

    [Fact]
    public void UnparsedTitleStaysMonitoredUnderNonEmptyRules()
    {
        const string rules = @"[{""gender"":""Men"",""discipline"":[""Downhill""]}]";

        EventFacets
            .IsMonitored("Skiing", "FIS Alpine Ski World Cup", rules, "Team Event at Are")
            .Should()
            .BeTrue();
    }

    [Fact]
    public void ParsedExcludedGenderRejectsEvenWhenDisciplineIsAbsent()
    {
        const string rules = @"[{""gender"":""Men""}]";

        EventFacets
            .IsMonitored("Skiing", "FIS Alpine Ski World Cup", rules, "Womens Team Event at Are")
            .Should()
            .BeFalse();

        EventFacets
            .IsMonitored("Skiing", "FIS Alpine Ski World Cup", rules, "Team Event at Are")
            .Should()
            .BeTrue();
    }

    [Fact]
    public void OmittedRuleDimensionActsAsWildcard()
    {
        const string rules = @"[{""discipline"":""Slalom""}]";

        EventFacets
            .IsMonitored("Skiing", "FIS Alpine Ski World Cup", rules, "Mens Slalom at Levi")
            .Should()
            .BeTrue();

        EventFacets
            .IsMonitored("Skiing", "FIS Alpine Ski World Cup", rules, "Womens Slalom at Levi")
            .Should()
            .BeTrue();

        EventFacets
            .IsMonitored("Skiing", "FIS Alpine Ski World Cup", rules, "Womens Giant Slalom at Kronplatz")
            .Should()
            .BeFalse();
    }

    [Fact]
    public void InvalidMonitoredFacetsArePermissive()
    {
        EventFacets
            .IsMonitored("Skiing", "FIS Alpine Ski World Cup", "{ bad", "Mens Downhill at Val Gardena")
            .Should()
            .BeTrue();

        EventFacets
            .IsMonitored("Skiing", "FIS Alpine Ski World Cup", "\"Men\"", "Mens Downhill at Val Gardena")
            .Should()
            .BeTrue();
    }

    [Fact]
    public void LeagueWithoutSchemaIgnoresMonitoredFacets()
    {
        var league = new League
        {
            Sport = "Motorsport",
            Name = "Formula 1",
            MonitoredFacets = "[]"
        };

        EventFacets.IsMonitored(league, "Mens Downhill at Val Gardena").Should().BeTrue();
    }

    [Fact]
    public void NormalizeForStorageCanonicalizesValues()
    {
        var json = @"[{""discipline"":[""super g""]}]";

        var normalized = EventFacets.NormalizeForStorage(Schema, json, out var error);

        error.Should().BeNull();
        normalized.Should().Be(@"[{""discipline"":[""Super G""]}]");
    }

    [Fact]
    public void NormalizeForStorageCollapsesFullValueListToWildcard()
    {
        var json = @"[{""gender"":[""Men"",""Women"",""Mixed""]}]";

        var normalized = EventFacets.NormalizeForStorage(Schema, json, out var error);

        error.Should().BeNull();
        normalized.Should().Be(@"[{""gender"":""*""}]");
    }

    [Fact]
    public void NormalizeForStorageReportsUnknownValue()
    {
        var json = @"[{""gender"":""Unknown""}]";

        var normalized = EventFacets.NormalizeForStorage(Schema, json, out var error);

        normalized.Should().BeNull();
        error.Should().Contain("Unknown");
    }

    [Fact]
    public void NormalizeForStorageReportsUnknownDimension()
    {
        var json = @"[{""unknown"":""Men""}]";

        var normalized = EventFacets.NormalizeForStorage(Schema, json, out var error);

        normalized.Should().BeNull();
        error.Should().Contain("unknown");
    }

    [Fact]
    public void NormalizeForStorageKeepsNullAsNull()
    {
        var normalized = EventFacets.NormalizeForStorage(Schema, null, out var error);

        normalized.Should().BeNull();
        error.Should().BeNull();
    }

    [Fact]
    public void NormalizeForStorageStoresListsInSchemaOrder()
    {
        var json = @"[{""discipline"":[""Super G"",""Downhill"",""downhill""],""gender"":""Men""},{""gender"":""Women"",""discipline"":""Slalom""}]";

        var normalized = EventFacets.NormalizeForStorage(Schema, json, out var error);

        error.Should().BeNull();
        normalized.Should().Be(@"[{""gender"":[""Men""],""discipline"":[""Downhill"",""Super G""]},{""gender"":[""Women""],""discipline"":[""Slalom""]}]");
    }

    [Fact]
    public void NormalizeForLeagueRefusesANewValueForALeagueWithoutFacets()
    {
        EventFacets.NormalizeForLeague("Motorsport", "Formula 1", null, @"[{""gender"":""Men""}]", null, out var error)
            .Should().BeNull();
        error.Should().NotBeNull();

        EventFacets.NormalizeForLeague("Motorsport", "Formula 1", null, null, null, out error).Should().BeNull();
        error.Should().BeNull("null is the default for every league");
    }

    [Fact]
    public void NormalizeForLeagueKeepsTheStoredValueWhenTheSchemaIsGone()
    {
        // A rename upstream can hide the schema. The edit dialog then sends
        // the stored value back, and the save must not fail on it.
        const string stored = @"[{""gender"":[""Women""]}]";

        EventFacets.NormalizeForLeague("Skiing", "Renamed Alpine Cup", null, stored, stored, out var error)
            .Should().Be(stored);
        error.Should().BeNull();

        EventFacets.NormalizeForLeague("Skiing", "Renamed Alpine Cup", null, @"[{""gender"":[""Men""]}]", stored, out error)
            .Should().BeNull();
        error.Should().NotBeNull();
    }

    [Fact]
    public void TheHubLeagueIdFindsTheSchemaAfterARename()
    {
        EventFacets.GetSchema("Skiing", "FIS Alpine Ski World Cup 2027", "lg-001231").Should().NotBeNull();
        EventFacets.GetSchema(null, null, "lg-001231").Should().NotBeNull();
        EventFacets.GetSchema("Skiing", "FIS Alpine Ski World Cup 2027", "lg-000001").Should().BeNull();

        var league = new League
        {
            Sport = "Skiing", Name = "Audi FIS Ski World Cup", ExternalId = "lg-001231",
            MonitoredFacets = @"[{""gender"":[""Women""]}]"
        };
        EventFacets.IsMonitored(league, "Mens Downhill at Wengen").Should().BeFalse();
        EventFacets.IsMonitored(league, "Womens Downhill at Wengen").Should().BeTrue();
    }

    [Fact]
    public void NormalizeForStorageCapsTheNumberOfRules()
    {
        var atLimit = "[" + string.Join(",", Enumerable.Repeat(@"{""gender"":""Men""}", EventFacets.MaxRules)) + "]";
        var overLimit = "[" + string.Join(",", Enumerable.Repeat(@"{""gender"":""Men""}", EventFacets.MaxRules + 1)) + "]";

        EventFacets.NormalizeForStorage(Schema, atLimit, out var error).Should().NotBeNull();
        error.Should().BeNull();

        EventFacets.NormalizeForStorage(Schema, overLimit, out error).Should().BeNull();
        error.Should().Contain(EventFacets.MaxRules.ToString());
    }

    [Theory]
    [InlineData("Womens Super Giant Slalom at Cortina")]
    [InlineData("Mens super giant at Kvitfjell")]
    [InlineData("Mens Super-G at Kvitfjell")]
    [InlineData("Mens SuperG at Kvitfjell")]
    [InlineData("Mens Supergiant at Kvitfjell")]
    public void SuperGiantIsSuperGNeverGiantSlalom(string title)
    {
        EventFacets.ParseTitle(Schema, title)["discipline"].Should().Be("Super G");
    }

    [Fact]
    public void LadiesAndApostropheFormsParse()
    {
        EventFacets.ParseTitle(Schema, "Ladies' Downhill at Cortina")["gender"].Should().Be("Women");
        EventFacets.ParseTitle(Schema, "Women's Slalom at Levi")["gender"].Should().Be("Women");
        EventFacets.ParseTitle(Schema, "Men's Slalom at Levi")["gender"].Should().Be("Men");
        EventFacets.ParseTitle(Schema, "Mixed Team Parallel at Lech")["gender"].Should().Be("Mixed");
        EventFacets.ParseTitle(Schema, "Mixed Team Parallel at Lech")["discipline"].Should().Be("Parallel");
        EventFacets.ParseTitle(Schema, "Mens Alpine Combined at Kitzbühel")["discipline"].Should().Be("Combined");
    }

    [Theory]
    // Motorsport session words and other winter sport words must not parse.
    [InlineData("Womens 7.5km Sprint at Hochfilzen")]
    [InlineData("Formula 1 Belgian Grand Prix Sprint")]
    public void OtherSportWordsAreNotDisciplines(string title)
    {
        EventFacets.ParseTitle(Schema, title).Should().NotContainKey("discipline");
    }

    [Theory]
    [InlineData("FIS.Alpine.Ski.World.Cup.2026.Womens.Super.G.St.Moritz.1080p.WEB.h264")]
    [InlineData("Biathlon.World.Cup.2026.Womens.7.5km.Sprint.Hochfilzen.1080p.WEB.h264")]
    [InlineData("Mens Downhill at Val Gardena")]
    public void TheMotorsportFilenameSweepNeverReturnsAFacetValue(string name)
    {
        // The sweep runs every league's session table when it does not know the
        // league. Facet values live elsewhere, so a ski name cannot reach it.
        var session = EventPartDetector.DetectMotorsportSessionFromFilename(name);
        var facetValues = Schema.Dimensions.SelectMany(d => d.Values);
        if (session != null)
        {
            facetValues.Should().NotContain(session);
        }
    }

    [Fact]
    public void NoMotorsportSessionTableNamesAFacetValue()
    {
        var facetValues = Schema.Dimensions.SelectMany(d => d.Values).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var league in new[] { "Formula 1", "IndyCar", "MotoGP", "NASCAR", "WEC", "Formula E", "Supercars" })
        {
            EventPartDetector.GetMotorsportSessionTypes(league)
                .Should().NotContain(s => facetValues.Contains(s), league);
        }
    }
}
