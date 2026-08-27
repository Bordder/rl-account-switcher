namespace RLSwitcher.Services;

/// <summary>Where this app keeps its own data: %APPDATA%\RLSwitcher.</summary>
public static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RLSwitcher");

    public static string AccountsFile => Path.Combine(Root, "accounts.json");
    public static string SettingsFile => Path.Combine(Root, "settings.json");
    public static string VaultFile => Path.Combine(Root, "vault.bin");
    public static string BackupDir => Path.Combine(Root, "backups");

    /// <summary>Isolated WebView2 profile for the in-app Epic login (its own cookies).</summary>
    public static string WebViewDir => Path.Combine(Root, "webview");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(BackupDir);
    }
}
