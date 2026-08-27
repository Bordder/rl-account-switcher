using System.Text.Json;

namespace RLSwitcher.Services;

/// <summary>One ranked playlist's standing.</summary>
public sealed record RankInfo(string Mode, string Tier, string Division, int Mmr)
{
    public string TierDisplay => string.IsNullOrEmpty(Tier) ? "Unranked" : Tier;
    public string MmrDisplay => Mmr > 0 ? Mmr.ToString() : "N/A";
    public bool HasDivision => !string.IsNullOrEmpty(Division);
}

/// <summary>Outcome of a stats lookup.</summary>
public sealed class StatsResult
{
    public bool Ok { get; init; }
    public string? Error { get; init; }
    public List<RankInfo> Ranks { get; init; } = new();
    public DateTimeOffset FetchedUtc { get; init; } = DateTimeOffset.UtcNow;

    public static StatsResult Fail(string error) => new() { Ok = false, Error = error };
}

/// <summary>
/// Parses the rocketleague.tracker.network (tracker.gg v2) profile JSON into
/// per-playlist ranks. Playlist ids: 10 = 1v1, 11 = 2v2, 13 = 3v3.
/// </summary>
public static class RlStats
{
    private static readonly (int Id, string Mode)[] Wanted =
        { (10, "1v1"), (11, "2v2"), (13, "3v3") };

    public static string ProfilePageUrl(string epicDisplayName)
        => $"https://rocketleague.tracker.network/rocket-league/profile/epic/{Uri.EscapeDataString(epicDisplayName)}/overview";

    /// <summary>True for the tracker.gg API response we want to read.</summary>
    public static bool IsProfileApi(string uri)
        => uri.Contains("/api/v2/rocket-league/standard/profile/epic/", StringComparison.OrdinalIgnoreCase);

    public static StatsResult Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("errors", out var errs) && errs.ValueKind == JsonValueKind.Array && errs.GetArrayLength() > 0)
                return StatsResult.Fail("No public tracker profile for this account.");

            if (!root.TryGetProperty("data", out var data) ||
                !data.TryGetProperty("segments", out var segments) ||
                segments.ValueKind != JsonValueKind.Array)
                return StatsResult.Fail("Could not read stats for this account.");

            // Always emit all three rows; fill from the matching segment when the
            // account has played that playlist, otherwise leave it as Unranked.
            var ranks = new List<RankInfo>();
            var sawProfile = false;
            foreach (var (id, mode) in Wanted)
            {
                RankInfo? found = null;
                foreach (var seg in segments.EnumerateArray())
                {
                    if (!seg.TryGetProperty("type", out var type) || type.GetString() != "playlist") continue;
                    if (!seg.TryGetProperty("attributes", out var attr) ||
                        !attr.TryGetProperty("playlistId", out var pid) ||
                        pid.ValueKind != JsonValueKind.Number || pid.GetInt32() != id) continue;

                    sawProfile = true;
                    var stats = seg.GetProperty("stats");
                    var tier = MetaName(stats, "tier");
                    var div = MetaName(stats, "division");
                    var mmr = stats.TryGetProperty("rating", out var rating) &&
                              rating.TryGetProperty("value", out var rv) && rv.ValueKind == JsonValueKind.Number
                              ? (int)Math.Round(rv.GetDouble()) : 0;

                    found = new RankInfo(mode, tier, div, mmr);
                    break;
                }
                ranks.Add(found ?? new RankInfo(mode, "", "", 0));
            }

            // If none of the three playlists existed at all, the profile is likely
            // empty/private rather than just missing 1v1.
            if (!sawProfile && segments.GetArrayLength() == 0)
                return StatsResult.Fail("No public tracker profile for this account.");

            return new StatsResult { Ok = true, Ranks = ranks };
        }
        catch (Exception ex)
        {
            return StatsResult.Fail("Couldn't parse stats: " + ex.Message);
        }
    }

    private static string MetaName(JsonElement stats, string prop)
    {
        if (stats.TryGetProperty(prop, out var el) &&
            el.TryGetProperty("metadata", out var meta) &&
            meta.TryGetProperty("name", out var name))
            return name.GetString() ?? "";
        return "";
    }
}
