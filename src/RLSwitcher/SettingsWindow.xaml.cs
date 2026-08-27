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
