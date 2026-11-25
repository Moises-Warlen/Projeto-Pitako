using System.Security.Cryptography;
using System.Text;
using Pitako.Modules.Marketplaces.Application.Abstractions;

namespace Pitako.Modules.Marketplaces.Infrastructure.Security;

/// <summary>
/// Proteção autenticada AES-GCM (256 bits) para armazenamento cifrado de credenciais de marketplaces.
/// </summary>
public sealed class AesGcmSecretProtector : ISecretProtector
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly byte[] _key;

    public AesGcmSecretProtector(byte[] key256Bits)
    {
        ArgumentNullException.ThrowIfNull(key256Bits);
        if (key256Bits.Length != 32)
            throw new ArgumentException("Chave AES-GCM deve possuir exatamente 32 bytes (256 bits).", nameof(key256Bits));

        _key = (byte[])key256Bits.Clone();
    }

    public static AesGcmSecretProtector FromPassphrase(string passphrase)
    {
        if (string.IsNullOrWhiteSpace(passphrase))
            throw new ArgumentException("Passphrase de proteção de segredos é obrigatória.", nameof(passphrase));

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(passphrase.Trim()));
        return new AesGcmSecretProtector(hash);
    }

    public string Protect(string plaintext)
    {
        if (string.IsNullOrWhiteSpace(plaintext))
            throw new ArgumentException("Texto para proteção não pode ser vazio.", nameof(plaintext));

        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipherBytes = new byte[plainBytes.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plainBytes, cipherBytes, tag);

        var combined = new byte[NonceSize + TagSize + cipherBytes.Length];
        Buffer.BlockCopy(nonce, 0, combined, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, combined, NonceSize, TagSize);
        Buffer.BlockCopy(cipherBytes, 0, combined, NonceSize + TagSize, cipherBytes.Length);

        return $"v1:{Convert.ToBase64String(combined)}";
    }

    public string Unprotect(string ciphertext)
    {
        if (string.IsNullOrWhiteSpace(ciphertext) || !ciphertext.StartsWith("v1:", StringComparison.Ordinal))
            throw new CryptographicException("Formato de segredo protegido inválido.");

        var raw = Convert.FromBase64String(ciphertext[3..]);
        if (raw.Length < NonceSize + TagSize)
            throw new CryptographicException("Payload AES-GCM truncado.");

        var nonce = raw.AsSpan(0, NonceSize);
        var tag = raw.AsSpan(NonceSize, TagSize);
        var cipher = raw.AsSpan(NonceSize + TagSize);
        var plain = new byte[cipher.Length];

        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, cipher, tag, plain);

        return Encoding.UTF8.GetString(plain);
    }
}
