using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using RLSwitcher.Models;
using RLSwitcher.Services;
using Wpf.Ui;

namespace RLSwitcher;

public partial class MainWindow
{
    private readonly ObservableCollection<AccountVM> _vms = new();
    private readonly VaultSession _vault = new();
    private StatsFetcher _stats = null!;
    private AppSettings _settings = new();

    public MainWindow()
    {
        InitializeComponent();
        AppPaths.EnsureCreated();
        var snackbar = new SnackbarService();
        snackbar.SetSnackbarPresenter(RootSnackbar);
        Notify.UseSnackbar(snackbar);
        _settings = Store.LoadSettings();
        foreach (var a in Store.LoadAccounts()) _vms.Add(new AccountVM(a));
        AccountsList.ItemsSource = _vms;
        _stats = new StatsFetcher(StatsWeb);

        AppTitleBar.AddHandler(UIElement.MouseLeftButtonDownEvent,
            new System.Windows.Input.MouseButtonEventHandler((_, e) =>
            {
                if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
                    try { DragMove(); } catch { }
            }), handledEventsToo: false);

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        if (!_settings.OnboardingComplete || string.IsNullOrEmpty(_settings.RocketLeagueExePath))
            RunOnboarding();
        Refresh();
        _ = CheckForUpdatesAsync();
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            var current = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version
                          ?? new Version(0, 0, 0, 0);
            var info = await UpdateChecker.CheckAsync(current);
            if (info is null) return;

            // If the release ships an MSI, install it in place: download, run the
            // installer, and close so it can replace the running files. The user
            // just reopens the app afterwards. No MSI (or install fails) falls back
            // to opening the download page.
            if (!string.IsNullOrEmpty(info.MsiUrl))
            {
                var go = await Notify.ConfirmAsync("Update available",
                    $"{info.Tag} is out. You're on {current.ToString(3)}.\n\n" +
                    "Download and install it now? RLSwitcher will close while the installer runs, then you can reopen it.",
                    confirmText: "Update now", cancelText: "Later");
                if (!go) return;

                try
                {
                    Notify.Toast("Updating", "Downloading the installer…");
                    if (await UpdateChecker.DownloadAndRunAsync(info))
                    {
                        System.Windows.Application.Current.Shutdown();
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn("Auto-update failed; falling back to the download page.", ex);
                    await Notify.InfoAsync("Update", "Couldn't install automatically: " + ex.Message +
                        "\n\nOpening the download page instead.");
                    OpenUrl(info.PageUrl);
                }
                return;
            }

            var open = await Notify.ConfirmAsync("Update available",
                $"{info.Tag} is out. You're on {current.ToString(3)}.\n\nOpen the download page?",
                confirmText: "Open page", cancelText: "Later");
            if (open) OpenUrl(info.PageUrl);
        }
        catch (Exception ex) { Log.Warn("Update check failed.", ex); }
    }

    private static void OpenUrl(string? url)
    {
        if (string.IsNullOrEmpty(url)) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Warn("Could not open URL: " + url, ex); }
    }

    private void RunOnboarding()
    {
        var win = new OnboardingWindow(_settings, _vault) { Owner = this };
        win.ShowDialog();
        foreach (var a in win.CreatedAccounts) _vms.Add(new AccountVM(a));
        if (win.CreatedAccounts.Count > 0) Persist();
        _settings = Store.LoadSettings();
    }

    private void Refresh()
    {
        foreach (var vm in _vms) vm.IsActive = vm.Account.Id == _settings.ActiveAccountId;

        EmptyPanel.Visibility = _vms.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        var vaultHint = _vault.DiskMode switch
        {
            VaultMode.Password => " · portable (master password)",
            VaultMode.Dpapi => " · local only",
            _ => "",
        };
        SubtitleText.Text = $"{_vms.Count} account{(_vms.Count == 1 ? "" : "s")}{vaultHint}";

        var pathOk = !string.IsNullOrEmpty(_settings.RocketLeagueExePath) && File.Exists(_settings.RocketLeagueExePath);
        PathWarning.IsOpen = !pathOk;
    }

    // --- Add ---

    private async void AddAccount_Click(object sender, RoutedEventArgs e)
    {
        var login = new LoginWindow { Owner = this };
        if (login.ShowDialog() != true || login.Result is null) return;

        var auth = login.Result;
        if (_vms.Any(v => v.Account.EpicAccountId == auth.AccountId))
        {
            await Notify.InfoAsync("Already added", $"\"{auth.DisplayName}\" is already in the switcher.");
            return;
        }

        var account = new Account
        {
            EpicDisplayName = auth.DisplayName,
            EpicAccountId = auth.AccountId,
            HasToken = true,
        };

        try
        {
            EnsureVaultUnlocked();
            var vault = _vault.EnsureForWrite();
            vault.Set(account.Id, auth.RefreshToken);
            vault.Save();
        }
        catch (Exception ex)
        {
            Log.Error("Could not save a new login to the vault.", ex);
            await Notify.InfoAsync("Couldn't save", "Could not save the login securely: " + ex.Message);
            return;
        }

        _vms.Add(new AccountVM(account));
        Persist();
        Refresh();
        Notify.Success("Account added", $"{account.DisplayName} is ready to play.");
    }

    // --- Play ---

    private async void Play_Click(object sender, RoutedEventArgs e)
    {
        var vm = VmFrom(sender);
        if (vm is null) return;

        if (string.IsNullOrEmpty(_settings.RocketLeagueExePath) || !File.Exists(_settings.RocketLeagueExePath))
        {
            await Notify.InfoAsync("Path needed", "Set the path to RocketLeague.exe in Settings first.");
            return;
        }

        try
        {
            EnsureVaultUnlocked();
            var vault = _vault.Current!;
            await LaunchService.LaunchAsync(vm.Account, _settings, vault);
            _settings.ActiveAccountId = vm.Account.Id;
            Store.SaveSettings(_settings);
            Persist();
            vm.RaiseModelChanged();
            Refresh();

            Notify.Success("Launching", $"Starting Rocket League as {vm.Account.DisplayName}.");

            // Count in-game time in the background; persist and refresh when the game exits.
            PlaytimeTracker.Track(vm.Account, () => Dispatcher.Invoke(() =>
            {
                Persist();
                vm.RaiseModelChanged();
            }));
        }
        catch (LaunchException ex) { await Notify.InfoAsync("Can't launch", ex.Message); }
        catch (Exception ex)
        {
            Log.Error("Launch failed.", ex);
            await Notify.InfoAsync("Launch failed", ex.Message);
        }
    }

    // --- Inline stats ---

    private void Card_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not AccountVM vm) return;
        vm.IsExpanded = !vm.IsExpanded;
        if (vm.IsExpanded && !vm.Loaded && !vm.IsLoading)
            _ = FetchStatsAsync(vm);
    }

    private void RefreshStats_Click(object sender, RoutedEventArgs e)
    {
        var vm = VmFrom(sender);
        if (vm is null || vm.IsLoading) return;
        vm.Loaded = false;
        vm.Ranks.Clear();
        _ = FetchStatsAsync(vm);
    }

    private void OpenProfile_Click(object sender, RoutedEventArgs e)
    {
        var vm = VmFrom(sender);
        if (vm is null) return;
        OpenUrl(RlStats.ProfilePageUrl(vm.Account.EpicDisplayName));
    }

    private async Task FetchStatsAsync(AccountVM vm)
    {
        vm.IsLoading = true;
        vm.Error = null;
        try
        {
            var result = await _stats.FetchAsync(vm.Account.EpicDisplayName);
            vm.Ranks.Clear();
            if (result.Ok)
            {
                foreach (var r in result.Ranks) vm.Ranks.Add(r);
                vm.Loaded = true;
            }
            else
            {
                vm.Error = result.Error;
            }
        }
        catch (Exception ex)
        {
            vm.Error = "Could not load stats: " + ex.Message;
        }
        finally
        {
            vm.IsLoading = false;
        }
    }

    // --- Edit / delete ---

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        var vm = VmFrom(sender);
        if (vm is null) return;

        if (!EditAccountDialog.Show(this, vm.Account, out var nickname, out var launchArgs)) return;

        vm.Account.Label = nickname;
        vm.Account.LaunchArgs = launchArgs;
        vm.RaiseModelChanged();
        Persist();
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        var vm = VmFrom(sender);
        if (vm is null) return;

        var ok = await Notify.ConfirmAsync("Remove account",
            $"Remove \"{vm.Account.DisplayName}\" from the switcher? This does not touch the Epic account itself.",
            confirmText: "Remove", cancelText: "Keep");
        if (!ok) return;

        try
        {
            EnsureVaultUnlocked();
            var v = _vault.Current;
            if (v is not null) { v.Remove(vm.Account.Id); v.Save(); }
        }
        catch (Exception ex) { Log.Warn($"Could not remove '{vm.Account.DisplayName}' token from vault (local record removed anyway).", ex); }

        _vms.Remove(vm);
        Persist();
        Refresh();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var win = new SettingsWindow(_settings, _vault) { Owner = this };
        win.ShowDialog();
        _settings = Store.LoadSettings();
        if (win.AccountsChanged) ReloadAccounts();
        Refresh();
    }

    private void ReloadAccounts()
    {
        _vms.Clear();
        foreach (var a in Store.LoadAccounts()) _vms.Add(new AccountVM(a));
    }

    private void Help_Click(object sender, RoutedEventArgs e) => RunOnboarding();

    // --- helpers ---

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

    private static AccountVM? VmFrom(object sender)
        => (sender as FrameworkElement)?.Tag as AccountVM;

    private void Persist() => Store.SaveAccounts(_vms.Select(v => v.Account));
}
