using System.Text;

namespace RLSwitcher.Services;

/// <summary>
/// Small append-only file logger at %APPDATA%\RLSwitcher\logs\app.log. Every
/// swallowed exception in the app routes here instead of vanishing, so a user
/// hitting a problem can open the log (Settings) and see what actually failed.
/// One file, trimmed when it grows past a cap. Never throws: logging must not
/// be able to break the thing it's logging.
/// </summary>
public static class Log
{
    private static readonly object Gate = new();
    private const long MaxBytes = 512 * 1024; // ~half a MB, then we trim the oldest half

    public static string Dir => Path.Combine(AppPaths.Root, "logs");
    public static string File => Path.Combine(Dir, "app.log");

    public static void Info(string message) => Write("INFO", message, null);
    public static void Warn(string message, Exception? ex = null) => Write("WARN", message, ex);
    public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    private static void Write(string level, string message, Exception? ex)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var line = new StringBuilder()
                .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss"))
                .Append(" [").Append(level).Append("] ")
                .Append(message);
            if (ex is not null) line.Append(" :: ").Append(ex.GetType().Name).Append(": ").Append(ex.Message);
            line.Append(Environment.NewLine);

            lock (Gate)
            {
                Trim();
                System.IO.File.AppendAllText(File, line.ToString(), Encoding.UTF8);
            }
        }
        catch { /* logging is best-effort; never let it throw */ }
    }

    /// <summary>Returns the last <paramref name="maxLines"/> log lines, newest last. Empty if none.</summary>
    public static string Tail(int maxLines = 200)
    {
        try
        {
            lock (Gate)
            {
                if (!System.IO.File.Exists(File)) return "";
                var lines = System.IO.File.ReadAllLines(File);
                var start = Math.Max(0, lines.Length - maxLines);
                return string.Join(Environment.NewLine, lines[start..]);
            }
        }
        catch { return ""; }
    }

    private static void Trim()
    {
        try
        {
            var info = new FileInfo(File);
            if (!info.Exists || info.Length < MaxBytes) return;
            var lines = System.IO.File.ReadAllLines(File);
            System.IO.File.WriteAllLines(File, lines[(lines.Length / 2)..], Encoding.UTF8);
        }
        catch { /* if trimming fails, leave the file as-is */ }
    }
}
