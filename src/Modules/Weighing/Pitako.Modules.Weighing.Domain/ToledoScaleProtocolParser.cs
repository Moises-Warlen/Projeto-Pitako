using System.Globalization;
using System.Text;
using Pitako.SharedKernel.Results;

namespace Pitako.Modules.Weighing.Domain;

public sealed record ParsedScaleReading(
    decimal GrossWeightGrams,
    bool IsStable,
    string NormalizedPayload);

/// <summary>
/// Decodificador de frames dos protocolos seriais Toledo (Prt1 / Prix 3 P03).
/// Suporta quadros com delimitadores STX/ETX (&lt;STX&gt;SSSSS&lt;ETX&gt; ou formato P03 com prefixo de status)
/// e detecta explicitamente instabilidade (IIIII), sobrecarga (SSSSS) e subcarga (NNNNN).
/// </summary>
public static class ToledoScaleProtocolParser
{
    public static Result<ParsedScaleReading> ParseFrame(string rawFrame)
    {
        if (string.IsNullOrWhiteSpace(rawFrame))
            return Result.Failure<ParsedScaleReading>(Error.Validation("Scale.EmptyFrame", "Nenhum dado recebido da balança Toledo."));

        // Remove delimitadores ASCII STX (0x02), ETX (0x03), CR, LF e espaços extras.
        var sb = new StringBuilder(rawFrame.Length);
        foreach (var ch in rawFrame)
        {
            if (ch is '\x02' or '\x03' or '\r' or '\n')
                continue;
            sb.Append(ch);
        }

        var cleaned = sb.ToString().Trim();
        if (cleaned.Length == 0)
            return Result.Failure<ParsedScaleReading>(Error.Validation("Scale.EmptyFrame", "Quadro vazio após remover delimitadores."));

        if (cleaned.Contains("IIIII", StringComparison.OrdinalIgnoreCase) ||
            cleaned.StartsWith("U,", StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<ParsedScaleReading>(Error.Conflict("Scale.UnstableWeight", "Peso ainda instável na balança Toledo."));
        }

        if (cleaned.Contains("SSSSS", StringComparison.OrdinalIgnoreCase))
            return Result.Failure<ParsedScaleReading>(Error.Validation("Scale.Overload", "Sobrecarga detectada na balança."));

        if (cleaned.Contains("NNNNN", StringComparison.OrdinalIgnoreCase))
            return Result.Failure<ParsedScaleReading>(Error.Validation("Scale.Underload", "Subcarga ou prato ausente na balança."));

        // Formato 1: Toledo P03 simplificado -> "00.450" ou "01.250" (kg com ponto decimal) ou "S,00.450"
        var normalizedToken = cleaned.StartsWith("S,", StringComparison.OrdinalIgnoreCase)
            ? cleaned[2..].Trim()
            : cleaned;

        if (normalizedToken.Contains('.'))
        {
            var tokenNoUnit = normalizedToken.Replace("kg", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
            if (decimal.TryParse(tokenNoUnit, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var kgValue) && kgValue >= 0m)
            {
                var grams = decimal.Round(kgValue * 1000m, 3, MidpointRounding.ToEven);
                return Result.Success(new ParsedScaleReading(grams, true, cleaned));
            }
        }

        // Formato 2: Toledo Prt padrão de 5 ou 6 dígitos inteiros representando gramas (ex.: "00450" = 450g, "001250" = 1250g).
        if (normalizedToken.Length is >= 5 and <= 6 && normalizedToken.All(char.IsDigit))
        {
            var grams = decimal.Parse(normalizedToken, CultureInfo.InvariantCulture);
            return Result.Success(new ParsedScaleReading(grams, true, cleaned));
        }

        return Result.Failure<ParsedScaleReading>(Error.Validation(
            "Scale.InvalidFrame",
            $"Quadro '{cleaned}' incompatível com o protocolo Toledo."));
    }
}
