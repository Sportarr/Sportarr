namespace Sportarr.Update;

/// <summary>
/// Writes each step to logs/update.log in the data folder and to the console.
/// </summary>
public sealed class UpdateLog
{
    private readonly string? _path;
    private readonly object _lock = new();

    public UpdateLog(string? path)
    {
        _path = path;
        if (_path != null)
        {
            try { Directory.CreateDirectory(Path.GetDirectoryName(_path)!); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* Writes then go to the console only. */ }
        }
    }

    public List<string> Lines { get; } = new();

    public void Write(string message)
    {
        var line = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}Z] {message}";
        lock (_lock)
        {
            Lines.Add(line);
            Console.WriteLine(line);
            if (_path != null)
            {
                try { File.AppendAllText(_path, line + Environment.NewLine); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* The console still has the line. */ }
            }
        }
    }
}
