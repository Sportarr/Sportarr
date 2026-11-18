using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Sportarr.Api.Services.MotorsportLive;

namespace Sportarr.Api.Tests.Services.MotorsportLive;

internal static class MotorsportFixtures
{
    public static string Path(string name) =>
        System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", "MotorsportLive", name);

    public static string Read(string name) => File.ReadAllText(Path(name));

    public static JsonNode Json(string name) => JsonNode.Parse(Read(name))!;

    public static byte[] Gzip(string text)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest))
            gzip.Write(Encoding.UTF8.GetBytes(text));
        return output.ToArray();
    }
}

internal sealed class ManualTime : TimeProvider
{
    public ManualTime(DateTimeOffset now) => Now = now;
    public DateTimeOffset Now { get; set; }
    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>Answers each request from a table of path and query to a body.</summary>
internal sealed class FixtureHandler : HttpMessageHandler
{
    private readonly Dictionary<string, Func<HttpResponseMessage>> _routes = new();
    public List<string> Requests { get; } = new();

    public FixtureHandler Json(string pathAndQuery, Func<string> body)
    {
        _routes[pathAndQuery] = () => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body(), Encoding.UTF8, "application/json")
        };
        return this;
    }

    public FixtureHandler Json(string pathAndQuery, string fixture) => Json(pathAndQuery, () => MotorsportFixtures.Read(fixture));

    public FixtureHandler Raw(string pathAndQuery, Func<HttpResponseMessage> response)
    {
        _routes[pathAndQuery] = response;
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var key = request.RequestUri!.PathAndQuery;
        Requests.Add(key);
        return Task.FromResult(_routes.TryGetValue(key, out var route)
            ? route()
            : new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}

internal sealed class HandlerFactory : IHttpClientFactory
{
    private readonly HttpMessageHandler _handler;
    public HandlerFactory(HttpMessageHandler handler) => _handler = handler;
    public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
}

internal static class MotorsportProviders
{
    public static MotorsportTimingFetcher Fetcher(HttpMessageHandler handler, TimeProvider time) =>
        new(new HandlerFactory(handler), NullLogger<MotorsportTimingFetcher>.Instance, time);

    public static MotorsportSessionQuery Query(MotorsportSeries series, string title, string anchorUtc, string? location = null) =>
        MotorsportSeriesMap.DescribeSession(series, title, DateTimeOffset.Parse(anchorUtc), location)
        ?? throw new InvalidOperationException($"'{title}' names no session");
}
