using System.Security.Cryptography;
using System.Text;
using Pitako.Modules.DataLifecycle.Application.Abstractions;

namespace Pitako.Modules.DataLifecycle.Infrastructure.Security;

/// <summary>
/// Cifragem autenticada AES-GCM (256 bits) + SHA-256 para snapshots de backup por estabelecimento.
/// </summary>
public sealed class BackupAesGcmEncryptionProvider : IBackupEncryptionProvider
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly byte[] _key;

    public BackupAesGcmEncryptionProvider(byte[] key256Bits)
    {
        ArgumentNullException.ThrowIfNull(key256Bits);
        if (key256Bits.Length != 32)
            throw new ArgumentException("Chave AES-GCM deve possuir 32 bytes.", nameof(key256Bits));

        _key = (byte[])key256Bits.Clone();
    }

    public static BackupAesGcmEncryptionProvider FromPassphrase(string passphrase)
    {
        if (string.IsNullOrWhiteSpace(passphrase))
            throw new ArgumentException("Passphrase obrigatória.", nameof(passphrase));

        return new BackupAesGcmEncryptionProvider(SHA256.HashData(Encoding.UTF8.GetBytes(passphrase.Trim())));
    }

    public string Protect(string plaintext)
    {
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[plainBytes.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plainBytes, cipher, tag);

        var combined = new byte[NonceSize + TagSize + cipher.Length];
        Buffer.BlockCopy(nonce, 0, combined, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, combined, NonceSize, TagSize);
        Buffer.BlockCopy(cipher, 0, combined, NonceSize + TagSize, cipher.Length);
        return $"v1:{Convert.ToBase64String(combined)}";
    }

    public string Unprotect(string ciphertext)
    {
        if (string.IsNullOrWhiteSpace(ciphertext) || !ciphertext.StartsWith("v1:", StringComparison.Ordinal))
            throw new CryptographicException("Formato de backup inválido.");

        var raw = Convert.FromBase64String(ciphertext[3..]);
        var nonce = raw.AsSpan(0, NonceSize);
        var tag = raw.AsSpan(NonceSize, TagSize);
        var cipher = raw.AsSpan(NonceSize + TagSize);
        var plain = new byte[cipher.Length];

        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, cipher, tag, plain);
        return Encoding.UTF8.GetString(plain);
    }

    public string ComputeSha256(string plaintext) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(plaintext))).ToLowerInvariant();
}
