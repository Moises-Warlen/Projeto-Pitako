using System.Security.Cryptography;
using System.Text;
using Pitako.Modules.Security.Application.Abstractions;
using Pitako.SharedKernel.Results;

namespace Pitako.Modules.Security.Infrastructure.Crypto;

public sealed class DefaultPasswordStrengthValidator : IPasswordStrengthValidator
{
    public Result Validate(string plainPassword)
    {
        if (string.IsNullOrWhiteSpace(plainPassword) || plainPassword.Length < 8)
            return Result.Failure(Error.Validation("Password.TooShort", "A senha deve ter pelo menos 8 caracteres."));

        if (!plainPassword.Any(char.IsUpper) || !plainPassword.Any(char.IsLower) || !plainPassword.Any(char.IsDigit))
            return Result.Failure(Error.Validation("Password.Weak", "A senha deve conter letras maiúsculas, minúsculas e números."));

        return Result.Success();
    }
}

public sealed class CryptoTokenGenerator : ITokenGenerator
{
    public string GenerateUrlSafeToken(int byteLength = 32)
    {
        var bytes = RandomNumberGenerator.GetBytes(byteLength);
        return Base64UrlEncode(bytes);
    }

    public string ComputeSha256Hash(string rawToken)
    {
        if (string.IsNullOrEmpty(rawToken))
            return string.Empty;

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(hashBytes);
    }

    internal static string Base64UrlEncode(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    internal static byte[] Base64UrlDecode(string input)
    {
        var padded = input.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }

        return Convert.FromBase64String(padded);
    }
}
