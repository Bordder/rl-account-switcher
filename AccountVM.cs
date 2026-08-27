using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using RLSwitcher.Models;
using RLSwitcher.Services;

namespace RLSwitcher;

/// <summary>
/// Wraps an <see cref="Account"/> for the list: adds the inline expand state and
/// the per-account stats (ranks) that load on first expand, with change
/// notification so cards update in place instead of being rebuilt.
/// </summary>
public sealed class AccountVM : INotifyPropertyChanged
{
    public Account Account { get; }

    public AccountVM(Account account) => Account = account;

    // Passthrough to the model (raised via RaiseModelChanged after edits).
    public string DisplayName => Account.DisplayName;
    public string EpicDisplayName => Account.EpicDisplayName;
    public DateTimeOffset? LastUsedUtc => Account.LastUsedUtc;

    public ObservableCollection<RankInfo> Ranks { get; } = new();

    private bool _isActive;
    public bool IsActive { get => _isActive; set => Set(ref _isActive, value); }

    private bool _isExpanded;
    public bool IsExpanded { get => _isExpanded; set => Set(ref _isExpanded, value); }

    private bool _isLoading;
    public bool IsLoading { get => _isLoading; set => Set(ref _isLoading, value); }

    /// <summary>True once ranks have been fetched, so re-expanding is instant.</summary>
    public bool Loaded { get; set; }

    private string? _error;
    public string? Error
    {
        get => _error;
        set { if (Set(ref _error, value)) OnPropertyChanged(nameof(HasError)); }
    }
    public bool HasError => !string.IsNullOrEmpty(_error);

    public void RaiseModelChanged()
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(EpicDisplayName));
        OnPropertyChanged(nameof(LastUsedUtc));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
