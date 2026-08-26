using System.Text;

namespace Sportarr.Api.Helpers;

/// <summary>
/// Reads a remote response with a ceiling on how much is taken into memory.
///
/// Playlists, guides and indexer responses all come from servers the user
/// configured but nobody controls. Reading them whole with no limit let one
/// misbehaving or hostile source exhaust the process and take every search,
/// grab, import and recording down with it. The declared length is checked
/// first and the read is capped as it goes, because a server can omit that
/// header or lie about it.
/// </summary>
public static class BoundedHttpContent
{
    /// <summary>
    /// Ceiling for text payloads: playlists, guides and indexer responses. A
    /// very large real playlist is a few tens of megabytes.
    /// </summary>
    public const long DefaultMaxBytes = 128L * 1024 * 1024;

    /// <summary>
    /// Ceiling on how long the body read may run. The client timeout stops at
    /// the headers when the response streams, so without a deadline of its
    /// own a server that sent headers and then stalled held the read open for
    /// ever. RSS sync runs serially and searches hold a semaphore slot, so
    /// one such server wedged the whole pipeline until restart.
    /// </summary>
    public static readonly TimeSpan DefaultReadTimeout = TimeSpan.FromSeconds(100);

    public static async Task<byte[]> ReadAsByteArrayAsync(
        HttpContent content, string what, long maxBytes = DefaultMaxBytes,
        CancellationToken ct = default, TimeSpan? readTimeout = null)
    {
        var declared = content.Headers.ContentLength;
        if (declared > maxBytes)
        {
            throw new InvalidOperationException(
                $"{what} is too large ({declared} bytes); the ceiling is {maxBytes} bytes.");
        }

        var timeout = readTimeout ?? DefaultReadTimeout;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeout);

        try
        {
            await using var stream = await content.ReadAsStreamAsync(deadline.Token);
            using var buffer = new MemoryStream();

            var chunk = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(chunk, deadline.Token)) > 0)
            {
                if (buffer.Length + read > maxBytes)
                {
                    throw new InvalidOperationException(
                        $"{what} exceeded the {maxBytes} byte ceiling while downloading.");
                }

                buffer.Write(chunk, 0, read);
            }

            return buffer.ToArray();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new InvalidOperationException(
                $"{what} stalled while downloading; gave up after {timeout.TotalSeconds:F0} seconds.");
        }
    }

    public static async Task<string> ReadAsStringAsync(
        HttpContent content, string what, long maxBytes = DefaultMaxBytes,
        CancellationToken ct = default, TimeSpan? readTimeout = null)
    {
        var bytes = await ReadAsByteArrayAsync(content, what, maxBytes, ct, readTimeout);
        return ResolveEncoding(content).GetString(bytes);
    }

    /// <summary>
    /// The charset the response declares, falling back to UTF-8.
    ///
    /// Reading everything as UTF-8 is what the framework does not do: its own
    /// string read honours the declared charset. Indexer feeds still go out as
    /// ISO-8859-1 and similar, and decoding those as UTF-8 mangles accented
    /// release titles or breaks the XML parse outright.
    /// </summary>
    public static Encoding ResolveEncoding(HttpContent content)
    {
        var charSet = content.Headers.ContentType?.CharSet;
        if (string.IsNullOrWhiteSpace(charSet)) return Encoding.UTF8;

        // Some servers quote the value.
        charSet = charSet.Trim().Trim('"', '\'');
        if (charSet.Length == 0) return Encoding.UTF8;

        try
        {
            // Legacy code pages are not built in on .NET Core.
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(charSet);
        }
        catch (ArgumentException)
        {
            // A charset this runtime does not know. UTF-8 is the better guess
            // than refusing the response.
            return Encoding.UTF8;
        }
    }
}
