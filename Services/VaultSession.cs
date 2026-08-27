namespace RLSwitcher.Services;

/// <summary>
/// Holds the unlocked <see cref="Vault"/> for the app's lifetime and hides the
/// "which mode / is it unlocked" bookkeeping from the UI. Credential storage is
/// entirely optional; if nobody ever saves a password, no vault file is created.
/// </summary>
public sealed class VaultSession
{
    private Vault? _vault;

    /// <summary>The current vault mode on disk (None if no file).</summary>
    public VaultMode DiskMode => Vault.DetectMode();

    /// <summary>True once a vault is open and usable this session.</summary>
    public bool IsUnlocked => _vault is not null;

    /// <summary>
    /// Ensures a vault is available for writing a credential. For a fresh install
    /// this silently creates a DPAPI vault (no password, local only). If the vault
    /// on disk is password-protected it must already be unlocked.
    /// </summary>
    public Vault EnsureForWrite()
    {
        if (_vault is not null) return _vault;

        var mode = DiskMode;
        _vault = mode switch
        {
            VaultMode.None => Vault.CreateDpapi(),
            VaultMode.Dpapi => Vault.Unlock(null),
            _ => throw new InvalidOperationException(
                "The credential vault is password-protected and locked. Unlock it in Settings first."),
        };
        return _vault;
    }

    /// <summary>Unlocks an existing DPAPI vault, or a password vault with the password.</summary>
    public void Unlock(string? password)
    {
        var mode = DiskMode;
        if (mode == VaultMode.None) { _vault = Vault.CreateDpapi(); return; }
        _vault = Vault.Unlock(password);
    }

    public Vault? Current => _vault;

    /// <summary>Replaces the active vault (used when enabling/disabling the master password).</summary>
    public void Replace(Vault vault) => _vault = vault;
}
