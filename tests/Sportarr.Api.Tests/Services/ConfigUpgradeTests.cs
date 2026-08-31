using System.Xml.Serialization;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Models;
using Sportarr.Api.Services;
using Xunit;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// The disk scan default moved from hourly to twice a day so drives can
/// spin down, and the owner wanted every install on the new default, not
/// only fresh ones. The upgrade runs once per config file. The stamp is what
/// makes that safe: a user who goes back to a faster interval afterwards
/// must never be re-upgraded on the next start.
/// </summary>
public class ConfigUpgradeTests
{
    private static string WriteConfig(Config config)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"sportarr-cfg-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var serializer = new XmlSerializer(typeof(Config));
        using var stream = File.Create(Path.Combine(dir, "config.xml"));
        serializer.Serialize(stream, config);
        return dir;
    }

    private static ConfigService NewService(string dataPath)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Sportarr:DataPath"] = dataPath })
            .Build();
        return new ConfigService(configuration, NullLogger<ConfigService>.Instance);
    }

    [Fact]
    public async Task An_old_config_on_the_hourly_default_is_raised_once()
    {
        var dir = WriteConfig(new Config { DiskScanIntervalMinutes = 60, SettingsUpgradeLevel = 0 });
        try
        {
            var config = await NewService(dir).GetConfigAsync();

            config.DiskScanIntervalMinutes.Should().Be(720);
            config.SettingsUpgradeLevel.Should().BeGreaterThan(0);

            // The stamp must be on disk, not only in memory.
            var reread = await NewService(dir).GetConfigAsync();
            reread.DiskScanIntervalMinutes.Should().Be(720);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task A_value_the_user_sets_after_the_upgrade_stays()
    {
        var dir = WriteConfig(new Config { DiskScanIntervalMinutes = 60, SettingsUpgradeLevel = 1 });
        try
        {
            var config = await NewService(dir).GetConfigAsync();

            config.DiskScanIntervalMinutes.Should().Be(60, "an already-upgraded install chose this on purpose");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task A_slower_interval_is_left_alone_even_on_first_upgrade()
    {
        var dir = WriteConfig(new Config { DiskScanIntervalMinutes = 1440, SettingsUpgradeLevel = 0 });
        try
        {
            var config = await NewService(dir).GetConfigAsync();

            config.DiskScanIntervalMinutes.Should().Be(1440);
            config.SettingsUpgradeLevel.Should().BeGreaterThan(0);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task A_fresh_install_is_born_stamped()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"sportarr-cfg-{Guid.NewGuid():N}");
        try
        {
            var config = await NewService(dir).GetConfigAsync();

            config.DiskScanIntervalMinutes.Should().Be(720);
            config.SettingsUpgradeLevel.Should().BeGreaterThan(0);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
