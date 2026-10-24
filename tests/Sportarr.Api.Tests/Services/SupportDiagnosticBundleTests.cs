using FluentAssertions;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class SupportDiagnosticBundleTests
{
    [Fact]
    public void Build_RedactsCredentialsButKeepsTroubleshootingLocations()
    {
        var files = SupportDiagnosticBundle.Build(
            "[ERR] Connection to 192.0.2.5 failed at /home/alex/media Password=secret123\n",
            "sportarr.txt", "4.1.7", "Linux", false);

        files.Should().ContainSingle();
        files[0].Content.Should().Contain("192.0.2.5");
        files[0].Content.Should().Contain("/home/[USER]/media");
        files[0].Content.Should().NotContain("secret123");
        files[0].Content.Should().Contain("===== SECTION: RECENT ERRORS =====");
    }

    [Fact]
    public void Build_RedactsSecretValueOnFollowingLine()
    {
        var files = SupportDiagnosticBundle.Build(
            "[ERR] Response: \"client_secret\":\n\"actual-secret\"\n[INF] Next line\n",
            "sportarr.txt", "4.1.8", "Linux", false);

        files[0].Content.Should().NotContain("actual-secret");
        files[0].Content.Should().Contain("[INF] Next line");
    }

    [Fact]
    public void Build_MasksAccountNameAndPublicIpButKeepsPrivateIp()
    {
        var files = SupportDiagnosticBundle.Build(
            "Created session for user synthetic_owner from IP 8.8.8.8; destination 192.168.1.42:8080; route 2606:4700:4700::1111\n",
            "sportarr.txt", "4.1.8", "Linux", false);

        files[0].Content.Should().NotContain("synthetic_owner");
        files[0].Content.Should().NotContain("8.8.8.8");
        files[0].Content.Should().NotContain("2606:4700:4700::1111");
        files[0].Content.Should().Contain("192.168.1.42:8080");
    }

    [Fact]
    public void Build_KeepsDottedSportarrVersionInBundleHeader()
    {
        var files = SupportDiagnosticBundle.Build("", "sportarr.txt", "4.1.7.82", "Linux", false);

        files[0].Content.Should().Contain("Version: 4.1.7.82");
    }

    [Fact]
    public void Build_KeepsOnlyTheNewestLogPartInOneSmallFile()
    {
        var log = "OLDEST LINE MARKER\n" + string.Concat(Enumerable.Repeat("[2026-10-18 10:00:00.000 +00:00] [INF] filler line\n", 120_000))
            + "[2026-10-18 10:00:00.000 +00:00] [ERR] Early failure\n"
            + string.Concat(Enumerable.Repeat("[2026-10-18 11:00:00.000 +00:00] [INF] filler line\n", 120_000))
            + "NEWEST LINE MARKER";

        var files = SupportDiagnosticBundle.Build(log, "sportarr.txt", "4.1.7", "Linux", false);

        files.Should().ContainSingle();
        files[0].Filename.Should().Be("sportarr-support-01.txt");
        System.Text.Encoding.UTF8.GetByteCount(files[0].Content).Should().BeLessThan(3 * 1024 * 1024);
        files[0].Content.Should().StartWith("SPORTARR SUPPORT BUNDLE v1");
        files[0].Content.Should().Contain("NEWEST LINE MARKER");
        files[0].Content.Should().NotContain("OLDEST LINE MARKER");
        files[0].Content.Should().Contain("Only the newest part of the log is included");
        // The error section still covers the part of the log that was cut.
        files[0].Content.Should().Contain("[ERR] Early failure");
    }

    [Fact]
    public void Build_GroupsRepeatedWarningsWithACount()
    {
        var log = string.Join('\n',
            "[2026-10-18 10:00:01.000 +00:00] [WRN] Indexer 4 timed out after 30 seconds",
            "[2026-10-18 10:00:02.000 +00:00] [WRN] Indexer 4 timed out after 31 seconds",
            "[2026-10-18 10:00:03.000 +00:00] [WRN] Indexer 4 timed out after 32 seconds",
            "[2026-10-18 10:00:04.000 +00:00] [WRN] Disk space is low");

        var content = SupportDiagnosticBundle.Build(log, "sportarr.txt", "4.1.8", "Linux", false)[0].Content;
        var warnings = content.Split("===== SECTION: RECENT WARNINGS =====\n")[1].Split("===== SECTION:")[0];

        warnings.Should().Contain("3x [2026-10-18 10:00:03.000 +00:00] [WRN] Indexer 4 timed out after 32 seconds");
        warnings.Should().Contain("[WRN] Disk space is low");
        warnings.Should().NotContain("after 30 seconds");
    }

    [Fact]
    public void Build_AddsRedactedRecentSearches()
    {
        var searches = "2026-10-18 10:00 UTC  Monza Grand Prix  automatic\n"
            + "    - https://tracker.example/announce?passkey=abc123 | WEBDL-720p | 3.20 GB | rejected";

        var content = SupportDiagnosticBundle.Build("", "sportarr.txt", "4.1.8", "Linux", false, searches)[0].Content;

        content.Should().Contain("===== SECTION: RECENT SEARCHES =====\n2026-10-18 10:00 UTC  Monza Grand Prix");
        content.Should().NotContain("abc123");
    }

    [Theory]
    [InlineData("https://tracker.example/announce?passkey=abc123&left=1", "abc123")]
    [InlineData("https://tracker.example/announce?authkey=AuthKeySecret83&torrent_pass=PassSecret75", "AuthKeySecret83")]
    [InlineData("https://tracker.example/announce?authkey=AuthKeySecret83&torrent_pass=PassSecret75", "PassSecret75")]
    [InlineData("https://indexer.example/api?jackett_apikey=JackettKey79", "JackettKey79")]
    [InlineData("https://indexer.example/api?t=get&r=NewznabKey66", "NewznabKey66")]
    [InlineData("https://tv.example/live/viewer/shortpass/42.ts", "shortpass")]
    [InlineData("https://tv.example/timeshift/viewer/shortpass/60/2026-10-06:12-00/42.ts", "shortpass")]
    [InlineData("https://api.telegram.org/bot123456789:TelegramSecret82/sendMessage", "TelegramSecret82")]
    [InlineData("https://calendar.example/private-CalendarSecret46/basic.ics", "CalendarSecret46")]
    [InlineData("https://feed.example/data/c67edf10a4c9475aba6f914f6098ec12/events", "c67edf10a4c9475aba6f914f6098ec12")]
    [InlineData("https://indexer.example/api?rsskey=abc123", "abc123")]
    [InlineData("{\"cookie\":\"abc123\"}", "abc123")]
    [InlineData("ConnectionString=Server=localhost;Password=abc123", "abc123")]
    [InlineData("https://discord.com/api/webhooks/123456789/abc123", "abc123")]
    [InlineData("https://ptb.discord.com/api/webhooks/123456789/abc123", "abc123")]
    [InlineData("https://canary.discord.com/api/webhooks/123456789/abc123", "abc123")]
    [InlineData("https://hooks.slack.com/services/T000/B000/abc123", "abc123")]
    [InlineData("[Webhook] Sending POST to https://home.example/api/webhook/abc123", "abc123")]
    [InlineData("[Webhook] HTTP request failed for http://home.example/api/webhook/abc123", "abc123")]
    [InlineData("https://notifiarr.com/api/v1/notification/passthrough/abc123", "abc123")]
    [InlineData("auth: abc123", "abc123")]
    [InlineData("Cookie: session=abc123; refresh=def456", "def456")]
    [InlineData("Password=\"my secret phrase\"", "my secret phrase")]
    [InlineData("https://user:pa/ss@tracker.example/path", "pa/ss")]
    [InlineData("Please email alice@example.com", "alice@example.com")]
    public void Build_RedactsAdditionalSecrets(string logLine, string secret)
    {
        var files = SupportDiagnosticBundle.Build(logLine, "sportarr.txt", "4.1.8", "Linux", false);

        files[0].Content.Should().NotContain(secret);
    }

    [Fact]
    public void Build_KeepsShortServicePathsForConnectionTroubleshooting()
    {
        var content = SupportDiagnosticBundle.Build(
            "[WRN] https://seedbox.example/RPC2 returned 400", "sportarr.txt", "4.1.8", "Linux", false)[0].Content;

        content.Should().Contain("https://seedbox.example/RPC2 returned 400");
    }

    [Fact]
    public void Build_PreservesLaterLinesWhenAUrlHasNoBasicCredentials()
    {
        var log = "[ERR] Connecting to https://indexer.example:443/api failed\n" +
            "[ERR] Reporter @helper found a different problem\n" +
            "[INF] https://user:private123@tracker.example/announce succeeded\n";

        var content = SupportDiagnosticBundle.Build(log, "sportarr.txt", "4.1.8", "Linux", false)[0].Content;

        content.Should().Contain("https://indexer.example:443/api failed");
        content.Should().Contain("Reporter @helper found a different problem");
        content.Should().NotContain("private123");
    }

    [Fact]
    public void Read_RejectsInvalidNamesAndMissingFiles()
    {
        var path = Path.Combine(Path.GetTempPath(), $"support-bundle-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        try
        {
            Action traversal = () => SupportDiagnosticBundle.Read(path, "../sportarr.txt", "4.1.8", "Linux");
            Action wrongType = () => SupportDiagnosticBundle.Read(path, "sportarr.zip", "4.1.8", "Linux");
            Action blank = () => SupportDiagnosticBundle.Read(path, "", "4.1.8", "Linux");
            Action missing = () => SupportDiagnosticBundle.Read(path, "missing.txt", "4.1.8", "Linux");

            traversal.Should().Throw<ArgumentException>();
            wrongType.Should().Throw<ArgumentException>();
            blank.Should().Throw<ArgumentException>();
            missing.Should().Throw<FileNotFoundException>();
        }
        finally
        {
            Directory.Delete(path, recursive: true);
        }
    }

    [Fact]
    public void Read_OmitsPartialFirstLineWhenLargeFileIsTruncated()
    {
        var path = Path.Combine(Path.GetTempPath(), $"support-bundle-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        try
        {
            var filename = "sportarr.txt";
            var marker = "PARTIAL_LINE_MARKER";
            var partialLine = new string('Y', 100) + marker
                + new string('Y', 18 * 1024 * 1024 - 100 - marker.Length);
            File.WriteAllText(Path.Combine(path, filename),
                "FIRST LINE START " + new string('X', 1024 * 1024) + "\n"
                + partialLine + "\n[ERR] Recent failure\n");

            var files = SupportDiagnosticBundle.Read(path, filename, "4.1.8", "Linux");

            files.Should().OnlyContain(file => !file.Content.Contains("FIRST LINE START"));
            files.Should().OnlyContain(file => !file.Content.Contains(marker));
            files.Should().Contain(file => file.Content.Contains("[ERR] Recent failure"));
            files[0].Content.Should().Contain("Older log content was omitted");
        }
        finally
        {
            Directory.Delete(path, recursive: true);
        }
    }

    [Fact]
    public void Build_CutsOversizedTextWithoutLeavingABrokenUtf8Character()
    {
        const int maximum = 20 * 1024 * 1024;
        var source = new string('A', 1024 * 1024) + "€" + new string('B', maximum - 1);

        var files = SupportDiagnosticBundle.Build(source, "sportarr.txt", "4.1.8", "Linux", false);

        files[0].Content.Should().Contain("Older log content was omitted");
        files.Should().OnlyContain(file => !file.Content.Contains('\uFFFD'));
        files.Should().OnlyContain(file => !file.Content.Contains('€'));
        files.Should().Contain(file => file.Content.Contains('B'));
    }

    [Fact]
    public void Build_DoesNotCutAMultibyteCharacterAtTheTailStart()
    {
        // Two bytes per character and one trailing byte, so the cut lands mid-character.
        var source = string.Concat(Enumerable.Repeat("\u00e9", 2 * 1024 * 1024)) + "x";

        var files = SupportDiagnosticBundle.Build(source, "sportarr.txt", "4.1.8", "Linux", false);

        files.Should().ContainSingle();
        files[0].Content.Should().NotContain("\uFFFD");
        files[0].Content.Should().Contain("\u00e9\u00e9");
    }
}
