using System.Text;
using System.Windows;
using RLSwitcher.Models;
using RLSwitcher.Services;

namespace RLSwitcher;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        AppPaths.EnsureCreated();

        var args = e.Args;

        // Headless entry points for Stream Deck / shortcuts / AutoHotkey:
        //   RLSwitcher.exe --account "Smurf2"   launch straight into an account
        //   RLSwitcher.exe --list               show the known account names
        //   RLSwitcher.exe --help               show CLI usage
        if (HasFlag(args, "--help", "-h", "/?")) { ShowCliHelp(); Shutdown(); return; }
        if (HasFlag(args, "--list")) { ShowAccountList(); Shutdown(); return; }

        var wanted = ValueAfter(args, "--account", "-a");
        if (wanted is not null)
        {
            // Don't let a transient dialog close the app before the launch finishes.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            RunHeadlessLaunch(wanted);
            Shutdown();
            return;
        }

        // Normal GUI start.
        new MainWindow().Show();
    }

    private void RunHeadlessLaunch(string wanted)
    {
        try
        {
            var settings = Store.LoadSettings();
            var accounts = Store.LoadAccounts();
            var account = Match(accounts, wanted);
            if (account is null)
            {
                Environment.ExitCode = 2;
                Fail($"No account matching \"{wanted}\". Run with --list to see the names.");
                return;
            }

            if (string.IsNullOrEmpty(settings.RocketLeagueExePath) || !File.Exists(settings.RocketLeagueExePath))
            {
                Environment.ExitCode = 3;
                Fail("Rocket League path isn't set yet. Open RLSwitcher normally and set it in Settings first.");
                return;
            }

            var vault = new VaultSession();
            if (vault.DiskMode == VaultMode.Password)
            {
                var pw = InputDialog.Ask(null!, "Unlock vault",
                    $"Enter your master password to launch \"{account.DisplayName}\":", isPassword: true);
                if (pw is null) { Environment.ExitCode = 4; return; }
                vault.Unlock(pw);
            }
            else
            {
                vault.Unlock(null);
            }

            LaunchService.LaunchAsync(account, settings, vault.Current!).GetAwaiter().GetResult();

            settings.ActiveAccountId = account.Id;
            Store.SaveSettings(settings);
            Store.SaveAccounts(accounts);
            Log.Info($"CLI launched '{account.DisplayName}'.");
        }
        catch (Exception ex)
        {
            Environment.ExitCode = 1;
            Log.Error("CLI launch failed.", ex);
            Fail("Launch failed: " + ex.Message);
        }
    }

    private static Account? Match(List<Account> accounts, string wanted)
    {
        wanted = wanted.Trim();
        return accounts.FirstOrDefault(a =>
                   Eq(a.Label, wanted) || Eq(a.EpicDisplayName, wanted) || Eq(a.DisplayName, wanted))
               ?? accounts.FirstOrDefault(a => Eq(a.Id, wanted));

        static bool Eq(string? a, string b) => !string.IsNullOrEmpty(a) &&
            string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    private static void ShowAccountList()
    {
        var accounts = Store.LoadAccounts();
        var sb = new StringBuilder();
        if (accounts.Count == 0) sb.Append("No accounts saved yet.");
        else foreach (var a in accounts) sb.AppendLine("• " + a.DisplayName);
        System.Windows.MessageBox.Show(sb.ToString().TrimEnd(), "RLSwitcher accounts",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static void ShowCliHelp()
        => System.Windows.MessageBox.Show(
            "Command line:\n\n" +
            "  RLSwitcher.exe --account \"Name\"   launch straight into an account\n" +
            "  RLSwitcher.exe --list             list account names\n" +
            "  RLSwitcher.exe --help             this help\n\n" +
            "The name matches a nickname or Epic display name (case-insensitive).\n" +
            "Handy for Stream Deck buttons, desktop shortcuts, or AutoHotkey.",
            "RLSwitcher", MessageBoxButton.OK, MessageBoxImage.Information);

    private static void Fail(string message)
        => System.Windows.MessageBox.Show(message, "RLSwitcher", MessageBoxButton.OK, MessageBoxImage.Warning);

    private static bool HasFlag(string[] args, params string[] flags)
        => args.Any(a => flags.Any(f => string.Equals(a, f, StringComparison.OrdinalIgnoreCase)));

    private static string? ValueAfter(string[] args, params string[] flags)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (flags.Any(f => string.Equals(args[i], f, StringComparison.OrdinalIgnoreCase)))
                return args[i + 1];
        return null;
    }
}
