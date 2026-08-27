using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using RLSwitcher.Services;

namespace RLSwitcher;

/// <summary>Username -> stable circle colour.</summary>
public sealed class AvatarBrushConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
        => new SolidColorBrush(Avatar.ColorFor(value as string));
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => Binding.DoNothing;
}

/// <summary>Username -> first-letter initial.</summary>
public sealed class InitialConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
        => Avatar.InitialFor(value as string);
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => Binding.DoNothing;
}

/// <summary>LastUsed timestamp -> "3h ago" / "never played".</summary>
public sealed class LastUsedConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
    {
        if (value is not DateTimeOffset used) return "never played";
        var span = DateTimeOffset.UtcNow - used;
        if (span < TimeSpan.FromMinutes(1)) return "playing now";
        if (span < TimeSpan.FromHours(1)) return $"{(int)span.TotalMinutes}m ago";
        if (span < TimeSpan.FromDays(1)) return $"{(int)span.TotalHours}h ago";
        return $"{(int)span.TotalDays}d ago";
    }
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => Binding.DoNothing;
}

internal static class Accent
{
    public static readonly Color Color = Color.FromRgb(0x4C, 0x8D, 0xFF);
}

/// <summary>IsActive -> accent border brush when active, else transparent.</summary>
public sealed class ActiveBorderBrushConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
        => value is true ? new SolidColorBrush(Accent.Color) : Brushes.Transparent;
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => Binding.DoNothing;
}

/// <summary>IsActive -> 2px border when active, else 0.</summary>
public sealed class ActiveThicknessConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
        => value is true ? new Thickness(2) : new Thickness(0);
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => Binding.DoNothing;
}

/// <summary>True -> Visible, false -> Collapsed.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
        => value is true ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => Binding.DoNothing;
}

/// <summary>IsExpanded -> chevron rotation (180 when open, else 0).</summary>
public sealed class BoolToAngleConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
        => value is true ? 180.0 : 0.0;
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => Binding.DoNothing;
}
