namespace Sportarr.Update;

/// <summary>
/// Writes a small file in one step. The text goes to a temporary file
/// first, and a rename puts it in place. A reader then sees the old file,
/// no file, or the whole new file, but never a half-written one.
/// </summary>
public static class AtomicFile
{
    public static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temp, text);
            // On Windows a reader can hold the target open for a moment.
            PackageSwapper.Retry(() => File.Move(temp, path, overwrite: true));
        }
        catch
        {
            try { File.Delete(temp); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            throw;
        }
    }

    /// <summary>
    /// Returns the trimmed text, or null when the file is missing, empty or
    /// cannot be read now. A caller treats null as "not yet".
    /// </summary>
    public static string? Read(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;
            var text = File.ReadAllText(path).Trim();
            return text.Length == 0 ? null : text;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
