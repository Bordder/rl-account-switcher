using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;
using RLSwitcher.Models;
using RLSwitcher.Services;

namespace RLSwitcher;

public partial class SettingsWindow
{
    private readonly AppSettings _settings;
    private readonly VaultSession _vault;

    /// <summary>True if an import changed the account list, so the main window should reload.</summary>
    public bool AccountsChanged { get; private set; }

    public SettingsWindow(AppSettings settings, VaultSession vault)
    {
        InitializeComponent();
        _settings = settings;
        _vault = vault;

        Tb.AddHandler(UIElement.MouseLeftButtonDownEvent,
            new System.Windows.Input.MouseButtonEventHandler((_, e) =>
            {
                if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
                    try { DragMove(); } catch { }
            }), handledEventsToo: false);

        ExeBox.Text = settings.RocketLeagueExePath ?? "";
        ArgsBox.Text = settings.ExtraLaunchArgs ?? "";
        RefreshVaultStatus();
        LogBox.Text = Log.Tail(200);
    }

    // --- Diagnostics ---

    private void RefreshLog_Click(object sender, RoutedEventArgs e)
    {
        LogBox.Text = Log.Tail(200);
        LogBox.ScrollToEnd();
    }

    private void OpenLogFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(Log.Dir);
            Process.Start(new ProcessStartInfo(Log.Dir) { UseShellExecute = true });
        }
        catch (Exception ex) { Log.Warn("Could not open the log folder.", ex); }
    }

    // --- Backup / restore ---

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            EnsureVaultUnlocked();
            var accounts = Store.LoadAccounts();
            if (accounts.Count == 0)
            {
                System.Windows.MessageBox.Show(this, "There are no accounts to export yet.",
                    "Nothing to export", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var pw = InputDialog.Ask(this, "Backup password",
                "Choose a password for this backup file. You'll need it to import the backup later.\nWARNING: without it the backup can't be opened.", isPassword: true);
            if (string.IsNullOrEmpty(pw)) return;
            var confirm = InputDialog.Ask(this, "Confirm backup password", "Re-enter the backup password:", isPassword: true);
            if (confirm != pw)
            {
                System.Windows.MessageBox.Show(this, "Passwords did not match.", "Mismatch",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            AppPaths.EnsureCreated();
            var dlg = new SaveFileDialog
            {
                Filter = "RLSwitcher backup|*" + BackupService.FileExtension,
                InitialDirectory = AppPaths.BackupDir,
                FileName = $"RLSwitcher-backup-{DateTimeOffset.Now:yyyy-MM-dd}{BackupService.FileExtension}",
            };
            if (dlg.ShowDialog(this) != true) return;

            BackupService.Export(dlg.FileName, pw, accounts, _vault.Current!);
            System.Windows.MessageBox.Show(this,
                $"Exported {accounts.Count} account(s).\n\nKeep this file and its password safe: together they restore every login.",
                "Backup saved", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log.Error("Backup export failed.", ex);
            System.Windows.MessageBox.Show(this, ex.Message, "Export failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dlg = new OpenFileDialog { Filter = "RLSwitcher backup|*" + BackupService.FileExtension + "|All files|*.*" };
            if (dlg.ShowDialog(this) != true) return;

            var pw = InputDialog.Ask(this, "Import backup", "Enter the password for this backup file:", isPassword: true);
            if (pw is null) return;

            var payload = BackupService.Import(dlg.FileName, pw);

            EnsureVaultUnlocked();
            var vault = _vault.EnsureForWrite();
            var current = Store.LoadAccounts();
            var byEpicId = current
                .Where(a => !string.IsNullOrEmpty(a.EpicAccountId))
                .Select(a => a.EpicAccountId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            int added = 0, skipped = 0;
            foreach (var acc in payload.Accounts)
            {
                if (!string.IsNullOrEmpty(acc.EpicAccountId) && byEpicId.Contains(acc.EpicAccountId))
                {
                    skipped++;
                    continue;
                }
                if (payload.Secrets.TryGetValue(acc.Id, out var token) && !string.IsNullOrEmpty(token))
                    vault.Set(acc.Id, token);
                current.Add(acc);
                if (!string.IsNullOrEmpty(acc.EpicAccountId)) byEpicId.Add(acc.EpicAccountId);
                added++;
            }

            vault.Save();
            Store.SaveAccounts(current);
            AccountsChanged = added > 0;

            System.Windows.MessageBox.Show(this,
                $"Imported {added} account(s)." + (skipped > 0 ? $" Skipped {skipped} already present." : ""),
                "Import complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (BackupException ex)
        {
            System.Windows.MessageBox.Show(this, ex.Message, "Import failed",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            Log.Error("Backup import failed.", ex);
            System.Windows.MessageBox.Show(this, ex.Message, "Import failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void EnsureVaultUnlocked()
    {
        if (_vault.IsUnlocked) return;
        if (_vault.DiskMode == VaultMode.Password)
        {
            var pw = InputDialog.Ask(this, "Unlock vault", "Enter your master password:", isPassword: true);
            if (pw is null) throw new InvalidOperationException("Vault unlock cancelled.");
            _vault.Unlock(pw);
        }
        else
        {
            _vault.Unlock(null);
        }
    }

    private void RefreshVaultStatus()
    {
        var mode = _vault.DiskMode;
        VaultStatusText.Text = mode switch
        {
            VaultMode.None => "No vault yet. Logins you add are encrypted to this Windows user (DPAPI): convenient, but not portable.",
            VaultMode.Dpapi => "Vault: DPAPI (local). No password needed, but useless after a PC reset.",
            VaultMode.Password => "Vault: master password (portable). Survives a reset and moves between PCs.",
            _ => "",
        };
        MasterPwButton.Content = mode == VaultMode.Password
            ? "Remove master password" : "Enable master password";
    }

    private void BrowseExe_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "Rocket League launch exe|Launcher.exe;RocketLeague*.exe|Executables|*.exe" };
        if (dlg.ShowDialog(this) == true) ExeBox.Text = dlg.FileName;
    }

    private void DetectExe_Click(object sender, RoutedEventArgs e)
    {
        var found = RocketLeagueLocator.Find();
        if (found is not null) ExeBox.Text = found;
        else System.Windows.MessageBox.Show(this,
            "Could not find RocketLeague.exe automatically. Use Browse to point at it (usually Epic Games\\rocketleague\\Binaries\\Win64).",
            "Not found", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OpenData_Click(object sender, RoutedEventArgs e)
    {
        AppPaths.EnsureCreated();
        Process.Start(new ProcessStartInfo(AppPaths.Root) { UseShellExecute = true });
    }

    private void ClearEpic_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (Directory.Exists(AppPaths.WebViewDir))
                Directory.Delete(AppPaths.WebViewDir, recursive: true);
            System.Windows.MessageBox.Show(this,
                "Epic login cookies cleared. The next account you add will start logged out.",
                "Done", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this,
                "Could not clear the session (an in-app browser window may still be open):\n\n" + ex.Message,
                "Clear failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ToggleMasterPassword_Click(object sender, RoutedEventArgs e)
    {
        var mode = _vault.DiskMode;
        try
        {
            if (mode == VaultMode.Password)
            {
                var pw = InputDialog.Ask(this, "Remove master password",
                    "Enter current master password to switch back to local (DPAPI) storage:", isPassword: true);
                if (pw is null) return;
                var opened = Vault.Unlock(pw);           // verifies password
                var dpapi = Vault.CreateDpapi();
                foreach (var kv in opened.Secrets) dpapi.Set(kv.Key, kv.Value);
                dpapi.Save();
                _vault.Replace(dpapi);
            }
            else
            {
                var pw = InputDialog.Ask(this, "Enable master password",
                    "Choose a master password. You'll enter it once per session to use stored logins.\nWARNING: if you forget it, stored logins cannot be recovered.", isPassword: true);
                if (string.IsNullOrEmpty(pw)) return;
                var confirm = InputDialog.Ask(this, "Confirm master password",
                    "Re-enter the master password:", isPassword: true);
                if (confirm != pw)
                {
                    System.Windows.MessageBox.Show(this, "Passwords did not match.",
                        "Mismatch", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var pwVault = Vault.CreatePassword(pw);
                if (mode == VaultMode.Dpapi)
                    foreach (var kv in Vault.Unlock(null).Secrets) pwVault.Set(kv.Key, kv.Value);
                pwVault.Save();
                _vault.Replace(pwVault);
            }
            RefreshVaultStatus();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, ex.Message, "Vault error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _settings.RocketLeagueExePath = string.IsNullOrWhiteSpace(ExeBox.Text) ? null : ExeBox.Text.Trim();
        _settings.ExtraLaunchArgs = string.IsNullOrWhiteSpace(ArgsBox.Text) ? null : ArgsBox.Text.Trim();
        Store.SaveSettings(_settings);
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
