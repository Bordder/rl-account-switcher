using System.Windows;
using RLSwitcher.Models;

namespace RLSwitcher;

/// <summary>
/// Edits the per-account bits: nickname and this account's own launch arguments,
/// alongside a readout of how often and how long it's been played. Returns the
/// edited values via <see cref="Nickname"/> and <see cref="LaunchArgs"/>.
/// </summary>
public partial class EditAccountDialog
{
    public string Nickname { get; private set; } = "";
    public string? LaunchArgs { get; private set; }

    private EditAccountDialog(Account account)
    {
        InitializeComponent();

        TitleBarControl.AddHandler(UIElement.MouseLeftButtonDownEvent,
            new System.Windows.Input.MouseButtonEventHandler((_, e) =>
            {
                if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
                    try { DragMove(); } catch { }
            }), handledEventsToo: false);

        EpicNameText.Text = "Epic account: " + account.EpicDisplayName;
        NicknameBox.Text = account.Label;
        ArgsBox.Text = account.LaunchArgs ?? "";

        LaunchCountText.Text = account.LaunchCount.ToString();
        PlaytimeText.Text = FormatPlaytime(account.TotalPlaySeconds);
        LastUsedText.Text = FormatLastUsed(account.LastUsedUtc);
    }

    /// <summary>Shows the dialog; returns true if the user saved.</summary>
    public static bool Show(Window owner, Account account, out string nickname, out string? launchArgs)
    {
        var dlg = new EditAccountDialog(account) { Owner = owner };
        var ok = dlg.ShowDialog() == true;
        nickname = dlg.Nickname;
        launchArgs = dlg.LaunchArgs;
        return ok;
    }

    private static string FormatPlaytime(long seconds)
    {
        if (seconds <= 0) return "—";
        var span = TimeSpan.FromSeconds(seconds);
        if (span.TotalHours >= 1) return $"{(int)span.TotalHours}h {span.Minutes}m";
        if (span.TotalMinutes >= 1) return $"{span.Minutes}m";
        return $"{span.Seconds}s";
    }

    private static string FormatLastUsed(DateTimeOffset? used)
    {
        if (used is null) return "never";
        var span = DateTimeOffset.UtcNow - used.Value;
        if (span < TimeSpan.FromMinutes(1)) return "just now";
        if (span < TimeSpan.FromHours(1)) return $"{(int)span.TotalMinutes}m ago";
        if (span < TimeSpan.FromDays(1)) return $"{(int)span.TotalHours}h ago";
        return $"{(int)span.TotalDays}d ago";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        Nickname = NicknameBox.Text.Trim();
        LaunchArgs = string.IsNullOrWhiteSpace(ArgsBox.Text) ? null : ArgsBox.Text.Trim();
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
