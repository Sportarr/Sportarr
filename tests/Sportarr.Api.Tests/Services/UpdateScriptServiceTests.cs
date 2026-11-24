using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sportarr.Api.Services;

namespace Sportarr.Api.Tests.Services;

public class UpdateScriptServiceTests : IDisposable
{
    private const string NewVersion = "999.0.0.1";

    private static readonly string ReleaseJson = $$"""
        {
          "tag_name": "v{{NewVersion}}",
          "html_url": "https://github.com/Sportarr/Sportarr/releases/tag/v{{NewVersion}}",
          "assets": [
            { "name": "sportarr-plex-legacy-plugin_{{NewVersion}}.zip", "browser_download_url": "https://example.test/plex.zip" },
            { "name": "Sportarr-linux-arm64-{{NewVersion}}.tar.gz", "browser_download_url": "https://example.test/linux-arm64.tar.gz" },
            { "name": "Sportarr-linux-x64-{{NewVersion}}.tar.gz", "browser_download_url": "https://example.test/linux-x64.tar.gz" },
            { "name": "Sportarr-osx-arm64-{{NewVersion}}.tar.gz", "browser_download_url": "https://example.test/osx-arm64.tar.gz" },
            { "name": "Sportarr-osx-x64-{{NewVersion}}.tar.gz", "browser_download_url": "https://example.test/osx-x64.tar.gz" },
            { "name": "Sportarr-Setup-{{NewVersion}}.exe", "browser_download_url": "https://example.test/setup.exe" },
            { "name": "Sportarr-win-x64-{{NewVersion}}.zip", "browser_download_url": "https://example.test/win-x64.zip" }
          ]
        }
        """;

    private readonly string _dataPath = Path.Combine(Path.GetTempPath(), $"update-script-{Guid.NewGuid():N}");
    private readonly FakeRunner _runner = new();
    private readonly FakeSafety _safety = new();
    private readonly FakeTime _time = new(new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero));
    private Func<HttpResponseMessage> _github = () => Json(ReleaseJson);
    private int _githubCalls;

    public UpdateScriptServiceTests()
    {
        Directory.CreateDirectory(_dataPath);
    }

    public void Dispose()
    {
        Directory.Delete(_dataPath, recursive: true);
    }

    [Fact]
    public async Task Runs_the_script_once_for_a_new_version_with_the_details_in_the_environment()
    {
        WriteConfig(automatic: true, mechanism: "Script");
        var service = CreateService();

        (await service.RunIfDueAsync()).Should().Be(UpdateScriptOutcome.Succeeded);
        (await service.RunIfDueAsync()).Should().Be(UpdateScriptOutcome.AlreadyRun);

        _runner.Calls.Should().ContainSingle();
        var call = _runner.Calls[0];
        call.Path.Should().Be("/scripts/update.sh");
        call.Env["SPORTARR_UPDATE_VERSION"].Should().Be(NewVersion);
        call.Env["SPORTARR_CURRENT_VERSION"].Should().Be("4.1.12.1121");
        call.Env["SPORTARR_UPDATE_URL"].Should().Be($"https://github.com/Sportarr/Sportarr/releases/tag/v{NewVersion}");
        call.Env["SPORTARR_UPDATE_DOWNLOAD_URL"].Should().Be(LatestReleaseClient.SelectDownloadUrl(
            LatestReleaseClient.Parse(ReleaseJson)!.Assets, LatestReleaseClient.CurrentOs(), LatestReleaseClient.CurrentArch()));
        call.Env["SPORTARR_INSTALL_DIR"].Should().Be(AppContext.BaseDirectory);
        call.Env["SPORTARR_DATA_DIR"].Should().Be(Path.GetFullPath(_dataPath));
    }

    [Fact]
    public async Task Waits_while_Sportarr_records_or_imports_and_runs_later()
    {
        WriteConfig(automatic: true, mechanism: "Script");
        _safety.Busy = "A recording is in progress.";
        var service = CreateService();

        (await service.RunIfDueAsync()).Should().Be(UpdateScriptOutcome.Busy);
        _runner.Calls.Should().BeEmpty();

        _safety.Busy = null;
        (await service.RunIfDueAsync()).Should().Be(UpdateScriptOutcome.Succeeded);
        _runner.Calls.Should().ContainSingle();
    }

    [Fact]
    public async Task The_script_check_reads_the_answer_of_the_last_scheduled_check()
    {
        WriteConfig(automatic: true, mechanism: "Script");
        _runner.ExitCode = 1;
        var service = CreateService();

        (await service.RunIfDueAsync()).Should().Be(UpdateScriptOutcome.Failed);
        _time.Advance(TimeSpan.FromDays(2));
        (await service.RunIfDueAsync()).Should().Be(UpdateScriptOutcome.Failed);

        _githubCalls.Should().Be(0);
        _runner.Calls.Should().HaveCount(2);
    }

    [Fact]
    public async Task Does_nothing_when_automatic_updates_are_off()
    {
        WriteConfig(automatic: false, mechanism: "Script");

        (await CreateService().RunIfDueAsync()).Should().Be(UpdateScriptOutcome.AutomaticOff);

        _runner.Calls.Should().BeEmpty();
        _githubCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("Docker")]
    [InlineData("External")]
    [InlineData("BuiltIn")]
    [InlineData("Script")]
    public async Task An_old_automatic_tick_never_runs_the_script(string mechanism)
    {
        // v4.1.12 and earlier stored this tick. Only the new one counts.
        WriteConfig(automatic: null, mechanism: mechanism, oldTick: true);

        (await CreateService().RunIfDueAsync()).Should().Be(UpdateScriptOutcome.AutomaticOff);

        _runner.Calls.Should().BeEmpty();
        _githubCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("Docker")]
    [InlineData("External")]
    [InlineData("BuiltIn")]
    [InlineData("Apt")]
    [InlineData("")]
    public async Task A_leftover_script_path_with_another_old_mechanism_does_nothing(string mechanism)
    {
        // Older versions kept the path after the user switched away from Script.
        WriteConfig(automatic: true, mechanism: mechanism);

        (await CreateService().RunIfDueAsync()).Should().Be(UpdateScriptOutcome.NoScriptPath);

        _runner.Calls.Should().BeEmpty();
        _githubCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("Script")]
    [InlineData("script")]
    public async Task A_real_script_config_keeps_working(string mechanism)
    {
        WriteConfig(automatic: true, mechanism: mechanism);

        (await CreateService().RunIfDueAsync()).Should().Be(UpdateScriptOutcome.Succeeded);

        _runner.Calls.Should().ContainSingle();
    }

    [Fact]
    public async Task Does_nothing_without_a_script_path()
    {
        WriteConfig(automatic: true, mechanism: "script", scriptPath: "  ");

        (await CreateService().RunIfDueAsync()).Should().Be(UpdateScriptOutcome.NoScriptPath);

        _runner.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Does_nothing_when_the_running_version_is_current()
    {
        WriteConfig(automatic: true, mechanism: "Script");
        _github = () => Json("""{ "tag_name": "v1.0.0", "html_url": "https://example.test", "assets": [] }""");

        (await CreateService().RunIfDueAsync()).Should().Be(UpdateScriptOutcome.UpToDate);

        _runner.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task A_failed_script_runs_again_for_that_version_only_after_one_day()
    {
        WriteConfig(automatic: true, mechanism: "Script");
        _runner.ExitCode = 1;
        var service = CreateService();

        (await service.RunIfDueAsync()).Should().Be(UpdateScriptOutcome.Failed);

        _time.Advance(TimeSpan.FromHours(23));
        (await service.RunIfDueAsync()).Should().Be(UpdateScriptOutcome.WaitingToRetry);
        _runner.Calls.Should().HaveCount(1);

        _time.Advance(TimeSpan.FromHours(2));
        _runner.ExitCode = 0;
        (await service.RunIfDueAsync()).Should().Be(UpdateScriptOutcome.Succeeded);
        _runner.Calls.Should().HaveCount(2);
    }

    [Fact]
    public async Task The_retry_wait_survives_a_restart()
    {
        WriteConfig(automatic: true, mechanism: "Script");
        _runner.ExitCode = 2;
        (await CreateService().RunIfDueAsync()).Should().Be(UpdateScriptOutcome.Failed);

        // A new service reads config.xml from disk, as after a restart.
        _time.Advance(TimeSpan.FromHours(2));
        (await CreateService().RunIfDueAsync()).Should().Be(UpdateScriptOutcome.WaitingToRetry);

        _runner.Calls.Should().HaveCount(1);
        File.ReadAllText(Path.Combine(_dataPath, "config.xml"))
            .Should().Contain($"<AutoUpdateLastVersion>{NewVersion}</AutoUpdateLastVersion>");
    }

    [Fact]
    public async Task A_script_that_does_not_start_counts_as_a_failure()
    {
        WriteConfig(automatic: true, mechanism: "Script");
        _runner.Result = new UpdateScriptRunResult(false, null, Error: "No such file");
        var service = CreateService();

        (await service.RunIfDueAsync()).Should().Be(UpdateScriptOutcome.Failed);
        (await service.RunIfDueAsync()).Should().Be(UpdateScriptOutcome.WaitingToRetry);
    }

    [Fact]
    public async Task A_newer_version_runs_the_script_again()
    {
        WriteConfig(automatic: true, mechanism: "Script", lastVersion: "998.0.0.1", lastSucceeded: true);

        (await CreateService().RunIfDueAsync()).Should().Be(UpdateScriptOutcome.Succeeded);

        _runner.Calls.Should().ContainSingle();
    }

    public static IEnumerable<object[]> GitHubFailures()
    {
        yield return new object[] { "server error" };
        yield return new object[] { "dns failure" };
        yield return new object[] { "timeout" };
        yield return new object[] { "bad json" };
    }

    [Theory]
    [MemberData(nameof(GitHubFailures))]
    public async Task Skips_quietly_when_GitHub_cannot_be_reached(string failure)
    {
        WriteConfig(automatic: true, mechanism: "Script");
        _github = failure switch
        {
            "server error" => () => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            "dns failure" => () => throw new HttpRequestException("No such host is known"),
            "timeout" => () => throw new TaskCanceledException("The request timed out"),
            _ => () => Json("<html>unicorn</html>")
        };
        var before = File.ReadAllText(Path.Combine(_dataPath, "config.xml"));

        (await CreateService().RunIfDueAsync()).Should().Be(UpdateScriptOutcome.GitHubUnreachable);

        _runner.Calls.Should().BeEmpty();
        File.ReadAllText(Path.Combine(_dataPath, "config.xml")).Should().Be(before);
    }

    [Theory]
    [InlineData("linux", "x64", "https://example.test/linux-x64.tar.gz")]
    [InlineData("linux", "arm64", "https://example.test/linux-arm64.tar.gz")]
    [InlineData("osx", "arm64", "https://example.test/osx-arm64.tar.gz")]
    [InlineData("win", "x64", "https://example.test/win-x64.zip")]
    [InlineData("win", "arm64", "")]
    [InlineData(null, "x64", "")]
    public void Picks_the_archive_for_this_system(string? os, string arch, string expected)
    {
        var assets = LatestReleaseClient.Parse(ReleaseJson)!.Assets;

        LatestReleaseClient.SelectDownloadUrl(assets, os, arch).Should().Be(expected);
    }

    private UpdateScriptService CreateService()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Sportarr:DataPath"] = _dataPath })
            .Build();
        var configService = new ConfigService(configuration, NullLogger<ConfigService>.Instance);

        var handler = new StubHandler(() =>
        {
            _githubCalls++;
            return _github();
        });
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(handler, disposeHandler: false));

        var updateCheck = new UpdateCheckService(
            factory.Object, _time, NullLogger<UpdateCheckService>.Instance,
            new RunningVersion("4.1.12", "4.1.12.1121", "4.1.12.1121+abc"));

        // The script check reads the answer of the last scheduled check.
        // Only the script check itself counts as GitHub calls below.
        updateCheck.RunScheduledCheckAsync().GetAwaiter().GetResult();
        _githubCalls = 0;

        return new UpdateScriptService(
            configService,
            updateCheck,
            _runner,
            _safety,
            configuration,
            _time,
            NullLogger<UpdateScriptService>.Instance);
    }

    // "automatic" is the new tick. A null writes none, as in an old file.
    // "mechanism" and "oldTick" write the keys of v4.1.12 and earlier.
    private void WriteConfig(
        bool? automatic,
        string mechanism,
        string scriptPath = "/scripts/update.sh",
        string lastVersion = "",
        bool lastSucceeded = false,
        bool? oldTick = null)
    {
        File.WriteAllText(Path.Combine(_dataPath, "config.xml"),
            "<Config><SettingsUpgradeLevel>999</SettingsUpgradeLevel>" +
            (automatic == null ? "" : $"<AutomaticUpdates>{automatic.Value.ToString().ToLowerInvariant()}</AutomaticUpdates>") +
            (oldTick == null ? "" : $"<UpdateAutomatically>{oldTick.Value.ToString().ToLowerInvariant()}</UpdateAutomatically>") +
            $"<UpdateMechanism>{mechanism}</UpdateMechanism>" +
            $"<UpdateScriptPath>{scriptPath}</UpdateScriptPath>" +
            $"<AutoUpdateLastVersion>{lastVersion}</AutoUpdateLastVersion>" +
            $"<AutoUpdateLastSucceeded>{lastSucceeded.ToString().ToLowerInvariant()}</AutoUpdateLastSucceeded>" +
            "</Config>");
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
    };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpResponseMessage> _respond;

        public StubHandler(Func<HttpResponseMessage> respond) => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_respond());
    }

    private sealed class FakeSafety : IUpdateSafety
    {
        public string? Busy { get; set; }
        public Task<string?> WhatIsBusyAsync(CancellationToken cancellationToken) => Task.FromResult(Busy);
        public Task<UpdateDatabaseBackup> BackUpDatabaseAsync(string note) => throw new NotSupportedException();
        public string? BackupProblem() => null;
    }

    private sealed class FakeRunner : IUpdateScriptRunner
    {
        public List<(string Path, IReadOnlyDictionary<string, string> Env)> Calls { get; } = new();
        public int ExitCode { get; set; }
        public UpdateScriptRunResult? Result { get; set; }

        public Task<UpdateScriptRunResult> RunAsync(
            string scriptPath, IReadOnlyDictionary<string, string> environment, CancellationToken cancellationToken)
        {
            Calls.Add((scriptPath, environment));
            return Task.FromResult(Result ?? new UpdateScriptRunResult(true, ExitCode));
        }
    }

    // Advance moves the wall clock and the monotonic counter together.
    private sealed class FakeTime : TimeProvider
    {
        private DateTimeOffset _now;
        private long _ticks = TimeSpan.FromDays(10).Ticks;

        public FakeTime(DateTimeOffset now) => _now = now;

        public void Advance(TimeSpan by)
        {
            _now += by;
            _ticks += by.Ticks;
        }

        public override DateTimeOffset GetUtcNow() => _now;
        public override long GetTimestamp() => _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    }
}
