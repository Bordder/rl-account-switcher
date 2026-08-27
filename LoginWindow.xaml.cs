using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using RLSwitcher.Services;

namespace RLSwitcher;

/// <summary>
/// In-app Epic login using an isolated WebView2 profile (its own cookie jar,
/// separate from the user's real browser). Starts logged out so every add asks
/// for fresh credentials, captures the authorizationCode automatically, and
/// exchanges it for tokens. No copy-paste.
/// </summary>
public partial class LoginWindow
{
    private static readonly Regex CodeRegex =
        new("\"authorizationCode\"\\s*:\\s*\"([0-9a-fA-F]{32})\"", RegexOptions.Compiled);

    private bool _captured;

    /// <summary>Set on success: the tokens/identity from the login.</summary>
    public AuthResult? Result { get; private set; }

    public LoginWindow()
    {
        InitializeComponent();
        Tb.AddHandler(UIElement.MouseLeftButtonDownEvent,
            new System.Windows.Input.MouseButtonEventHandler((_, e) =>
            {
                if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
                    try { DragMove(); } catch { }
            }), handledEventsToo: false);

        Loaded += async (_, _) => await InitAsync();
    }

    private async Task InitAsync()
    {
        try
        {
            AppPaths.EnsureCreated();
            var env = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null, userDataFolder: AppPaths.WebViewDir);
            await Web.EnsureCoreWebView2Async(env);

            Web.CoreWebView2.Settings.AreDevToolsEnabled = false;
            Web.CoreWebView2.Settings.IsStatusBarEnabled = false;
            Web.CoreWebView2.NavigationCompleted += OnNavigationCompleted;

            // Fresh start: clear any lingering Epic session so a new account can log in.
            Web.CoreWebView2.CookieManager.DeleteAllCookies();
            Web.CoreWebView2.Navigate(EpicOAuth.LoginUrl);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this,
                "Could not start the in-app browser. Make sure the WebView2 runtime is installed.\n\n" + ex.Message,
                "WebView2 error", MessageBoxButton.OK, MessageBoxImage.Error);
            DialogResult = false;
            Close();
        }
    }

    private async void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (_captured) return;

        var source = Web.Source?.ToString() ?? "";
        if (!source.Contains("/id/api/redirect", StringComparison.OrdinalIgnoreCase)) return;

        // The redirect page's body is JSON holding the authorizationCode.
        string? code = await TryReadCodeAsync();
        if (code is null) return;

        _captured = true;
        SetBusy(true, "Signing in");
        try
        {
            Result = await EpicOAuth.LoginWithAuthCodeAsync(code);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            _captured = false;
            SetBusy(false, "Sign-in failed. Try again.");
            System.Windows.MessageBox.Show(this, ex.Message, "Login failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task<string?> TryReadCodeAsync()
    {
        try
        {
            var raw = await Web.CoreWebView2.ExecuteScriptAsync("document.body.innerText");
            // ExecuteScriptAsync returns a JSON-encoded string; unwrap it.
            var text = JsonSerializer.Deserialize<string>(raw) ?? "";
            var m = CodeRegex.Match(text);
            return m.Success ? m.Groups[1].Value : null;
        }
        catch { return null; }
    }

    private void LogOut_Click(object sender, RoutedEventArgs e)
    {
        if (Web.CoreWebView2 is null) return;
        _captured = false;
        Web.CoreWebView2.CookieManager.DeleteAllCookies();
        SetBusy(false, "Logged out. Sign in with another account.");
        Web.CoreWebView2.Navigate(EpicOAuth.LoginUrl);
    }

    private void SetBusy(bool busy, string status)
    {
        Busy.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Text = status;
    }
}
