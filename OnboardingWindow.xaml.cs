using System.Windows;
using RLSwitcher.Models;
using RLSwitcher.Services;

namespace RLSwitcher;

public partial class OnboardingWindow
{
    private readonly AppSettings _settings;
    private readonly VaultSession _vault;
    private int _step;

    /// <summary>Accounts created during onboarding, for the caller to persist.</summary>
    public List<Account> CreatedAccounts { get; } = new();

    public OnboardingWindow(AppSettings settings, VaultSession vault)
    {
        InitializeComponent();
        _settings = settings;
        _vault = vault;

        ExeBox.Text = RocketLeagueLocator.Find() ?? settings.RocketLeagueExePath ?? "";

        OnbTitleBar.AddHandler(UIElement.MouseLeftButtonDownEvent,
            new System.Windows.Input.MouseButtonEventHandler((_, e) =>
            {
                if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
                    try { DragMove(); } catch { }
            }), handledEventsToo: false);

        UpdateUi();
    }

    private FrameworkElement[] Panels => new FrameworkElement[]
        { StepWelcome, StepGame, StepAccount, StepDone };

    private void UpdateUi()
    {
        for (int i = 0; i < Panels.Length; i++)
            Panels[i].Visibility = i == _step ? Visibility.Visible : Visibility.Collapsed;

        BackButton.Visibility = _step is > 0 and < 3 ? Visibility.Visible : Visibility.Collapsed;
        NextButton.Content = _step == 3 ? "Finish" : "Next";

        if (_step == 1) ValidateGamePath(showEmpty: false);
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        { Filter = "Rocket League launch exe|Launcher.exe;RocketLeague*.exe|Executables|*.exe" };
        if (dlg.ShowDialog(this) == true) { ExeBox.Text = dlg.FileName; ValidateGamePath(showEmpty: true); }
    }

    private void Detect_Click(object sender, RoutedEventArgs e)
    {
        var found = RocketLeagueLocator.Find();
        if (found is not null) { ExeBox.Text = found; ValidateGamePath(showEmpty: true); }
        else ShowGame("Not found", "Couldn't detect it. Use Browse to point at RocketLeague.exe.", InfoBarSeverityWarning);
    }

    private void AddViaLogin_Click(object sender, RoutedEventArgs e)
    {
        var login = new LoginWindow { Owner = this };
        if (login.ShowDialog() != true || login.Result is null) return;

        var auth = login.Result;
        if (CreatedAccounts.Any(a => a.EpicAccountId == auth.AccountId))
        {
            ShowAccount("Already added", $"\"{auth.DisplayName}\" is already added.", InfoBarSeverityWarning);
            return;
        }

        SetBusy(true);
        try
        {
            var account = new Account
            {
                EpicDisplayName = auth.DisplayName,
                EpicAccountId = auth.AccountId,
                HasToken = true,
            };
            var vault = _vault.EnsureForWrite();
            vault.Set(account.Id, auth.RefreshToken);
            vault.Save();
            CreatedAccounts.Add(account);
            SetBusy(false);
            ShowAccount("Added", $"\"{account.DisplayName}\" is ready. Click Next.", InfoBarSeveritySuccess);
        }
        catch (Exception ex)
        {
            SetBusy(false);
            ShowAccount("Error", ex.Message, InfoBarSeverityError);
        }
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        switch (_step)
        {
            case 0:
                _step = 1; UpdateUi(); break;

            case 1:
                if (!ValidateGamePath(showEmpty: true)) return;
                _settings.RocketLeagueExePath = ExeBox.Text.Trim();
                _step = 2; UpdateUi(); break;

            case 2:
                _step = 3; UpdateUi(); break; // adding happens via the button; Next skips

            case 3:
                _settings.RocketLeagueExePath = ExeBox.Text.Trim();
                _settings.OnboardingComplete = true;
                Store.SaveSettings(_settings);
                DialogResult = true;
                Close();
                break;
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_step > 0) { _step--; UpdateUi(); }
    }

    private bool ValidateGamePath(bool showEmpty)
    {
        var path = ExeBox.Text.Trim();
        if (path.Length == 0)
        {
            if (showEmpty) ShowGame("Path needed", "Detect it or Browse to RocketLeague.exe.", InfoBarSeverityWarning);
            else GameStatus.IsOpen = false;
            return false;
        }
        if (!File.Exists(path))
        {
            ShowGame("File not found", "That path doesn't exist. Check it or use Browse.", InfoBarSeverityError);
            return false;
        }
        ShowGame("Found", "Rocket League is ready to launch.", InfoBarSeveritySuccess);
        return true;
    }

    private void SetBusy(bool busy)
    {
        Busy.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        NextButton.IsEnabled = !busy;
        BackButton.IsEnabled = !busy;
    }

    // ui:InfoBar severity via helpers keeps the switch statements terse.
    private const int InfoBarSeveritySuccess = 0, InfoBarSeverityWarning = 1, InfoBarSeverityError = 2;

    private void ShowGame(string title, string msg, int sev) => SetInfo(GameStatus, title, msg, sev);
    private void ShowAccount(string title, string msg, int sev) => SetInfo(AccountStatus, title, msg, sev);

    private static void SetInfo(Wpf.Ui.Controls.InfoBar bar, string title, string msg, int sev)
    {
        bar.Title = title;
        bar.Message = msg;
        bar.Severity = sev switch
        {
            InfoBarSeveritySuccess => Wpf.Ui.Controls.InfoBarSeverity.Success,
            InfoBarSeverityWarning => Wpf.Ui.Controls.InfoBarSeverity.Warning,
            _ => Wpf.Ui.Controls.InfoBarSeverity.Error,
        };
        bar.IsOpen = true;
    }
}
