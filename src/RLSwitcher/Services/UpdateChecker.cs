using System.Net.Http;
using System.Text.Json;

namespace RLSwitcher.Services;

public sealed record UpdateInfo(Version Latest, string Tag, string PageUrl, string? MsiUrl);

/// <summary>
/// Checks the GitHub Releases API for a newer version than the running one.
/// Public repo, so no auth is needed. Any failure (offline, rate limit) returns
/// null and the app carries on quietly.
/// </summary>
public static class UpdateChecker
{
    private const string Owner = "Bordder";
    private const string Repo = "rl-account-switcher";

    private static readonly HttpClient Http = new();

    public static async Task<UpdateInfo?> CheckAsync(Version current)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get,
                $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest");
            req.Headers.UserAgent.ParseAdd("RLSwitcher-UpdateCheck");
            req.Headers.Accept.ParseAdd("application/vnd.github+json");

            using var resp = await Http.SendAsync(req).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;

            var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (root.TryGetProperty("draft", out var draft) && draft.GetBoolean()) return null;
            if (root.TryGetProperty("prerelease", out var pre) && pre.GetBoolean()) return null;

            var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
            if (!TryParseVersion(tag, out var latest)) return null;
            if (latest <= Normalize(current)) return null;

            var page = root.TryGetProperty("html_url", out var h) ? h.GetString() ?? "" : "";

            string? msi = null;
            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var a in assets.EnumerateArray())
                {
                    var name = a.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    if (name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
                    {
                        msi = a.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                        break;
                    }
                }
            }

            return new UpdateInfo(latest, tag, page, msi);
        }
        catch
        {
            return null;
        }
    }

    private static Version Normalize(Version v)
        => new(v.Major, v.Minor, Math.Max(v.Build, 0), 0);

    // Accepts tags like "v0.2.0", "0.2.0", or "0.2.0-beta".
    private static bool TryParseVersion(string tag, out Version version)
    {
        version = new Version(0, 0, 0, 0);
        var s = tag.TrimStart('v', 'V').Trim();
        if (s.Length == 0) return false;

        var parts = s.Split('.');
        if (!int.TryParse(parts[0], out var major)) return false;
        int minor = 0, patch = 0;
        if (parts.Length > 1 && !int.TryParse(parts[1], out minor)) return false;
        if (parts.Length > 2 && !int.TryParse(parts[2].Split('-', '+')[0], out patch)) return false;

        version = new Version(major, minor, patch, 0);
        return true;
    }
}
