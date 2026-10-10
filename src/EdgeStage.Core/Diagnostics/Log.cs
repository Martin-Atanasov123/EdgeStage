using System.Text;

namespace EdgeStage.Core.Diagnostics;

public enum LogLevel
{
    Debug,
    Info,
    Warn,
    Error,
}

/// <summary>
/// Minimal local file log (one file per day, newest <see cref="RetainedFiles"/> kept). Nothing is
/// ever sent anywhere. Until <see cref="Initialize"/> is called, messages go to the debugger only,
/// so library code can log safely from tests.
/// </summary>
public static class Log
{
    public const int RetainedFiles = 5;
    private static readonly object Gate = new();
    private static string? s_directory;
    private static StreamWriter? s_writer;
    private static DateOnly s_writerDate;

    public static LogLevel MinimumLevel { get; set; } = LogLevel.Info;

    public static string? CurrentFile { get; private set; }

    public static void Initialize(string directory, int retain = RetainedFiles)
    {
        lock (Gate)
        {
            s_directory = directory;
            Directory.CreateDirectory(directory);
            PruneOldFiles(directory, retain);
            OpenWriter(DateOnly.FromDateTime(DateTime.Now));
        }
    }

    public static void Debug(string message) => Write(LogLevel.Debug, message);
    public static void Info(string message) => Write(LogLevel.Info, message);
    public static void Warn(string message) => Write(LogLevel.Warn, message);
    public static void Error(string message, Exception? ex = null) =>
        Write(LogLevel.Error, ex is null ? message : $"{message}: {ex}");

    public static void Write(LogLevel level, string message)
    {
        if (level < MinimumLevel)
            return;
        var now = DateTime.Now;
        string line = $"{now:yyyy-MM-dd HH:mm:ss.fff} [{level.ToString().ToUpperInvariant(),-5}] [{Environment.CurrentManagedThreadId,3}] {message}";
        System.Diagnostics.Debug.WriteLine(line);

        lock (Gate)
        {
            if (s_directory is null)
                return;
            try
            {
                var today = DateOnly.FromDateTime(now);
                if (s_writer is null || today != s_writerDate)
                    OpenWriter(today);
                s_writer!.WriteLine(line);
            }
            catch (IOException)
            {
                // Logging must never take the app down (disk full, file locked by a viewer, …).
            }
        }
    }

    public static void Shutdown()
    {
        lock (Gate)
        {
            s_writer?.Dispose();
            s_writer = null;
            s_directory = null;
        }
    }

    /// <summary>Deletes all but the newest <paramref name="retain"/> log files.</summary>
    public static void PruneOldFiles(string directory, int retain)
    {
        foreach (var file in new DirectoryInfo(directory).GetFiles("edgestage-*.log")
                     .OrderByDescending(f => f.Name, StringComparer.Ordinal)
                     .Skip(Math.Max(0, retain - 1))) // leave room for today's file
        {
            try { file.Delete(); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static void OpenWriter(DateOnly date)
    {
        s_writer?.Dispose();
        CurrentFile = Path.Combine(s_directory!, $"edgestage-{date:yyyyMMdd}.log");
        var stream = new FileStream(CurrentFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        s_writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
        s_writerDate = date;
    }
}
