using System.Windows;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace RLSwitcher.Services;

/// <summary>
/// App-wide user messaging, themed to match the Fluent window instead of the bare
/// Win32 <c>System.Windows.MessageBox</c>. Blocking questions and errors use a
/// themed dialog; transient "it worked" notices use a snackbar that slides in and
/// dismisses itself, so success no longer costs the user a click.
/// </summary>
public static class Notify
{
    private static ISnackbarService? _snackbar;

    /// <summary>Wired once by the main window, which owns the snackbar presenter.</summary>
    public static void UseSnackbar(ISnackbarService service) => _snackbar = service;

    /// <summary>Non-blocking success toast. Falls back to nothing if no presenter is set up.</summary>
    public static void Success(string title, string message)
        => _snackbar?.Show(title, message, ControlAppearance.Success,
            new SymbolIcon(SymbolRegular.CheckmarkCircle24), TimeSpan.FromSeconds(3));

    /// <summary>Non-blocking info toast.</summary>
    public static void Toast(string title, string message)
        => _snackbar?.Show(title, message, ControlAppearance.Info,
            new SymbolIcon(SymbolRegular.Info24), TimeSpan.FromSeconds(4));

    /// <summary>Themed info dialog with a single OK button.</summary>
    public static async Task InfoAsync(string title, string message)
    {
        var box = new Wpf.Ui.Controls.MessageBox
        {
            Title = title,
            Content = message,
            CloseButtonText = "OK",
        };
        await box.ShowDialogAsync();
    }

    /// <summary>Themed yes/no confirmation. Returns true only if the primary button is chosen.</summary>
    public static async Task<bool> ConfirmAsync(string title, string message,
        string confirmText = "Yes", string cancelText = "Cancel")
    {
        var box = new Wpf.Ui.Controls.MessageBox
        {
            Title = title,
            Content = message,
            PrimaryButtonText = confirmText,
            PrimaryButtonAppearance = ControlAppearance.Primary,
            CloseButtonText = cancelText,
        };
        return await box.ShowDialogAsync() == Wpf.Ui.Controls.MessageBoxResult.Primary;
    }
}
