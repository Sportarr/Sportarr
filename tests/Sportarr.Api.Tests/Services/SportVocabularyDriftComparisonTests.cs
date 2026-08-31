using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Data;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Xunit;
using Xunit.Abstractions;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// Evidence for a PROPOSED change that is deliberately not implemented.
///
/// The sport mismatch penalty compares the parsed sport against Event.Sport.
/// On a real library the Event row and the League row disagree for several
/// leagues, so a release is penalised 50 points against its own correct event.
/// Values below were read from a live server on 2026-08-29.
///
///   league               League.Sport        Event.Sport   drift
///   NFL                  American Football   Football      yes
///   NCAA Division 1      American Football   Football      yes
///   NHL                  Ice Hockey          Hockey        yes
///   UFC                  Fighting            Combat        yes
///   Boxing               Fighting            Combat        yes
///   MLB, MotoGP, F1, V8 Supercars, WorldSSP, FA Cup, UEFA Champions League,
///   ATP, WTA, LPGA, PDC Darts, UCI World Tour, Bellator, ONE, MVP MMA,
///   Olympics Ice Hockey                                    no
///
/// Proposal: prefer League.Sport over Event.Sport for the mismatch comparison,
/// because the League row is curated and already agrees with the parser.
/// Modelled here by scoring an event whose Sport reads the league's value.
/// </summary>
public class SportVocabularyDriftComparisonTests
{
    private readonly ITestOutputHelper _out;
    public SportVocabularyDriftComparisonTests(ITestOutputHelper o) => _out = o;

    private const int SuggestionThreshold = 50;

    private static readonly MediaFileParser Media = new(NullLogger<MediaFileParser>.Instance);
    private static readonly SportsFileNameParser Sports = new(NullLogger<SportsFileNameParser>.Instance);
    private static readonly EventPartDetector Parts = new(NullLogger<EventPartDetector>.Instance);

    private static ImportMatchingService Svc()
    {
        var o = new DbContextOptionsBuilder<SportarrDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new ImportMatchingService(new SportarrDbContext(o), Media, Sports, Parts,
            NullLogger<ImportMatchingService>.Instance);
    }

    public sealed record Case(
        string League, string Release, string CorrectTitle,
        string EventSport, string LeagueSport, string WrongTitle,
        string? Round = null, string? WrongRound = null);

    private static Event Build(int id, string title, string eventSport, string league,
        string leagueSport, string? round) => new()
    {
        Id = id,
        Title = title,
        Sport = eventSport,
        Round = round,
        EventDate = new DateTime(2026, 6, 15, 18, 0, 0, DateTimeKind.Utc),
        BroadcastDate = new DateTime(2026, 6, 15),
        League = new League { Id = id, Name = league, Sport = leagueSport }
    };

    /// <summary>Every league on the live server, with its real sport values.</summary>
    public static TheoryData<Case> Corpus() =>
    [
        // --- leagues where the two vocabularies DRIFT ---
        new("NFL", "NFL.2026.Week.5.Arizona.Cardinals.vs.San.Francisco.49ers.1080p.WEB",
            "Arizona Cardinals vs San Francisco 49ers", "Football", "American Football",
            "Kansas City Chiefs vs Denver Broncos"),
        new("NCAA Division 1", "NCAA.2026.Alabama.vs.Georgia.1080p.WEB",
            "Alabama vs Georgia", "Football", "American Football", "Ohio State vs Michigan"),
        new("NHL", "NHL.2026.02.14.Toronto.Maple.Leafs.vs.Montreal.Canadiens.1080p.WEB",
            "Toronto Maple Leafs vs Montreal Canadiens", "Hockey", "Ice Hockey",
            "Boston Bruins vs New York Rangers"),
        new("UFC", "UFC 310 Prelims 1080p WEB-DL",
            "UFC 310", "Combat", "Fighting", "UFC 311"),
        new("Boxing", "Boxing 2026 06 15 Fury vs Usyk 1080p WEB",
            "Fury vs Usyk", "Combat", "Fighting", "Joshua vs Wilder"),

        // --- leagues where the vocabularies AGREE (regression guard) ---
        new("MLB", "MLB - S2026E135 - San Francisco Giants vs Houston Astros - HDTV-1080p",
            "San Francisco Giants vs Houston Astros", "Baseball", "Baseball",
            "New York Yankees vs Boston Red Sox"),
        new("NBA", "NBA - S2026E410 - Boston Celtics vs Miami Heat - WEB-DL-1080p",
            "Boston Celtics vs Miami Heat", "Basketball", "Basketball",
            "Los Angeles Lakers vs Golden State Warriors"),
        new("Bellator", "Bellator 320 1080p WEB-DL",
            "Bellator 320", "Fighting", "Fighting", "Bellator 321"),
        new("ONE", "ONE Championship 168 1080p WEB",
            "ONE 168", "Fighting", "Fighting", "ONE 169"),
        new("MVP MMA", "MVP MMA 2026 06 15 Main Card 1080p",
            "MVP MMA 12", "Combat", "Combat", "MVP MMA 13"),
        new("Formula 1", "Formula 1 2026 Round 12 British Grand Prix Race 1080p",
            "British Grand Prix", "Motorsport", "Motorsport", "Hungarian Grand Prix", "12", "13"),
        new("MotoGP", "MotoGP 2026 Round 09 Italian GP Race 1080p WEB",
            "Italian GP", "Motorsport", "Motorsport", "Dutch GP", "9", "10"),
        new("V8 Supercars", "V8 Supercars 2026 Round 04 Perth SuperSprint 1080p",
            "Perth SuperSprint", "Motorsport", "Motorsport", "Darwin Triple Crown"),
        new("WorldSSP", "WSBK 2026 Round 06 Misano Race 1 1080p",
            "Misano Race 1", "Motorsport", "Motorsport", "Donington Race 1"),
        new("UEFA Champions League", "UCL 2026 04 15 Real Madrid vs Bayern Munich 1080p",
            "Real Madrid vs Bayern Munich", "Soccer", "Soccer", "Barcelona vs Inter Milan"),
        new("FA Cup", "FA Cup 2026 05 16 Manchester City vs Chelsea 1080p",
            "Manchester City vs Chelsea", "Soccer", "Soccer", "Arsenal vs Liverpool"),
        new("ATP World Tour", "ATP 2026 Wimbledon Final Alcaraz vs Sinner 1080p",
            "Wimbledon Final", "Tennis", "Tennis", "US Open Final"),
        new("WTA Tour", "WTA 2026 Roland Garros Final Swiatek vs Gauff 1080p",
            "Roland Garros Final", "Tennis", "Tennis", "Australian Open Final"),
        new("LPGA Tour", "LPGA 2026 US Womens Open Round 4 1080p",
            "US Womens Open", "Golf", "Golf", "Womens PGA Championship"),
        new("PDC Darts", "PDC 2026 World Championship Final 1080p",
            "World Championship Final", "Darts", "Darts", "World Matchplay Final"),
        new("UCI World Tour", "UCI 2026 Tour de France Stage 12 1080p",
            "Tour de France Stage 12", "Cycling", "Cycling", "Giro d Italia Stage 12"),
        new("Olympics Ice Hockey", "Olympics 2026 Ice Hockey Final Canada vs USA 1080p",
            "Ice Hockey Final Canada vs USA", "Hockey", "Hockey", "Ice Hockey Bronze Match"),
    ];

    [Theory]
    [MemberData(nameof(Corpus))]
    public void CurrentVersusProposed(Case c)
    {
        var svc = Svc();
        var sports = Sports.Parse(c.Release);
        var media = Media.Parse(c.Release);
        var searchTitle = sports.Confidence >= 60 && !string.IsNullOrEmpty(sports.EventTitle)
            ? sports.EventTitle!
            : media.EventTitle;
        var part = Parts.DetectPart(c.Release, sports.Sport ?? "Fighting")?.SegmentName;

        var correct = Build(1, c.CorrectTitle, c.EventSport, c.League, c.LeagueSport, c.Round);
        var wrong = Build(2, c.WrongTitle, c.EventSport, c.League, c.LeagueSport, c.WrongRound ?? c.Round);

        int Cur(Event e) => svc.CalculateMatchConfidence(searchTitle, e.Title, part, e, sports);
        int New(Event e)
        {
            var viaLeague = Build(e.Id, e.Title, e.League?.Sport ?? e.Sport, c.League, c.LeagueSport, e.Round);
            return svc.CalculateMatchConfidence(searchTitle, e.Title, part, viaLeague, sports);
        }

        int curOk = Cur(correct), newOk = New(correct);
        int curBad = Cur(wrong), newBad = New(wrong);
        var drift = !string.Equals(c.EventSport, c.LeagueSport, StringComparison.OrdinalIgnoreCase);

        _out.WriteLine(
            $"{c.League,-24} drift={(drift ? "YES" : "no "),-3} parsedSport='{sports.Sport}' " +
            $"correct {curOk,4}->{newOk,4}  wrong {curBad,4}->{newBad,4}  " +
            $"gap {curOk - curBad,4}->{newOk - newBad,4}  " +
            $"threshold {(curOk >= SuggestionThreshold ? "PASS" : "FAIL")}->{(newOk >= SuggestionThreshold ? "PASS" : "FAIL")}");

        // The proposal must never make the correct event score lower, and must
        // never shrink the distance between the correct event and a wrong one.
        Assert.True(newOk >= curOk, $"{c.League}: correct event dropped {curOk} -> {newOk}");
        Assert.True(newOk - newBad >= curOk - curBad,
            $"{c.League}: separation shrank {curOk - curBad} -> {newOk - newBad}");
    }
}
