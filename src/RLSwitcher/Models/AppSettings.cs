namespace RLSwitcher.Models;

/// <summary>User-tweakable settings, persisted to settings.json.</summary>
public sealed class AppSettings
{
    /// <summary>Full path to RocketLeague.exe. Auto-detected from Epic manifests.</summary>
    public string? RocketLeagueExePath { get; set; }

    /// <summary>Extra command-line args appended to every Rocket League launch.</summary>
    public string? ExtraLaunchArgs { get; set; }

    /// <summary>Dark or Light for the Fluent theme.</summary>
    public string Theme { get; set; } = "Dark";

    /// <summary>Set once the first-run onboarding wizard has completed.</summary>
    public bool OnboardingComplete { get; set; }

    /// <summary>Id of the account launched most recently, highlighted as active.</summary>
    public string? ActiveAccountId { get; set; }

    /// <summary>When the app last checked GitHub for a newer release.</summary>
    public DateTimeOffset? LastUpdateCheckUtc { get; set; }
}
