using System.Text.Json.Serialization;

namespace RLSwitcher.Models;

/// <summary>
/// One saved Epic account. Non-secret metadata only. The actual refresh token
/// lives in the encrypted vault, keyed by <see cref="Id"/>. The refresh token is
/// account-bound (not machine-bound), so a vault backup restores accounts on any PC.
/// </summary>
public sealed class Account
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>User-facing label. Defaults to the Epic display name, editable.</summary>
    public string Label { get; set; } = "";

    /// <summary>The account's Epic display name, as returned by the OAuth login.</summary>
    public string EpicDisplayName { get; set; } = "";

    /// <summary>Epic account id (GUID-ish). Passed to Rocket League on launch.</summary>
    public string EpicAccountId { get; set; } = "";

    /// <summary>True once a refresh token for this account has been stored in the vault.</summary>
    public bool HasToken { get; set; }

    public DateTimeOffset AddedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastUsedUtc { get; set; }

    /// <summary>What the avatar and lists show. Falls back to the Epic name.</summary>
    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Label) ? EpicDisplayName : Label;

    /// <summary>Set at runtime on the account that was launched most recently.</summary>
    [JsonIgnore]
    public bool IsActive { get; set; }
}
