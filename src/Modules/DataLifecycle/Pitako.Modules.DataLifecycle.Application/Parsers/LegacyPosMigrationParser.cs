using System.Globalization;
using Pitako.Modules.DataLifecycle.Application.Abstractions;
using Pitako.Modules.DataLifecycle.Domain;
using Pitako.SharedKernel.Results;

namespace Pitako.Modules.DataLifecycle.Application.Parsers;

public sealed record MigrationRowValidationError(int LineNumber, string RawLine, string Reason);

public sealed record LegacyMigrationParseResult(
    int TotalRows,
    int ValidCount,
    int SkippedDuplicateCount,
    int FailedCount,
    IReadOnlyList<MigratedCatalogItemInput> ValidItems,
    IReadOnlyList<MigrationRowValidationError> Errors);

internal sealed record SourceColumnAliases(
    string[] SkuAliases,
    string[] CategoryAliases,
    string[] TitleAliases,
    string[] PriceAliases,
    string[] DescriptionAliases);

/// <summary>
/// Parser multi-formato para importação assistida de planilhas/CSVs exportados de
/// Consumer, Goomer, Saipos e layout genérico, com deduplicação de SKU e relatório por linha (Seção 31).
/// </summary>
public static class LegacyPosMigrationParser
{
    private static readonly IReadOnlyDictionary<DataMigrationSourceSystem, SourceColumnAliases> Schemas =
        new Dictionary<DataMigrationSourceSystem, SourceColumnAliases>
        {
            [DataMigrationSourceSystem.Consumer] = new(
                ["codigo_pdv", "codigo", "sku"],
                ["categoria", "grupo"],
                ["nome_produto", "produto", "nome"],
                ["preco", "valor", "preco_venda"],
                ["descricao", "observacao"]),

            [DataMigrationSourceSystem.Goomer] = new(
                ["sku", "external_code", "codigo"],
                ["category", "categoria"],
                ["title", "name", "produto"],
                ["price", "preco"],
                ["description", "descricao"]),

            [DataMigrationSourceSystem.Saipos] = new(
                ["codigo", "codigo_saipos", "sku"],
                ["grupo", "categoria"],
                ["descricao", "produto", "nome"],
                ["valor_venda", "valor", "preco"],
                ["detalhes", "observacao"]),

            [DataMigrationSourceSystem.GenericCsv] = new(
                ["sku", "codigo"],
                ["categoria", "category", "grupo"],
                ["nome", "title", "produto", "descricao"],
                ["preco", "price", "valor"],
                ["descricao", "description"])
        };

    public static Result<LegacyMigrationParseResult> ParseCatalogCsv(DataMigrationSourceSystem source, string rawCsv)
    {
        if (string.IsNullOrWhiteSpace(rawCsv))
            return Result.Failure<LegacyMigrationParseResult>(Error.Validation("Migration.EmptyInput", "Arquivo de importação está vazio."));

        var lines = rawCsv
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        if (lines.Count < 2)
            return Result.Failure<LegacyMigrationParseResult>(Error.Validation("Migration.NoDataRows", "Arquivo deve conter cabeçalho e ao menos uma linha de dados."));

        var delimiter = lines[0].Contains(';') ? ';' : ',';
        var headers = SplitLine(lines[0], delimiter).Select(h => h.Trim().ToLowerInvariant()).ToArray();
        var aliasMap = Schemas[source];

        var skuIdx = FindIndex(headers, aliasMap.SkuAliases);
        var catIdx = FindIndex(headers, aliasMap.CategoryAliases);
        var titleIdx = FindIndex(headers, aliasMap.TitleAliases);
        var priceIdx = FindIndex(headers, aliasMap.PriceAliases);
        var descIdx = FindIndex(headers, aliasMap.DescriptionAliases);

        if (titleIdx < 0 || priceIdx < 0)
            return Result.Failure<LegacyMigrationParseResult>(Error.Validation("Migration.InvalidHeaders", $"Cabeçalhos incompatíveis com formato {source}."));

        var seenSkus = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var validItems = new List<MigratedCatalogItemInput>();
        var errors = new List<MigrationRowValidationError>();
        var duplicates = 0;

        for (var i = 1; i < lines.Count; i++)
        {
            var cols = SplitLine(lines[i], delimiter);
            var title = GetCol(cols, titleIdx);
            var rawPrice = GetCol(cols, priceIdx);

            if (string.IsNullOrWhiteSpace(title))
            {
                errors.Add(new MigrationRowValidationError(i + 1, lines[i], "Nome/descrição do item ausente."));
                continue;
            }

            if (!TryParsePrice(rawPrice, out var price) || price <= 0m)
            {
                errors.Add(new MigrationRowValidationError(i + 1, lines[i], $"Preço inválido '{rawPrice}'."));
                continue;
            }

            var sku = skuIdx >= 0 && !string.IsNullOrWhiteSpace(GetCol(cols, skuIdx))
                ? GetCol(cols, skuIdx)
                : $"{source.ToString().ToUpperInvariant()}-{i}";

            if (!seenSkus.Add(sku))
            {
                duplicates++;
                continue;
            }

            var category = catIdx >= 0 && !string.IsNullOrWhiteSpace(GetCol(cols, catIdx))
                ? GetCol(cols, catIdx)
                : "Geral";

            var desc = descIdx >= 0 && !string.IsNullOrWhiteSpace(GetCol(cols, descIdx))
                ? GetCol(cols, descIdx)
                : null;

            validItems.Add(new MigratedCatalogItemInput(sku, category, title, price, desc));
        }

        var totalRows = lines.Count - 1;
        return Result.Success(new LegacyMigrationParseResult(
            totalRows,
            validItems.Count,
            duplicates,
            errors.Count,
            validItems,
            errors));
    }

    private static string[] SplitLine(string line, char delimiter) =>
        line.Split(delimiter).Select(c => c.Trim().Trim('"')).ToArray();

    private static int FindIndex(string[] headers, string[] aliases) =>
        Array.FindIndex(headers, h => aliases.Contains(h, StringComparer.OrdinalIgnoreCase));

    private static string GetCol(string[] cols, int idx) =>
        idx >= 0 && idx < cols.Length ? cols[idx].Trim() : string.Empty;

    private static bool TryParsePrice(string raw, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var clean = raw.Replace("R$", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
        if (clean.Contains(',') && !clean.Contains('.'))
            clean = clean.Replace(',', '.');
        else if (clean.Contains(',') && clean.Contains('.'))
            clean = clean.Replace(".", string.Empty, StringComparison.Ordinal).Replace(',', '.');

        return decimal.TryParse(clean, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }
}
