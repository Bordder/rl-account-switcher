using System.Windows.Media;

namespace RLSwitcher.Services;

/// <summary>
/// Epic accounts show a coloured circle with the first letter of the name.
/// We reproduce that: a stable colour derived from the username plus its initial.
/// </summary>
public static class Avatar
{
    // A calm, high-contrast set. Index is chosen from a stable hash of the name.
    private static readonly (byte r, byte g, byte b)[] Colors =
    {
        (0xE5, 0x39, 0x35), (0xD8, 0x1B, 0x60), (0x8E, 0x24, 0xAA), (0x5E, 0x35, 0xB1),
        (0x39, 0x49, 0xAB), (0x1E, 0x88, 0xE5), (0x00, 0x97, 0xA7), (0x00, 0x89, 0x7B),
        (0x43, 0xA0, 0x47), (0x7C, 0xB3, 0x42), (0xF4, 0x51, 0x1E), (0x6D, 0x4C, 0x41),
    };

    public static Color ColorFor(string? username)
    {
        var name = string.IsNullOrWhiteSpace(username) ? "?" : username.Trim();
        uint hash = 2166136261;
        foreach (var ch in name) { hash ^= ch; hash *= 16777619; } // FNV-1a, stable
        var (r, g, b) = Colors[hash % (uint)Colors.Length];
        return Color.FromRgb(r, g, b);
    }

    public static string InitialFor(string? username)
    {
        var name = username?.Trim();
        if (string.IsNullOrEmpty(name)) return "?";
        return char.ToUpperInvariant(name[0]).ToString();
    }
}
