using System.Reflection;
using RLSwitcher.Models;

namespace RLSwitcher.Services;

/// <summary>
/// One place for the "is there a newer release, and install it" flow, shared by
/// the startup check, the top-of-window banner, and the Settings button. Records
/// when the last check ran so the UI can show it.
/// </summary>
public static class Updater
{
    public static Version Current =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);

    public static string CurrentDisplay => Current.ToString(3);

    /// <summary>Checks for a newer release and stamps <see cref="AppSettings.LastUpdateCheckUtc"/>. Null = up to date or offline.</summary>
    public static async Task<UpdateInfo?> CheckAsync(AppSettings settings)
    {
        UpdateInfo? info = null;
        try { info = await UpdateChecker.CheckAsync(Current); }
        catch (Exception ex) { Log.Warn("Update check failed.", ex); }

        settings.LastUpdateCheckUtc = DateTimeOffset.UtcNow;
        Store.SaveSettings(settings);
        return info;
    }

    /// <summary>
    /// Offers to install <paramref name="info"/>. If it ships an MSI, downloads and
    /// runs the installer and returns true (the caller should shut the app down so
    /// the files can be replaced). Otherwise opens the download page. Returns false
    /// if nothing needs to happen.
    /// </summary>
    public static async Task<bool> PromptAndRunAsync(UpdateInfo info)
    {
        if (!string.IsNullOrEmpty(info.MsiUrl))
        {
            var go = await Notify.ConfirmAsync("Update available",
                $"{info.Tag} is out. You're on {CurrentDisplay}.\n\n" +
                "Download and install it now? RLSwitcher will close while the installer runs, then you can reopen it.",
                confirmText: "Update now", cancelText: "Later");
            if (!go) return false;

            try
            {
                Notify.Toast("Updating", "Downloading the installer…");
                return await UpdateChecker.DownloadAndRunAsync(info);
            }
            catch (Exception ex)
            {
                Log.Warn("Auto-update failed; falling back to the download page.", ex);
                await Notify.InfoAsync("Update",
                    "Couldn't install automatically: " + ex.Message + "\n\nOpening the download page instead.");
                OpenUrl(info.PageUrl);
                return false;
            }
        }

        var open = await Notify.ConfirmAsync("Update available",
            $"{info.Tag} is out. You're on {CurrentDisplay}.\n\nOpen the download page?",
            confirmText: "Open page", cancelText: "Later");
        if (open) OpenUrl(info.PageUrl);
        return false;
    }

    /// <summary>"2h ago" style text for the last check, or "never".</summary>
    public static string LastCheckedText(DateTimeOffset? last)
    {
        if (last is null) return "never checked";
        var span = DateTimeOffset.UtcNow - last.Value;
        if (span < TimeSpan.FromMinutes(1)) return "checked just now";
        if (span < TimeSpan.FromHours(1)) return $"checked {(int)span.TotalMinutes}m ago";
        if (span < TimeSpan.FromDays(1)) return $"checked {(int)span.TotalHours}h ago";
        return $"checked {(int)span.TotalDays}d ago";
    }

    private static void OpenUrl(string? url)
    {
        if (string.IsNullOrEmpty(url)) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Warn("Could not open URL: " + url, ex); }
    }
}
