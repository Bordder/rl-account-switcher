using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using RLSwitcher.Models;
using RLSwitcher.Services;

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

            var choice = System.Windows.MessageBox.Show(this,
                $"{info.Tag} is out. You're on {current.ToString(3)}.\n\nOpen the download page?",
                "Update available", MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (choice == MessageBoxResult.Yes && !string.IsNullOrEmpty(info.PageUrl))
                Process.Start(new ProcessStartInfo(info.PageUrl) { UseShellExecute = true });
        }
        catch { /* update check is best-effort */ }
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

    private void AddAccount_Click(object sender, RoutedEventArgs e)
    {
        var login = new LoginWindow { Owner = this };
        if (login.ShowDialog() != true || login.Result is null) return;

        var auth = login.Result;
        if (_vms.Any(v => v.Account.EpicAccountId == auth.AccountId))
        {
            Warn($"\"{auth.DisplayName}\" is already in the switcher.");
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
            Warn("Could not save the login securely: " + ex.Message);
            return;
        }

        _vms.Add(new AccountVM(account));
        Persist();
        Refresh();
    }

    // --- Play ---

    private async void Play_Click(object sender, RoutedEventArgs e)
    {
        var vm = VmFrom(sender);
        if (vm is null) return;

        if (string.IsNullOrEmpty(_settings.RocketLeagueExePath) || !File.Exists(_settings.RocketLeagueExePath))
        {
            Warn("Set the path to RocketLeague.exe in Settings first.");
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
        }
        catch (LaunchException ex) { Warn(ex.Message); }
        catch (Exception ex) { Warn("Launch failed: " + ex.Message); }
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
        try
        {
            Process.Start(new ProcessStartInfo(RlStats.ProfilePageUrl(vm.Account.EpicDisplayName))
            { UseShellExecute = true });
        }
        catch { /* best effort */ }
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

        var name = InputDialog.Ask(this, "Rename account", "Nickname (blank = Epic name):", vm.Account.Label);
        if (name is null) return;

        vm.Account.Label = name.Trim();
        vm.RaiseModelChanged();
        Persist();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        var vm = VmFrom(sender);
        if (vm is null) return;

        var ok = System.Windows.MessageBox.Show(this,
            $"Remove \"{vm.Account.DisplayName}\" from the switcher? This does not touch the Epic account itself.",
            "Remove account", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (ok != MessageBoxResult.Yes) return;

        try
        {
            EnsureVaultUnlocked();
            var v = _vault.Current;
            if (v is not null) { v.Remove(vm.Account.Id); v.Save(); }
        }
        catch { /* removing the local record is enough */ }

        _vms.Remove(vm);
        Persist();
        Refresh();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var win = new SettingsWindow(_settings, _vault) { Owner = this };
        win.ShowDialog();
        _settings = Store.LoadSettings();
        Refresh();
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

    private void Warn(string message)
        => System.Windows.MessageBox.Show(this, message, "Rocket League Account Switcher",
            MessageBoxButton.OK, MessageBoxImage.Information);
}
