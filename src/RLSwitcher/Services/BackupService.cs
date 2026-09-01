using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RLSwitcher.Models;

namespace RLSwitcher.Services;

/// <summary>The decrypted contents of a backup file: account metadata plus their refresh tokens.</summary>
public sealed record BackupPayload(int Version, List<Account> Accounts, Dictionary<string, string> Secrets);

public sealed class BackupException : Exception
{
    public BackupException(string message) : base(message) { }
}

/// <summary>
/// Encrypted, portable account backup. This is separate from the vault's own
/// master-password portability: a backup is a single self-contained file you
/// choose a password for, hand to another PC, and import. It carries both the
/// account list and the Epic refresh tokens, so importing it fully restores every
/// account regardless of the target machine's vault mode.
///
/// File layout: magic "RLSB" + version byte + [salt(16)][nonce(12)][tag(16)][ciphertext],
/// where the plaintext is the JSON <see cref="BackupPayload"/>. AES-256-GCM under a
/// PBKDF2-SHA256 key, same construction the vault uses.
/// </summary>
public static class BackupService
{
    public const string FileExtension = ".rlbackup";

    private static readonly byte[] Magic = "RLSB"u8.ToArray();
    private const byte FormatVersion = 1;
    private const int SaltLen = 16, NonceLen = 12, TagLen = 16, KeyLen = 32, Iterations = 200_000;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    /// <summary>Encrypts the given accounts and their tokens under <paramref name="password"/> and writes the file.</summary>
    public static void Export(string path, string password, IEnumerable<Account> accounts, Vault vault)
    {
        if (string.IsNullOrEmpty(password)) throw new BackupException("A backup password is required.");

        var list = accounts.ToList();
        var secrets = new Dictionary<string, string>();
        foreach (var a in list)
        {
            var token = vault.Get(a.Id);
            if (!string.IsNullOrEmpty(token)) secrets[a.Id] = token;
        }

        var payload = new BackupPayload(FormatVersion, list, secrets);
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(payload, Json);
        var blob = Encrypt(plaintext, password);

        using var fs = File.Create(path);
        fs.Write(Magic);
        fs.WriteByte(FormatVersion);
        fs.Write(blob);
        Log.Info($"Exported backup of {list.Count} account(s) to {path}.");
    }

    /// <summary>Reads and decrypts a backup file. Throws <see cref="BackupException"/> on a bad file or wrong password.</summary>
    public static BackupPayload Import(string path, string password)
    {
        byte[] bytes;
        try { bytes = File.ReadAllBytes(path); }
        catch (Exception ex) { throw new BackupException("Could not read the backup file: " + ex.Message); }

        if (bytes.Length < Magic.Length + 1 || !bytes.AsSpan(0, Magic.Length).SequenceEqual(Magic))
            throw new BackupException("This doesn't look like an RLSwitcher backup file.");

        var version = bytes[Magic.Length];
        if (version != FormatVersion)
            throw new BackupException($"This backup was made by a different version (format {version}).");

        var blob = bytes.AsSpan(Magic.Length + 1).ToArray();
        byte[] plaintext;
        try { plaintext = Decrypt(blob, password); }
        catch (CryptographicException) { throw new BackupException("Wrong password, or the backup file is corrupt."); }
        catch (Exception ex) { throw new BackupException("Could not decrypt the backup: " + ex.Message); }

        BackupPayload? payload;
        try { payload = JsonSerializer.Deserialize<BackupPayload>(plaintext, Json); }
        catch (Exception ex) { throw new BackupException("The backup contents were unreadable: " + ex.Message); }

        if (payload is null) throw new BackupException("The backup was empty.");
        return payload;
    }

    // --- AES-GCM. Layout: [salt(16)][nonce(12)][tag(16)][ciphertext] ---

    private static byte[] Encrypt(byte[] plaintext, string password)
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

    private static byte[] Decrypt(byte[] blob, string password)
    {
        if (blob.Length < SaltLen + NonceLen + TagLen) throw new CryptographicException("Truncated blob.");
        var salt = blob.AsSpan(0, SaltLen).ToArray();
        var nonce = blob.AsSpan(SaltLen, NonceLen).ToArray();
        var tag = blob.AsSpan(SaltLen + NonceLen, TagLen).ToArray();
        var cipher = blob.AsSpan(SaltLen + NonceLen + TagLen).ToArray();
        var key = DeriveKey(password, salt);
        var plain = new byte[cipher.Length];

        using var gcm = new AesGcm(key, TagLen);
        gcm.Decrypt(nonce, cipher, tag, plain); // throws CryptographicException on wrong password / tamper
        return plain;
    }

    private static byte[] DeriveKey(string password, byte[] salt)
        => Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, KeyLen);
}
