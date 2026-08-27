using System.Text.Json;
using RLSwitcher.Models;

namespace RLSwitcher.Services;

/// <summary>JSON persistence for accounts and settings.</summary>
public static class Store
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static List<Account> LoadAccounts()
    {
        if (!File.Exists(AppPaths.AccountsFile)) return new();
        try
        {
            var json = File.ReadAllText(AppPaths.AccountsFile);
            return JsonSerializer.Deserialize<List<Account>>(json) ?? new();
        }
        catch { return new(); }
    }

    public static void SaveAccounts(IEnumerable<Account> accounts)
    {
        AppPaths.EnsureCreated();
        File.WriteAllText(AppPaths.AccountsFile, JsonSerializer.Serialize(accounts, Options));
    }

    public static AppSettings LoadSettings()
    {
        if (!File.Exists(AppPaths.SettingsFile)) return new();
        try
        {
            var json = File.ReadAllText(AppPaths.SettingsFile);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new();
        }
        catch { return new(); }
    }

    public static void SaveSettings(AppSettings settings)
    {
        AppPaths.EnsureCreated();
        File.WriteAllText(AppPaths.SettingsFile, JsonSerializer.Serialize(settings, Options));
    }
}
