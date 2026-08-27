using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace RLSwitcher.Services;

/// <summary>Result of a successful token request.</summary>
public sealed record AuthResult(string AccessToken, string RefreshToken, string AccountId, string DisplayName);

public sealed class EpicAuthException : Exception
{
    public EpicAuthException(string message) : base(message) { }
}

/// <summary>
/// Talks to Epic's public account service using the same OAuth client the Epic
/// Games Launcher itself uses (client id 34a02cf8..., a well-known public value).
/// Flow: browser login -> 32-char authorizationCode -> refresh token (stored) ->
/// per-launch exchange code -> Rocket League boots authenticated.
/// </summary>
public static class EpicOAuth
{
    private const string ApiBase = "https://account-public-service-prod.ak.epicgames.com/account/api";

    // Base64 of "<clientId>:<clientSecret>" for the public launcher client.
    private const string BasicAuth =
        "MzRhMDJjZjhmNDQxNGUyOWIxNTkyMTg3NmRhMzZmOWE6ZGFhZmJjY2M3Mzc3NDUwMzlkZmZlNTNkOTRmYzc2Y2Y=";

    /// <summary>The page the user logs into; its final redirect shows the authorizationCode.</summary>
    public const string LoginUrl =
        "https://www.epicgames.com/id/login?redirectUrl=" +
        "https%3A//www.epicgames.com/id/api/redirect%3FclientId%3D34a02cf8f4414e29b15921876da36f9a%26responseType%3Dcode";

    private static readonly HttpClient Http = new();

    /// <summary>Exchanges the 32-char authorizationCode from the browser for tokens.</summary>
    public static Task<AuthResult> LoginWithAuthCodeAsync(string authorizationCode)
        => TokenRequestAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = authorizationCode.Trim(),
            ["token_type"] = "eg1",
        });

    /// <summary>Trades a stored refresh token for a fresh access token (and a rotated refresh token).</summary>
    public static Task<AuthResult> RefreshAsync(string refreshToken)
        => TokenRequestAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["token_type"] = "eg1",
        });

    /// <summary>Gets a one-shot exchange code used to launch the game authenticated.</summary>
    public static async Task<string> GetExchangeCodeAsync(string accessToken)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{ApiBase}/oauth/exchange");
        req.Headers.Authorization = new AuthenticationHeaderValue("bearer", accessToken);

        using var resp = await Http.SendAsync(req).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) throw new EpicAuthException(ErrorMessage(body, "Could not get an exchange code."));

        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("code").GetString()
            ?? throw new EpicAuthException("Epic returned no exchange code.");
    }

    private static async Task<AuthResult> TokenRequestAsync(Dictionary<string, string> form)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{ApiBase}/oauth/token");
        req.Headers.TryAddWithoutValidation("Authorization", $"basic {BasicAuth}");
        req.Content = new FormUrlEncodedContent(form);

        using var resp = await Http.SendAsync(req).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) throw new EpicAuthException(ErrorMessage(body, "Epic login failed."));

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        return new AuthResult(
            AccessToken: GetStr(root, "access_token"),
            RefreshToken: GetStr(root, "refresh_token"),
            AccountId: GetStr(root, "account_id"),
            DisplayName: root.TryGetProperty("displayName", out var dn) ? dn.GetString() ?? "" : "");
    }

    private static string GetStr(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) ? v.GetString() ?? "" : "";

    private static string ErrorMessage(string body, string fallback)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("errorMessage", out var m) && m.GetString() is { } s)
                return s;
        }
        catch { /* not JSON */ }
        return fallback;
    }
}
