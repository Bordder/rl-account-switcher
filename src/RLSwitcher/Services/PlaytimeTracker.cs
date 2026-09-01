using System.Diagnostics;
using RLSwitcher.Models;

namespace RLSwitcher.Services;

/// <summary>
/// Best-effort in-game time tracking. The process we start is Epic's Launcher.exe
/// EAC bootstrap, which exits almost immediately after spawning the real game, so
/// its own lifetime tells us nothing. Instead we watch for the RocketLeague game
/// process to appear and then exit, and count the span between.
///
/// Only one game runs at a time, so a new launch cancels any in-flight watch.
/// This is honest-but-approximate: if the game process is named differently on a
/// given install, or never appears, the launch simply isn't counted (and that's
/// logged) rather than recording a bogus duration.
/// </summary>
public static class PlaytimeTracker
{
    private static readonly string[] GameProcessNames = { "RocketLeague", "RocketLeague_EAC" };
    private static readonly TimeSpan Poll = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan AppearTimeout = TimeSpan.FromSeconds(120);

    private static CancellationTokenSource? _active;

    /// <summary>
    /// Starts watching for the launched game to appear and exit, then adds the
    /// elapsed seconds to <paramref name="account"/> and invokes <paramref name="onUpdated"/>
    /// (which should persist and refresh the UI). Returns immediately.
    /// </summary>
    public static void Track(Account account, Action onUpdated)
    {
        _active?.Cancel();
        var cts = _active = new CancellationTokenSource();
        _ = Task.Run(() => WatchAsync(account, onUpdated, cts.Token), cts.Token);
    }

    private static async Task WatchAsync(Account account, Action onUpdated, CancellationToken token)
    {
        try
        {
            if (!await WaitForRunningAsync(running: true, AppearTimeout, token))
            {
                Log.Warn($"Playtime: game process never appeared for '{account.DisplayName}'; launch not counted.");
                return;
            }

            var start = DateTimeOffset.UtcNow;
            await WaitForRunningAsync(running: false, Timeout.InfiniteTimeSpan, token);

            var seconds = (long)Math.Max(0, (DateTimeOffset.UtcNow - start).TotalSeconds);
            account.TotalPlaySeconds += seconds;
            onUpdated();
            Log.Info($"Playtime: '{account.DisplayName}' +{seconds}s (total {account.TotalPlaySeconds}s).");
        }
        catch (OperationCanceledException) { /* superseded by a newer launch */ }
        catch (Exception ex) { Log.Warn("Playtime tracking failed.", ex); }
    }

    private static async Task<bool> WaitForRunningAsync(bool running, TimeSpan timeout, CancellationToken token)
    {
        var deadline = timeout == Timeout.InfiniteTimeSpan ? DateTimeOffset.MaxValue : DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            token.ThrowIfCancellationRequested();
            if (IsGameRunning() == running) return true;
            await Task.Delay(Poll, token);
        }
        return false;
    }

    private static bool IsGameRunning()
    {
        foreach (var name in GameProcessNames)
        {
            var procs = Process.GetProcessesByName(name);
            try { if (procs.Length > 0) return true; }
            finally { foreach (var p in procs) p.Dispose(); }
        }
        return false;
    }
}
