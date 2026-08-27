using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RLSwitcher.Services;

public enum VaultMode : byte
{
    /// <summary>No vault file yet.</summary>
    None = 0,
    /// <summary>Windows DPAPI. Local only, no password prompt. Does NOT survive a PC reset.</summary>
    Dpapi = 1,
    /// <summary>AES-GCM with a master password. Portable across machines / survives a reset.</summary>
    Password = 2,
}

/// <summary>
/// Encrypted secret store: maps an account id to its Epic refresh token. Two modes:
///  - DPAPI: encrypted to this Windows user, unlocked automatically, useless after a reset.
///  - Password: AES-GCM under a master password, portable and reset-proof, but the
///    user types the password once per session to unlock it.
/// </summary>
public sealed class Vault
{
    private const int SaltLen = 16, NonceLen = 12, TagLen = 16, KeyLen = 32, Iterations = 200_000;

    private readonly Dictionary<string, string> _secrets;
    private readonly VaultMode _mode;
    private readonly string? _password; // in-memory only while unlocked (Password mode)

    private Vault(Dictionary<string, string> secrets, VaultMode mode, string? password)
    {
        _secrets = secrets;
        _mode = mode;
        _password = password;
    }

    public VaultMode Mode => _mode;
    public IReadOnlyDictionary<string, string> Secrets => _secrets;

    public string? Get(string accountId)
        => _secrets.TryGetValue(accountId, out var s) ? s : null;

    public void Set(string accountId, string secret) => _secrets[accountId] = secret;
    public void Remove(string accountId) => _secrets.Remove(accountId);

    // --- factory / load ---

    public static bool Exists() => File.Exists(AppPaths.VaultFile);

    /// <summary>Reads the leading mode byte without decrypting. None if no file.</summary>
    public static VaultMode DetectMode()
    {
        if (!File.Exists(AppPaths.VaultFile)) return VaultMode.None;
        try
        {
            using var fs = File.OpenRead(AppPaths.VaultFile);
            var b = fs.ReadByte();
            return b is (int)VaultMode.Dpapi or (int)VaultMode.Password ? (VaultMode)b : VaultMode.None;
        }
        catch { return VaultMode.None; }
    }

    public static Vault CreateDpapi() => new(new(), VaultMode.Dpapi, null);
    public static Vault CreatePassword(string password) => new(new(), VaultMode.Password, password);

    /// <summary>
    /// Opens the existing vault. For Password mode the caller must supply the
    /// master password; a wrong password throws <see cref="CryptographicException"/>.
    /// </summary>
    public static Vault Unlock(string? password)
    {
        var bytes = File.ReadAllBytes(AppPaths.VaultFile);
        var mode = (VaultMode)bytes[0];
        var payload = bytes.AsSpan(1).ToArray();

        byte[] json;
        if (mode == VaultMode.Dpapi)
        {
            json = System.Security.Cryptography.ProtectedData.Unprotect(
                payload, null, DataProtectionScope.CurrentUser);
        }
        else if (mode == VaultMode.Password)
        {
            if (string.IsNullOrEmpty(password))
                throw new InvalidOperationException("Master password required.");
            json = DecryptAesGcm(payload, password);
        }
        else throw new InvalidDataException("Unknown vault format.");

        var secrets = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
        return new Vault(secrets, mode, mode == VaultMode.Password ? password : null);
    }

    public void Save()
    {
        AppPaths.EnsureCreated();
        var json = JsonSerializer.SerializeToUtf8Bytes(_secrets);
        byte[] payload = _mode switch
        {
            VaultMode.Dpapi => System.Security.Cryptography.ProtectedData.Protect(
                json, null, DataProtectionScope.CurrentUser),
            VaultMode.Password => EncryptAesGcm(json, _password!),
            _ => throw new InvalidOperationException("Vault has no mode."),
        };

        using var fs = File.Create(AppPaths.VaultFile);
        fs.WriteByte((byte)_mode);
        fs.Write(payload);
    }

    // --- AES-GCM helpers (Password mode). Layout: [salt(16)][nonce(12)][tag(16)][ciphertext] ---

    private static byte[] EncryptAesGcm(byte[] plaintext, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltLen);
        var nonce = RandomNumberGenerator.GetBytes(NonceLen);
        var key = DeriveKey(password, salt);
        var cipher = new byte[plaintext.Length];
        var tag = new byte[TagLen];

        using (var gcm = new AesGcm(key, TagLen))
            gcm.Encrypt(nonce, plaintext, cipher, tag);

        var outBytes = new byte[SaltLen + NonceLen + TagLen + cipher.Length];
        Buffer.BlockCopy(salt, 0, outBytes, 0, SaltLen);
        Buffer.BlockCopy(nonce, 0, outBytes, SaltLen, NonceLen);
        Buffer.BlockCopy(tag, 0, outBytes, SaltLen + NonceLen, TagLen);
        Buffer.BlockCopy(cipher, 0, outBytes, SaltLen + NonceLen + TagLen, cipher.Length);
        return outBytes;
    }

    private static byte[] DecryptAesGcm(byte[] blob, string password)
    {
        var salt = blob.AsSpan(0, SaltLen).ToArray();
        var nonce = blob.AsSpan(SaltLen, NonceLen).ToArray();
        var tag = blob.AsSpan(SaltLen + NonceLen, TagLen).ToArray();
        var cipher = blob.AsSpan(SaltLen + NonceLen + TagLen).ToArray();
        var key = DeriveKey(password, salt);
        var plain = new byte[cipher.Length];

        using var gcm = new AesGcm(key, TagLen);
        gcm.Decrypt(nonce, cipher, tag, plain); // throws on wrong password / tampering
        return plain;
    }

    private static byte[] DeriveKey(string password, byte[] salt)
        => Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, KeyLen);
}
