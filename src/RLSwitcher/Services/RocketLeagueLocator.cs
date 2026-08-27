using System.Text.Json;

namespace RLSwitcher.Services;

/// <summary>Finds RocketLeague.exe, preferring Epic's own install manifests.</summary>
public static class RocketLeagueLocator
{
    /// <summary>Rocket League's Epic app id.</summary>
    public const string AppName = "Sugar";

    private static string ManifestsDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "Epic", "EpicGamesLauncher", "Data", "Manifests");

    // Epic's install manifest points at Launcher.exe (its EAC bootstrap), which is
    // exactly what Epic runs with the auth args and gives full online play, so it's
    // the preferred fallback ahead of the raw game binaries.
    private static readonly string[] ProbeRoots =
    {
        @"C:\Program Files\Epic Games\rocketleague\Binaries\Win64",
        @"C:\Program Files (x86)\Epic Games\rocketleague\Binaries\Win64",
    };

    private static readonly string[] ProbeExes = { "Launcher.exe", "RocketLeague_EAC.exe", "RocketLeague.exe" };

    /// <summary>Best guess at RocketLeague.exe, or null if it can't be found.</summary>
    public static string? Find()
    {
        var fromManifest = FindViaManifests();
        if (fromManifest is not null) return fromManifest;

        foreach (var root in ProbeRoots)
            foreach (var exe in ProbeExes)
            {
                var p = Path.Combine(root, exe);
                if (File.Exists(p)) return p;
            }

        return null;
    }

    private static string? FindViaManifests()
    {
        if (!Directory.Exists(ManifestsDir)) return null;

        foreach (var file in Directory.EnumerateFiles(ManifestsDir, "*.item"))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                var root = doc.RootElement;

                var appName = Str(root, "MainGameAppName") ?? Str(root, "AppName");
                var display = Str(root, "DisplayName") ?? "";
                var isRl = string.Equals(appName, AppName, StringComparison.OrdinalIgnoreCase)
                           || display.Contains("Rocket League", StringComparison.OrdinalIgnoreCase);
                if (!isRl) continue;

                var install = Str(root, "InstallLocation");
                var exe = Str(root, "LaunchExecutable");
                if (install is null || exe is null) continue;

                var full = Path.GetFullPath(Path.Combine(install, exe.Replace('/', '\\')));
                if (File.Exists(full)) return full;
            }
            catch { /* skip malformed manifest */ }
        }
        return null;
    }

    private static string? Str(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) ? v.GetString() : null;
}
