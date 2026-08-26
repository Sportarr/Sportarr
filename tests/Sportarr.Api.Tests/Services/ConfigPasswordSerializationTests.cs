using System.Text;
using System.Xml.Serialization;
using FluentAssertions;
using Sportarr.Api.Models;
using Xunit;

namespace Sportarr.Api.Tests.Services;

/// <summary>
/// config.xml from before hashing existed can still hold a plaintext
/// password, and startup reads it once to hash it. It must therefore still be
/// readable, while never being written back.
///
/// Marking it XmlIgnore stopped both. The migration then found nothing to
/// hash, wrote an empty hash, and an upgraded install could not sign in.
/// </summary>
public class ConfigPasswordSerializationTests
{
    private static readonly XmlSerializer Serializer = new(typeof(Config));

    [Fact]
    public void A_plaintext_password_in_an_old_file_is_still_read()
    {
        const string legacy = """
            <?xml version="1.0" encoding="utf-8"?>
            <Config>
              <Username>admin</Username>
              <Password>legacy-secret</Password>
            </Config>
            """;

        using var reader = new StringReader(legacy);
        var config = (Config)Serializer.Deserialize(reader)!;

        config.Password.Should().Be("legacy-secret", "startup has to read it to hash it");
        config.Username.Should().Be("admin");
    }

    [Fact]
    public void A_plaintext_password_is_never_written_back()
    {
        var config = new Config { Username = "admin", Password = "should-not-persist" };

        var written = Write(config);

        written.Should().NotContain("should-not-persist");
        written.Should().NotContain("<Password>");
    }

    [Fact]
    public void The_hash_and_salt_are_still_written()
    {
        var config = new Config
        {
            Username = "admin",
            PasswordHash = "hashed-value",
            PasswordSalt = "salt-value"
        };

        var written = Write(config);

        written.Should().Contain("hashed-value");
        written.Should().Contain("salt-value");
    }

    private static string Write(Config config)
    {
        var buffer = new StringWriter();
        Serializer.Serialize(buffer, config);
        return buffer.ToString();
    }
}
