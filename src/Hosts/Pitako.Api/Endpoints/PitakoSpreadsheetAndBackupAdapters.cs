using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Pitako.Modules.Catalog.Domain;
using Pitako.Modules.Inventory.Domain;

namespace Pitako.Api.Endpoints;

public sealed record ParsedVariationDto(string Name, decimal Price, decimal? MeasureAmount, string? MeasureUnit);
public sealed record ParsedIngredientDto(string Name, decimal Quantity, string UnitCode, decimal UnitCost);
public sealed record ParsedModifierGroupDto(string GroupName, List<string> Options);

public sealed record SpreadsheetPreviewRowDto(
    int RowNumber,
    string Sku,
    string CategoryName,
    string ProductName,
    string Description,
    decimal Price,
    decimal? PromotionalPrice,
    List<ParsedVariationDto> Variations,
    List<ParsedIngredientDto> Ingredients,
    List<ParsedModifierGroupDto> ModifierGroups,
    string? YieldText,
    string? TagsCsv,
    string? PhotoRef,
    string MatchStatus,
    Guid? ExistingProductId,
    decimal? ExistingPrice,
    bool HasExistingSheet,
    List<string> Errors,
    List<string> Warnings);

public sealed record BackupAdapterDescriptor(
    string AdapterId,
    string DisplayName,
    string Description,
    IReadOnlyList<string> SupportedExtensions);

public sealed record BackupFieldMappingItem(string SourceField, string PitakoTarget, int PopulatedCount);

public sealed record BackupPreviewResult(
    string AdapterId,
    string AdapterDisplayName,
    string DetectedFormat,
    int TotalRecordsFound,
    int CompatibleCount,
    int DuplicateCount,
    int IncompatibleCount,
    List<string> IncompatibleNotes,
    List<BackupFieldMappingItem> FieldMappings,
    List<SpreadsheetPreviewRowDto> PreviewRows);

public interface IBackupImportAdapter
{
    string AdapterId { get; }
    string DisplayName { get; }
    string Description { get; }
    IReadOnlyList<string> SupportedExtensions { get; }
    bool CanHandle(string fileName, string textContent);
    List<RawSpreadsheetRow> ExtractRows(string fileName, string textContent, out List<string> incompatibleNotes, out List<BackupFieldMappingItem> mappings);
}

public sealed record RawSpreadsheetRow(
    int RowNumber,
    string Sku,
    string CategoryName,
    string ProductName,
    string Description,
    string RawPrice,
    string RawPromoPrice,
    string RawVariations,
    string RawIngredients,
    string RawIngredientCosts,
    string RawUnits,
    string RawModifiers,
    string RawYield,
    string TagsCsv,
    string PhotoRef);

public static partial class PitakoSpreadsheetAndBackupAdapters
{
    private const int MaxSpreadsheetBytes = 10 * 1024 * 1024;
    private const int MaxZipUncompressedBytes = 18 * 1024 * 1024;
    private const int MaxImagesPerBatch = 80;

    private static readonly IReadOnlyList<IBackupImportAdapter> BackupAdapters =
    [
        new PitakoBackupJsonAdapter(),
        new StructuredMenuJsonAdapter(),
        new TabularPosBackupAdapter()
    ];

    public static IReadOnlyList<BackupAdapterDescriptor> ListImplementedBackupAdapters() =>
        BackupAdapters.Select(a => new BackupAdapterDescriptor(a.AdapterId, a.DisplayName, a.Description, a.SupportedExtensions)).ToList();

    public static byte[] BuildTemplateCsvBytes()
    {
        var sb = new StringBuilder();
        sb.AppendLine("SKU;Categoria;Produto;Descricao;Preco;PrecoPromocional;Variacoes;Ingredientes;CustosIngredientes;Unidades;Adicionais;PesoPorcao;Tags;CodigoFoto");
        sb.AppendLine("PROD-001;Clássicos da Casa;Tigela Tradicional;Creme artesanal servido bem gelado com frutas e crocantes;22.90;19.90;300ml:16.90|500ml:22.90|700ml:27.90;Polpa Base:250:0.035|Banana:60:0.008|Granola:30:0.022;0.035|0.008|0.022;g;Acompanhamentos:Granola,Paçoca,Leite em pó|Caldas:Mel,Leite Condensado;500 g;artesanal,destaque;tigela-tradicional.png");
        sb.AppendLine("PROD-002;Lanches Artesanais;Burger Duplo Cheddar;Dois blends 120g na brasa com cheddar cremoso e pão brioche;34.90;;Simples:28.90|Duplo:34.90;Pão Brioche:1:2.20|Blend Bovino:240:0.048|Queijo Cheddar:40:0.055;2.20|0.048|0.055;un|g|g;Extras:Bacon Crocante,Cebola Caramelizada,Ovo;380 g;artesanal;burger-duplo.png");
        sb.AppendLine("PROD-003;Bebidas;Suco Natural de Laranja;Espremido na hora sem adição de açúcar;12.00;;300ml:10.00|500ml:12.00;Laranja Pera:450:0.006;0.006;ml;Opções:Com Gelo,Sem Gelo;500 ml;vegano,sem-gluten;suco-laranja.png");
        var utf8 = Encoding.UTF8.GetBytes(sb.ToString());
        var bom = Encoding.UTF8.GetPreamble();
        var result = new byte[bom.Length + utf8.Length];
        Buffer.BlockCopy(bom, 0, result, 0, bom.Length);
        Buffer.BlockCopy(utf8, 0, result, bom.Length, utf8.Length);
        return result;
    }

    public static byte[] BuildTemplateExcelHtmlBytes()
    {
        var html = @"<!DOCTYPE html>
<html><head><meta charset=""utf-8""></head><body>
<table border=""1"">
  <thead>
    <tr style=""background:#1e293b;color:#fff;font-weight:bold;"">
      <th>SKU</th><th>Categoria</th><th>Produto</th><th>Descricao</th><th>Preco</th><th>PrecoPromocional</th>
      <th>Variacoes</th><th>Ingredientes</th><th>CustosIngredientes</th><th>Unidades</th><th>Adicionais</th><th>PesoPorcao</th><th>Tags</th><th>CodigoFoto</th>
    </tr>
  </thead>
  <tbody>
    <tr>
      <td>PROD-001</td><td>Clássicos da Casa</td><td>Tigela Tradicional</td><td>Creme artesanal servido gelado com frutas frescas</td><td>22.90</td><td>19.90</td>
      <td>300ml:16.90|500ml:22.90</td><td>Polpa Base:250:0.035|Banana:60:0.008</td><td>0.035|0.008</td><td>g|g</td>
      <td>Acompanhamentos:Granola,Paçoca,Leite em pó</td><td>500 g</td><td>artesanal,destaque</td><td>tigela-tradicional.png</td>
    </tr>
    <tr>
      <td>PROD-002</td><td>Lanches Artesanais</td><td>Burger Duplo Cheddar</td><td>Dois blends 120g na brasa com cheddar cremoso</td><td>34.90</td><td></td>
      <td>Simples:28.90|Duplo:34.90</td><td>Pão Brioche:1:2.20|Blend Bovino:240:0.048</td><td>2.20|0.048</td><td>un|g</td>
      <td>Extras:Bacon Crocante,Cebola Caramelizada</td><td>380 g</td><td>artesanal</td><td>burger-duplo.png</td>
    </tr>
  </tbody>
</table></body></html>";
        return Encoding.UTF8.GetBytes(html);
    }

    public static (bool Ok, string? Error, List<RawSpreadsheetRow> Rows, string DetectedFormat) ParseSpreadsheetPayload(
        string fileName,
        byte[] fileBytes)
    {
        if (fileBytes.Length == 0)
            return (false, "Arquivo vazio.", [], "Unknown");
        if (fileBytes.Length > MaxSpreadsheetBytes)
            return (false, "Arquivo excede o limite máximo de 10 MB.", [], "Unknown");

        var ext = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
        if (ext is not (".csv" or ".txt" or ".xls" or ".xlsx"))
            return (false, "Formato não suportado. Envie planilhas .csv, .xls ou .xlsx.", [], ext);

        if (ext == ".xlsx")
        {
            return ParseXlsxBytes(fileBytes);
        }

        var text = DecodeTextWithFallback(fileBytes);
        if (text.TrimStart().StartsWith("<", StringComparison.Ordinal))
        {
            if (text.Contains("<tr", StringComparison.OrdinalIgnoreCase) && text.Contains("<td", StringComparison.OrdinalIgnoreCase))
                return (true, null, ParseHtmlTable(text), "Excel HTML (.xls)");
            if (text.Contains("<Workbook", StringComparison.OrdinalIgnoreCase) || text.Contains("<Row", StringComparison.OrdinalIgnoreCase))
                return (true, null, ParseSpreadsheetMlXml(text), "Excel XML (.xls)");
        }

        return (true, null, ParseDelimitedText(text), "CSV");
    }

    public static List<SpreadsheetPreviewRowDto> EvaluateAgainstCatalog(
        IReadOnlyList<RawSpreadsheetRow> rawRows,
        IReadOnlyList<Product> existingProducts,
        IReadOnlyList<Category> existingCategories,
        IReadOnlyList<TechnicalSheet> existingSheets)
    {
        var categoryLookup = existingCategories.ToDictionary(c => c.Id, c => c.Name);
        var productBySku = existingProducts
            .Where(p => !string.IsNullOrWhiteSpace(p.Sku) && p.Status != CatalogItemStatus.Archived)
            .GroupBy(p => p.Sku!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var productByName = existingProducts
            .Where(p => p.Status != CatalogItemStatus.Archived)
            .GroupBy(p => NormalizeMatchKey(p.Name), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var sheetProductIds = existingSheets.Where(s => s.IsActive && s.ProductId.HasValue).Select(s => s.ProductId!.Value).ToHashSet();

        var seenKeysInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var preview = new List<SpreadsheetPreviewRowDto>();

        foreach (var raw in rawRows)
        {
            var errors = new List<string>();
            var warnings = new List<string>();

            var prodName = raw.ProductName.Trim();
            var catName = string.IsNullOrWhiteSpace(raw.CategoryName) ? "Geral" : raw.CategoryName.Trim();
            var sku = raw.Sku.Trim();

            if (string.IsNullOrWhiteSpace(prodName))
                errors.Add("Nome do produto é obrigatório.");

            var parsedPrice = ParsePriceDecimal(raw.RawPrice);
            var parsedPromo = ParseOptionalDecimal(raw.RawPromoPrice);
            var variations = ParseVariations(raw.RawVariations, errors);

            if (parsedPrice <= 0m && variations.Count > 0)
            {
                parsedPrice = variations.Min(v => v.Price);
            }

            if (parsedPrice <= 0m)
                errors.Add("Preço inválido ou menor/igual a zero.");

            if (parsedPromo.HasValue && (parsedPromo.Value <= 0m || parsedPromo.Value >= parsedPrice))
            {
                warnings.Add("Preço promocional ignorado (deve ser maior que zero e menor que o preço base).");
                parsedPromo = null;
            }

            var ingredients = ParseIngredients(raw.RawIngredients, raw.RawIngredientCosts, raw.RawUnits, warnings);
            var modifiers = ParseModifierGroups(raw.RawModifiers);

            var fileKey = !string.IsNullOrWhiteSpace(sku)
                ? $"sku:{sku}"
                : $"name:{NormalizeMatchKey(prodName)}|{NormalizeMatchKey(catName)}";

            var matchStatus = "New";
            if (!seenKeysInFile.Add(fileKey))
            {
                matchStatus = "DuplicateInFile";
                warnings.Add("Linha duplicada na própria planilha (será ignorada se não for revisada).");
            }

            Product? matchedExisting = null;
            if (!string.IsNullOrWhiteSpace(sku) && productBySku.TryGetValue(sku, out var bySku))
                matchedExisting = bySku;
            else if (!string.IsNullOrWhiteSpace(prodName) && productByName.TryGetValue(NormalizeMatchKey(prodName), out var byName))
                matchedExisting = byName;

            var hasSheet = matchedExisting is not null && sheetProductIds.Contains(matchedExisting.Id);
            if (matchedExisting is not null)
            {
                if (matchStatus != "DuplicateInFile")
                    matchStatus = "ExistingMatch";

                var existingCatName = categoryLookup.GetValueOrDefault(matchedExisting.CategoryId, "Geral");
                warnings.Add($"Já existe no catálogo ({existingCatName} | Preço atual: R$ {matchedExisting.BasePrice.Amount:0.00}{(hasSheet ? " | Com Ficha Técnica" : "")}).");
            }

            if (errors.Count > 0)
                matchStatus = "Error";

            preview.Add(new SpreadsheetPreviewRowDto(
                raw.RowNumber,
                sku,
                catName,
                prodName,
                raw.Description.Trim(),
                parsedPrice,
                parsedPromo,
                variations,
                ingredients,
                modifiers,
                string.IsNullOrWhiteSpace(raw.RawYield) ? null : raw.RawYield.Trim(),
                string.IsNullOrWhiteSpace(raw.TagsCsv) ? null : raw.TagsCsv.Trim(),
                string.IsNullOrWhiteSpace(raw.PhotoRef) ? null : raw.PhotoRef.Trim(),
                matchStatus,
                matchedExisting?.Id,
                matchedExisting?.BasePrice.Amount,
                hasSheet,
                errors,
                warnings));
        }

        return preview;
    }

    public static (bool Ok, string? Error, BackupPreviewResult? Result) AnalyzeBackupPayload(
        string fileName,
        byte[] bytes,
        IReadOnlyList<Product> existingProducts,
        IReadOnlyList<Category> existingCategories,
        IReadOnlyList<TechnicalSheet> existingSheets)
    {
        if (bytes.Length == 0)
            return (false, "Arquivo de backup vazio.", null);
        if (bytes.Length > 15 * 1024 * 1024)
            return (false, "Backup excede o limite de 15 MB.", null);

        var ext = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
        if (ext == ".xlsx")
        {
            var xlsRes = ParseXlsxBytes(bytes);
            if (!xlsRes.Ok) return (false, xlsRes.Error, null);
            var previewRows = EvaluateAgainstCatalog(xlsRes.Rows, existingProducts, existingCategories, existingSheets);
            var dupCount = previewRows.Count(r => r.MatchStatus is "ExistingMatch" or "DuplicateInFile");
            var errCount = previewRows.Count(r => r.MatchStatus == "Error");
            return (true, null, new BackupPreviewResult(
                "tabular-pos",
                "Planilha Tabular (Excel XLSX)",
                "Excel OpenXML (.xlsx)",
                previewRows.Count,
                previewRows.Count(r => r.MatchStatus != "Error"),
                dupCount,
                errCount,
                [],
                [
                    new("Planilha Coluna Produto/Nome", "Product.Name", previewRows.Count(r => !string.IsNullOrWhiteSpace(r.ProductName))),
                    new("Planilha Coluna Categoria", "Category.Name", previewRows.Count(r => !string.IsNullOrWhiteSpace(r.CategoryName))),
                    new("Planilha Coluna Preço", "Product.BasePrice", previewRows.Count(r => r.Price > 0)),
                    new("Planilha Coluna Ingredientes", "TechnicalSheet.Ingredients", previewRows.Count(r => r.Ingredients.Count > 0))
                ],
                previewRows));
        }

        var text = DecodeTextWithFallback(bytes);
        var adapter = BackupAdapters.FirstOrDefault(a => a.CanHandle(fileName ?? string.Empty, text));
        if (adapter is null)
        {
            return (false, "Formato de backup não reconhecido pelos importadores implementados (.json PITAKO/cardápio ou .csv/.xls/.xlsx tabular).", null);
        }

        var rows = adapter.ExtractRows(fileName ?? string.Empty, text, out var notes, out var mappings);
        if (rows.Count == 0)
            return (false, "Nenhum registro de produto/categoria compatível encontrado neste arquivo de backup.", null);

        var evaluated = EvaluateAgainstCatalog(rows, existingProducts, existingCategories, existingSheets);
        return (true, null, new BackupPreviewResult(
            adapter.AdapterId,
            adapter.DisplayName,
            $"{adapter.DisplayName} ({ext})",
            evaluated.Count,
            evaluated.Count(r => r.MatchStatus != "Error"),
            evaluated.Count(r => r.MatchStatus is "ExistingMatch" or "DuplicateInFile"),
            evaluated.Count(r => r.MatchStatus == "Error"),
            notes,
            mappings,
            evaluated));
    }

    public static (bool Ok, string? Error, List<(string FileName, string MatchKey, string Base64DataUrl)> Images) ExtractValidatedZipImages(byte[] zipBytes)
    {
        if (zipBytes.Length == 0 || zipBytes.Length > 15 * 1024 * 1024)
            return (false, "Pacote ZIP inválido ou acima de 15 MB.", []);

        var results = new List<(string FileName, string MatchKey, string Base64DataUrl)>();
        try
        {
            using var ms = new MemoryStream(zipBytes);
            using var zip = new ZipArchive(ms, ZipArchiveMode.Read, false);
            long totalUncompressed = 0;

            foreach (var entry in zip.Entries)
            {
                if (results.Count >= MaxImagesPerBatch) break;
                if (string.IsNullOrWhiteSpace(entry.Name)) continue;

                var ext = Path.GetExtension(entry.Name).ToLowerInvariant();
                if (ext is not (".png" or ".jpg" or ".jpeg" or ".webp")) continue;
                if (entry.Length <= 0 || entry.Length > 900_000) continue;

                totalUncompressed += entry.Length;
                if (totalUncompressed > MaxZipUncompressedBytes)
                    return (false, "Conteúdo descompactado excede limite seguro de 18 MB.", []);

                using var es = entry.Open();
                using var copy = new MemoryStream();
                es.CopyTo(copy);
                var imgBytes = copy.ToArray();
                var mime = DetectSafeRasterImageMime(imgBytes);
                if (mime is null) continue;

                var b64 = Convert.ToBase64String(imgBytes);
                var dataUrl = $"data:{mime};base64,{b64}";
                var matchKey = NormalizeMatchKey(Path.GetFileNameWithoutExtension(entry.Name));
                results.Add((entry.Name, matchKey, dataUrl));
            }
        }
        catch
        {
            return (false, "Arquivo ZIP corrompido ou formato não suportado.", []);
        }

        if (results.Count == 0)
            return (false, "Nenhuma imagem PNG, JPG ou WebP válida encontrada no pacote ZIP.", []);

        return (true, null, results);
    }

    public static string? DetectSafeRasterImageMime(byte[] bytes)
    {
        if (bytes.Length < 12) return null;
        // PNG: 89 50 4E 47
        if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
            return "image/png";
        // JPEG: FF D8 FF
        if (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return "image/jpeg";
        // WEBP: RIFF....WEBP
        if (bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46 &&
            bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
            return "image/webp";
        return null;
    }

    public static string NormalizeMatchKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var norm = value.Trim().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var ch in norm)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
        }
        return sb.ToString();
    }

    private static (bool Ok, string? Error, List<RawSpreadsheetRow> Rows, string DetectedFormat) ParseXlsxBytes(byte[] bytes)
    {
        try
        {
            using var ms = new MemoryStream(bytes);
            using var zip = new ZipArchive(ms, ZipArchiveMode.Read, false);

            var sharedStrings = new List<string>();
            var ssEntry = zip.GetEntry("xl/sharedStrings.xml");
            if (ssEntry is not null)
            {
                if (ssEntry.Length > MaxZipUncompressedBytes)
                    return (false, "Planilha XLSX excede tamanho seguro.", [], ".xlsx");
                using var ssStream = ssEntry.Open();
                var ssDoc = XDocument.Load(ssStream);
                foreach (var si in ssDoc.Descendants().Where(e => e.Name.LocalName == "si"))
                {
                    sharedStrings.Add(string.Concat(si.Descendants().Where(t => t.Name.LocalName == "t").Select(t => t.Value)));
                }
            }

            var sheetEntry = zip.Entries.FirstOrDefault(e =>
                e.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase) &&
                e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
            if (sheetEntry is null)
                return (false, "Nenhuma aba de planilha encontrada dentro do arquivo .xlsx.", [], ".xlsx");
            if (sheetEntry.Length > MaxZipUncompressedBytes)
                return (false, "Planilha XLSX excede tamanho seguro.", [], ".xlsx");

            using var sStream = sheetEntry.Open();
            var sheetDoc = XDocument.Load(sStream);
            var table = new List<List<string>>();

            foreach (var rowEl in sheetDoc.Descendants().Where(e => e.Name.LocalName == "row"))
            {
                var rowCols = new List<string>();
                foreach (var cell in rowEl.Elements().Where(e => e.Name.LocalName == "c"))
                {
                    var rAttr = cell.Attribute("r")?.Value;
                    var colIndex = rAttr is not null ? ExcelColRefToZeroIndex(rAttr) : rowCols.Count;
                    while (rowCols.Count < colIndex) rowCols.Add(string.Empty);

                    var type = cell.Attribute("t")?.Value;
                    var val = cell.Elements().FirstOrDefault(e => e.Name.LocalName == "v")?.Value;
                    if (type == "s" && int.TryParse(val, out var sIdx) && sIdx >= 0 && sIdx < sharedStrings.Count)
                        rowCols.Add(sharedStrings[sIdx]);
                    else if (type == "inlineStr")
                        rowCols.Add(string.Concat(cell.Descendants().Where(e => e.Name.LocalName == "t").Select(e => e.Value)));
                    else
                        rowCols.Add(val ?? string.Empty);
                }
                if (rowCols.Any(c => !string.IsNullOrWhiteSpace(c)))
                    table.Add(rowCols);
            }

            return (true, null, MapGridToRawRows(table), "Excel OpenXML (.xlsx)");
        }
        catch
        {
            return (false, "Não foi possível ler o arquivo .xlsx. Verifique se está salvo no formato padrão Excel.", [], ".xlsx");
        }
    }

    private static int ExcelColRefToZeroIndex(string cellRef)
    {
        var sum = 0;
        foreach (var ch in cellRef)
        {
            if (!char.IsLetter(ch)) break;
            sum = sum * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
        }
        return Math.Max(0, sum - 1);
    }

    private static List<RawSpreadsheetRow> ParseHtmlTable(string html)
    {
        var grid = new List<List<string>>();
        var trMatches = TrRegex().Matches(html);
        foreach (Match tr in trMatches)
        {
            var row = new List<string>();
            foreach (Match td in TdThRegex().Matches(tr.Groups[1].Value))
            {
                var clean = WebUtility.HtmlDecode(HtmlTagsRegex().Replace(td.Groups[1].Value, " ")).Trim();
                row.Add(Regex.Replace(clean, @"\s+", " "));
            }
            if (row.Any(c => !string.IsNullOrWhiteSpace(c)))
                grid.Add(row);
        }
        return MapGridToRawRows(grid);
    }

    private static List<RawSpreadsheetRow> ParseSpreadsheetMlXml(string xml)
    {
        try
        {
            var doc = XDocument.Parse(xml);
            var grid = doc.Descendants().Where(e => e.Name.LocalName == "Row")
                .Select(r => r.Elements().Where(e => e.Name.LocalName == "Cell")
                    .Select(c => c.Descendants().FirstOrDefault(d => d.Name.LocalName == "Data")?.Value?.Trim() ?? string.Empty)
                    .ToList())
                .Where(row => row.Any(c => !string.IsNullOrWhiteSpace(c)))
                .ToList();
            return MapGridToRawRows(grid);
        }
        catch
        {
            return [];
        }
    }

    internal static List<RawSpreadsheetRow> ParseDelimitedText(string text)
    {
        var lines = text.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();
        if (lines.Count < 2) return [];

        var headerLine = lines[0];
        var delimiter = headerLine.Count(c => c == ';') >= headerLine.Count(c => c == ',')
            ? (headerLine.Contains(';') ? ';' : '\t')
            : ',';
        if (!headerLine.Contains(delimiter)) delimiter = ';';

        var grid = lines.Select(l => SplitCsvLine(l, delimiter)).ToList();
        return MapGridToRawRows(grid);
    }

    private static List<string> SplitCsvLine(string line, char delimiter)
    {
        var cols = new List<string>();
        var cur = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    cur.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (ch == delimiter && !inQuotes)
            {
                cols.Add(cur.ToString().Trim());
                cur.Clear();
            }
            else
            {
                cur.Append(ch);
            }
        }
        cols.Add(cur.ToString().Trim());
        return cols;
    }

    private static List<RawSpreadsheetRow> MapGridToRawRows(List<List<string>> grid)
    {
        if (grid.Count < 2) return [];
        var headerRowIdx = 0;
        for (var r = 0; r < Math.Min(grid.Count, 5); r++)
        {
            var joined = string.Join(" ", grid[r]).ToLowerInvariant();
            if (joined.Contains("produto") || joined.Contains("nome") || joined.Contains("title") || joined.Contains("preco") || joined.Contains("price"))
            {
                headerRowIdx = r;
                break;
            }
        }

        var headers = grid[headerRowIdx].Select(NormalizeMatchKey).ToArray();
        int FindCol(params string[] aliases)
        {
            foreach (var alias in aliases)
            {
                var idx = Array.FindIndex(headers, h => h == alias || h.Contains(alias, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0) return idx;
            }
            return -1;
        }

        var skuIdx = FindCol("sku", "codigo", "codigopdv", "codigosaipos", "externalcode");
        var catIdx = FindCol("categoria", "category", "grupo");
        var nameIdx = FindCol("produto", "nomeproduto", "nome", "title", "item", "name");
        var descIdx = FindCol("descricao", "description", "detalhes", "observacao");
        var priceIdx = FindCol("precopromocional") == 0 ? -1 : FindCol("preco", "price", "valorvenda", "valor");
        var promoIdx = FindCol("precopromocional", "promocao", "promoprice");
        var varIdx = FindCol("variacoes", "tamanhos", "sizes", "variations");
        var ingIdx = FindCol("ingredientes", "fichatecnica", "composicao", "insumos");
        var costIdx = FindCol("custosingredientes", "custos", "custo");
        var unitIdx = FindCol("unidades", "unidade", "unit");
        var modIdx = FindCol("adicionais", "complementos", "opcionais", "modifiers");
        var yieldIdx = FindCol("pesoporcao", "rendimento", "peso", "volume", "yield");
        var tagIdx = FindCol("tags", "etiquetas");
        var photoIdx = FindCol("codigofoto", "foto", "imagem", "image", "imageurl", "arquivo");

        if (nameIdx < 0 && headers.Length >= 2) nameIdx = Math.Min(2, headers.Length - 1);
        if (priceIdx < 0)
        {
            priceIdx = Array.FindIndex(headers, h => (h == "preco" || h == "valor" || h == "price"));
        }

        string Col(List<string> row, int idx) => idx >= 0 && idx < row.Count ? row[idx].Trim() : string.Empty;

        var rows = new List<RawSpreadsheetRow>();
        for (var r = headerRowIdx + 1; r < grid.Count; r++)
        {
            var row = grid[r];
            if (!row.Any(c => !string.IsNullOrWhiteSpace(c))) continue;
            rows.Add(new RawSpreadsheetRow(
                r + 1,
                Col(row, skuIdx),
                Col(row, catIdx),
                Col(row, nameIdx),
                Col(row, descIdx),
                Col(row, priceIdx),
                Col(row, promoIdx),
                Col(row, varIdx),
                Col(row, ingIdx),
                Col(row, costIdx),
                Col(row, unitIdx),
                Col(row, modIdx),
                Col(row, yieldIdx),
                Col(row, tagIdx),
                Col(row, photoIdx)));
        }
        return rows;
    }

    private static List<ParsedVariationDto> ParseVariations(string raw, List<string> errors)
    {
        var list = new List<ParsedVariationDto>();
        if (string.IsNullOrWhiteSpace(raw)) return list;

        foreach (var part in raw.Split(['|', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var colonIdx = part.LastIndexOf(':');
            if (colonIdx <= 0)
            {
                errors.Add($"Variação inválida '{part}' (use Nome:Preco, ex: 500ml:21.90).");
                continue;
            }
            var vName = part[..colonIdx].Trim();
            var vPrice = ParsePriceDecimal(part[(colonIdx + 1)..]);
            if (string.IsNullOrWhiteSpace(vName) || vPrice <= 0m)
            {
                errors.Add($"Variação inválida '{part}'.");
                continue;
            }

            decimal? measureAmount = null;
            string? measureUnit = null;
            var m = MeasureRegex().Match(vName);
            if (m.Success && decimal.TryParse(m.Groups[1].Value.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var num))
            {
                measureAmount = num;
                measureUnit = m.Groups[2].Value.ToLowerInvariant();
            }
            list.Add(new ParsedVariationDto(vName, vPrice, measureAmount, measureUnit));
        }
        return list;
    }

    private static List<ParsedIngredientDto> ParseIngredients(string rawIng, string rawCosts, string rawUnits, List<string> warnings)
    {
        var list = new List<ParsedIngredientDto>();
        if (string.IsNullOrWhiteSpace(rawIng)) return list;

        var costTokens = (rawCosts ?? string.Empty).Split(['|', ';'], StringSplitOptions.TrimEntries);
        var unitTokens = (rawUnits ?? string.Empty).Split(['|', ';'], StringSplitOptions.TrimEntries);
        var parts = rawIng.Split(['|', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        for (var i = 0; i < parts.Length; i++)
        {
            var segs = parts[i].Split(':', StringSplitOptions.TrimEntries);
            var name = segs[0];
            if (string.IsNullOrWhiteSpace(name)) continue;

            decimal qty = 100m;
            var unit = unitTokens.Length > i && !string.IsNullOrWhiteSpace(unitTokens[i])
                ? unitTokens[i].ToLowerInvariant()
                : unitTokens.Length == 1 && !string.IsNullOrWhiteSpace(unitTokens[0])
                    ? unitTokens[0].ToLowerInvariant()
                    : "g";
            decimal unitCost = costTokens.Length > i ? ParsePriceDecimal(costTokens[i]) : 0.02m;
            if (unitCost <= 0m) unitCost = 0.02m;

            if (segs.Length >= 2)
            {
                var qParsed = ParseOptionalDecimal(segs[1]);
                if (qParsed.HasValue && qParsed.Value > 0m) qty = qParsed.Value;
            }
            if (segs.Length >= 3)
            {
                if (decimal.TryParse(segs[2].Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var thirdNum) && thirdNum > 0m)
                    unitCost = thirdNum;
                else if (!string.IsNullOrWhiteSpace(segs[2]))
                    unit = segs[2].ToLowerInvariant();
            }
            if (segs.Length >= 4)
            {
                var cParsed = ParseOptionalDecimal(segs[3]);
                if (cParsed.HasValue && cParsed.Value > 0m) unitCost = cParsed.Value;
            }

            if (unit is not ("g" or "kg" or "ml" or "l" or "un" or "unidade" or "pacote"))
            {
                warnings.Add($"Unidade '{unit}' do ingrediente '{name}' convertida para 'g'.");
                unit = "g";
            }
            if (unit == "unidade") unit = "un";

            list.Add(new ParsedIngredientDto(name, qty, unit, unitCost));
        }

        return list;
    }

    private static List<ParsedModifierGroupDto> ParseModifierGroups(string raw)
    {
        var list = new List<ParsedModifierGroupDto>();
        if (string.IsNullOrWhiteSpace(raw)) return list;

        foreach (var grpPart in raw.Split(['|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var colon = grpPart.IndexOf(':');
            if (colon > 0)
            {
                var grpName = grpPart[..colon].Trim();
                var opts = grpPart[(colon + 1)..]
                    .Split([',', '+'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(o => !string.IsNullOrWhiteSpace(o))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (!string.IsNullOrWhiteSpace(grpName) && opts.Count > 0)
                    list.Add(new ParsedModifierGroupDto(grpName, opts));
            }
            else
            {
                var opts = grpPart.Split([',', '+'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
                if (opts.Count > 0)
                    list.Add(new ParsedModifierGroupDto("Adicionais", opts));
            }
        }
        return list;
    }

    internal static decimal ParsePriceDecimal(string raw) => ParseOptionalDecimal(raw) ?? 0m;

    internal static decimal? ParseOptionalDecimal(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var clean = raw.Replace("R$", string.Empty, StringComparison.OrdinalIgnoreCase).Replace(" ", string.Empty).Trim();
        if (clean.Contains(',') && clean.Contains('.'))
        {
            clean = clean.Replace(".", string.Empty).Replace(',', '.');
        }
        else if (clean.Contains(','))
        {
            clean = clean.Replace(',', '.');
        }
        return decimal.TryParse(clean, NumberStyles.Any, CultureInfo.InvariantCulture, out var val) ? val : null;
    }

    private static string DecodeTextWithFallback(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        var utf8 = Encoding.UTF8.GetString(bytes);
        if (!utf8.Contains('\uFFFD')) return utf8;
        return Encoding.Latin1.GetString(bytes);
    }

    private sealed class PitakoBackupJsonAdapter : IBackupImportAdapter
    {
        public string AdapterId => "pitako-json";
        public string DisplayName => "PITAKO Backup JSON";
        public string Description => "Exportação estruturada nativa do PITAKO (categorias, produtos, variações, insumos e fichas técnicas).";
        public IReadOnlyList<string> SupportedExtensions => [".json"];

        public bool CanHandle(string fileName, string textContent)
        {
            if (!textContent.TrimStart().StartsWith('{')) return false;
            return textContent.Contains("\"categories\"", StringComparison.OrdinalIgnoreCase) &&
                   textContent.Contains("\"products\"", StringComparison.OrdinalIgnoreCase);
        }

        public List<RawSpreadsheetRow> ExtractRows(string fileName, string textContent, out List<string> incompatibleNotes, out List<BackupFieldMappingItem> mappings)
        {
            incompatibleNotes = [];
            mappings = [];
            var rows = new List<RawSpreadsheetRow>();

            using var doc = JsonDocument.Parse(textContent);
            var root = doc.RootElement;
            var catMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (root.TryGetProperty("categories", out var catsEl) && catsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var c in catsEl.EnumerateArray())
                {
                    var id = c.TryGetProperty("id", out var idEl) ? idEl.ToString() : string.Empty;
                    var name = c.TryGetProperty("name", out var nEl) ? nEl.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(name))
                        catMap[id] = name;
                }
            }

            if (root.TryGetProperty("products", out var prodsEl) && prodsEl.ValueKind == JsonValueKind.Array)
            {
                var idx = 1;
                foreach (var p in prodsEl.EnumerateArray())
                {
                    var pName = p.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty;
                    var pCatId = p.TryGetProperty("categoryId", out var cid) ? cid.ToString() : string.Empty;
                    var pCatName = p.TryGetProperty("categoryName", out var cn) ? cn.GetString() ?? string.Empty : catMap.GetValueOrDefault(pCatId, "Geral");
                    var pSku = p.TryGetProperty("sku", out var sk) ? sk.GetString() ?? string.Empty : string.Empty;
                    var pDesc = p.TryGetProperty("description", out var ds) ? ds.GetString() ?? string.Empty : string.Empty;
                    var pPrice = p.TryGetProperty("price", out var pr) ? pr.ToString() : (p.TryGetProperty("basePrice", out var bp) ? bp.ToString() : "0");
                    var varsText = string.Empty;
                    if (p.TryGetProperty("variations", out var vEl) && vEl.ValueKind == JsonValueKind.Array)
                    {
                        var vList = vEl.EnumerateArray().Select(v =>
                        {
                            var vn = v.TryGetProperty("variationName", out var vne) ? vne.GetString() : (v.TryGetProperty("name", out var vn2) ? vn2.GetString() : null);
                            var vp = v.TryGetProperty("price", out var vpe) ? vpe.ToString() : "0";
                            return !string.IsNullOrWhiteSpace(vn) ? $"{vn}:{vp}" : null;
                        }).Where(x => x is not null);
                        varsText = string.Join("|", vList!);
                    }

                    rows.Add(new RawSpreadsheetRow(idx++, pSku, pCatName, pName, pDesc, pPrice, string.Empty, varsText, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty));
                }
            }

            if (root.TryGetProperty("ordersCount", out _))
                incompatibleNotes.Add("Histórico de pedidos presente no JSON não é sobrescrito durante importação de catálogo para preservar auditoria.");

            mappings.Add(new("products[].name", "Product.Name", rows.Count));
            mappings.Add(new("products[].price / basePrice", "Product.BasePrice", rows.Count(r => ParsePriceDecimal(r.RawPrice) > 0)));
            mappings.Add(new("categories[].name", "Category.Name", rows.Count(r => !string.IsNullOrWhiteSpace(r.CategoryName))));
            return rows;
        }
    }

    private sealed class StructuredMenuJsonAdapter : IBackupImportAdapter
    {
        public string AdapterId => "menu-json-generic";
        public string DisplayName => "JSON Estruturado de Cardápio / PDV";
        public string Description => "Backups JSON contendo grupos/categorias e lista de itens com nome, descrição, preço e variações.";
        public IReadOnlyList<string> SupportedExtensions => [".json"];

        public bool CanHandle(string fileName, string textContent)
        {
            var trimmed = textContent.TrimStart();
            return trimmed.StartsWith('{') || trimmed.StartsWith('[');
        }

        public List<RawSpreadsheetRow> ExtractRows(string fileName, string textContent, out List<string> incompatibleNotes, out List<BackupFieldMappingItem> mappings)
        {
            incompatibleNotes = [];
            mappings = [];
            var rows = new List<RawSpreadsheetRow>();

            using var doc = JsonDocument.Parse(textContent);
            var root = doc.RootElement;
            var idx = 1;

            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in root.EnumerateArray())
                    TryAddJsonItem(item, "Geral", rows, ref idx);
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                var groupsProp = root.TryGetProperty("groups", out var g1) ? g1
                    : root.TryGetProperty("categories", out var g2) ? g2
                    : root.TryGetProperty("menu", out var g3) ? g3
                    : default;

                if (groupsProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var grp in groupsProp.EnumerateArray())
                    {
                        var catName = GetStr(grp, "name", "title", "category", "grupo") ?? "Geral";
                        var itemsProp = grp.TryGetProperty("items", out var it1) ? it1
                            : grp.TryGetProperty("products", out var it2) ? it2
                            : default;
                        if (itemsProp.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var item in itemsProp.EnumerateArray())
                                TryAddJsonItem(item, catName, rows, ref idx);
                        }
                        else if (grp.TryGetProperty("price", out _) || grp.TryGetProperty("preco", out _))
                        {
                            TryAddJsonItem(grp, "Geral", rows, ref idx);
                        }
                    }
                }
                else if (root.TryGetProperty("items", out var itemsRoot) && itemsRoot.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in itemsRoot.EnumerateArray())
                        TryAddJsonItem(item, "Geral", rows, ref idx);
                }
            }

            if (rows.Count == 0)
                incompatibleNotes.Add("Estrutura JSON sem matrizes reconhecidas (categories/groups/items/products).");

            mappings.Add(new("item.name / title / nome", "Product.Name", rows.Count));
            mappings.Add(new("category / group.name", "Category.Name", rows.Count));
            mappings.Add(new("price / valor", "Product.BasePrice", rows.Count));
            return rows;
        }

        private static void TryAddJsonItem(JsonElement item, string defaultCat, List<RawSpreadsheetRow> rows, ref int idx)
        {
            if (item.ValueKind != JsonValueKind.Object) return;
            var name = GetStr(item, "name", "title", "produto", "nome");
            if (string.IsNullOrWhiteSpace(name)) return;
            var cat = GetStr(item, "categoryName", "category", "categoria", "grupo") ?? defaultCat;
            var desc = GetStr(item, "description", "descricao", "details") ?? string.Empty;
            var sku = GetStr(item, "sku", "externalCode", "code", "codigo") ?? string.Empty;
            var price = GetStr(item, "price", "preco", "valor", "unitPrice") ?? "0";
            rows.Add(new RawSpreadsheetRow(idx++, sku, cat, name, desc, price, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty));
        }

        private static string? GetStr(JsonElement el, params string[] keys)
        {
            foreach (var k in keys)
            {
                if (el.TryGetProperty(k, out var val) && val.ValueKind is JsonValueKind.String or JsonValueKind.Number)
                    return val.ToString();
            }
            return null;
        }
    }

    private sealed class TabularPosBackupAdapter : IBackupImportAdapter
    {
        public string AdapterId => "tabular-pos";
        public string DisplayName => "Planilha Tabular POS (Consumer / Goomer / Saipos / CSV / XLS / XLSX)";
        public string Description => "Exportações em CSV, Excel .xls (HTML/XML) e .xlsx com colunas de código, categoria, produto, preço, descrição e ingredientes.";
        public IReadOnlyList<string> SupportedExtensions => [".csv", ".xls", ".xlsx", ".txt"];

        public bool CanHandle(string fileName, string textContent)
        {
            var ext = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
            return ext is ".csv" or ".xls" or ".xlsx" or ".txt";
        }

        public List<RawSpreadsheetRow> ExtractRows(string fileName, string textContent, out List<string> incompatibleNotes, out List<BackupFieldMappingItem> mappings)
        {
            incompatibleNotes = [];
            var res = ParseSpreadsheetPayload(fileName, Encoding.UTF8.GetBytes(textContent));
            var rows = res.Ok ? res.Rows : [];
            mappings =
            [
                new("Coluna Produto/Nome", "Product.Name", rows.Count(r => !string.IsNullOrWhiteSpace(r.ProductName))),
                new("Coluna Categoria/Grupo", "Category.Name", rows.Count(r => !string.IsNullOrWhiteSpace(r.CategoryName))),
                new("Coluna Preço/Valor", "Product.BasePrice", rows.Count(r => ParsePriceDecimal(r.RawPrice) > 0)),
                new("Coluna Ingredientes", "TechnicalSheet.Ingredients", rows.Count(r => !string.IsNullOrWhiteSpace(r.RawIngredients)))
            ];
            return rows;
        }
    }

    [GeneratedRegex(@"<tr[^>]*>(.*?)</tr>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TrRegex();

    [GeneratedRegex(@"<t[dh][^>]*>(.*?)</t[dh]>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TdThRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex HtmlTagsRegex();

    [GeneratedRegex(@"(\d+(?:[.,]\d+)?)\s*(ml|l|g|kg|un|fatias?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex MeasureRegex();
}
