using System.Diagnostics;
using RLSwitcher.Models;

namespace RLSwitcher.Services;

public sealed class LaunchException : Exception
{
    public LaunchException(string message) : base(message) { }
}

/// <summary>
/// Launches Rocket League as a chosen account: refresh the stored token, get a
/// one-shot exchange code, then start the game executable with Epic's auth args.
/// The launcher is never touched.
/// </summary>
public static class LaunchService
{
    /// <summary>
    /// Logs in from the browser authorizationCode and returns the tokens/identity.
    /// The caller creates the <see cref="Account"/> and stores the refresh token.
    /// </summary>
    public static Task<AuthResult> AddFromAuthCodeAsync(string authorizationCode)
        => EpicOAuth.LoginWithAuthCodeAsync(authorizationCode);

    /// <summary>
    /// Refreshes <paramref name="account"/>'s token, launches the game, and stores
    /// the rotated refresh token. Returns the started process.
    /// </summary>
    public static async Task<Process> LaunchAsync(Account account, AppSettings settings, Vault vault)
    {
        var exe = settings.RocketLeagueExePath;
        if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
            throw new LaunchException("RocketLeague.exe not set or missing. Fix the path in Settings.");

        var refreshToken = vault.Get(account.Id);
        if (string.IsNullOrEmpty(refreshToken))
            throw new LaunchException($"No saved login for '{account.DisplayName}'. Remove it and add it again.");

        AuthResult auth;
        try { auth = await EpicOAuth.RefreshAsync(refreshToken).ConfigureAwait(false); }
        catch (EpicAuthException ex)
        {
            throw new LaunchException(
                $"Login for '{account.DisplayName}' expired or was revoked ({ex.Message}). Re-add this account.");
        }

        // Rotate: Epic issues a new refresh token each time. Persist it immediately.
        vault.Set(account.Id, auth.RefreshToken);
        vault.Save();

        if (!string.IsNullOrEmpty(auth.AccountId)) account.EpicAccountId = auth.AccountId;
        if (!string.IsNullOrEmpty(auth.DisplayName)) account.EpicDisplayName = auth.DisplayName;

        var exchangeCode = await EpicOAuth.GetExchangeCodeAsync(auth.AccessToken).ConfigureAwait(false);

        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(exe) ?? "",
        };
        psi.ArgumentList.Add("-AUTH_LOGIN=unused");
        psi.ArgumentList.Add($"-AUTH_PASSWORD={exchangeCode}");
        psi.ArgumentList.Add("-AUTH_TYPE=exchangecode");
        psi.ArgumentList.Add($"-epicapp={RocketLeagueLocator.AppName}");
        psi.ArgumentList.Add("-epicenv=Prod");
        psi.ArgumentList.Add("-EpicPortal");
        psi.ArgumentList.Add("-epicusername=");
        psi.ArgumentList.Add($"-epicuserid={account.EpicAccountId}");

        // Global args first, then this account's own args (account overrides win by
        // coming later on the command line).
        foreach (var extra in SplitArgs(settings.ExtraLaunchArgs))
            psi.ArgumentList.Add(extra);
        foreach (var extra in SplitArgs(account.LaunchArgs))
            psi.ArgumentList.Add(extra);

        var proc = Process.Start(psi)
            ?? throw new LaunchException("Failed to start Rocket League.");

        account.LastUsedUtc = DateTimeOffset.UtcNow;
        account.LaunchCount++;
        return proc;
    }

    private static IEnumerable<string> SplitArgs(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) yield break;
        foreach (var part in raw.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            yield return part;
    }
}
