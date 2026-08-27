using System.Windows;

namespace RLSwitcher;

public partial class InputDialog
{
    public string Value { get; private set; } = "";

    private InputDialog(string title, string prompt, string initial, bool isPassword)
    {
        InitializeComponent();
        Title = title;
        TitleBarControl.Title = title;
        PromptText.Text = prompt;

        TitleBarControl.AddHandler(UIElement.MouseLeftButtonDownEvent,
            new System.Windows.Input.MouseButtonEventHandler((_, e) =>
            {
                if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
                    try { DragMove(); } catch { }
            }), handledEventsToo: false);

        if (isPassword)
        {
            ValueBox.Visibility = Visibility.Collapsed;
            PasswordBoxControl.Visibility = Visibility.Visible;
            Loaded += (_, _) => PasswordBoxControl.Focus();
        }
        else
        {
            ValueBox.Text = initial;
            Loaded += (_, _) => { ValueBox.Focus(); ValueBox.SelectAll(); };
        }
    }

    /// <summary>Shows a prompt and returns the entered text, or null if cancelled.</summary>
    public static string? Ask(Window owner, string title, string prompt,
                              string initial = "", bool isPassword = false)
    {
        var dlg = new InputDialog(title, prompt, initial, isPassword) { Owner = owner };
        return dlg.ShowDialog() == true ? dlg.Value : null;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Value = PasswordBoxControl.Visibility == Visibility.Visible
            ? PasswordBoxControl.Password
            : ValueBox.Text.Trim();
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
