using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using Pitako.Modules.Reports.Application.Abstractions;
using Pitako.Modules.Reports.Domain;

namespace Pitako.Modules.Reports.Application.Export;

/// <summary>
/// Gerador real de relatórios nos formatos CSV (com proteção contra Formula Injection),
/// Excel OpenXML (.xlsx via ZipArchive) e PDF 1.4 válido com tabela xref exata (Seção 30).
/// </summary>
public static class ReportFormatExporters
{
    public static ExportedReportFile Export(ReportDataset dataset, ReportExportFormat format)
    {
        ArgumentNullException.ThrowIfNull(dataset);

        var slug = $"{dataset.Category}_{dataset.GeneratedAtUtc:yyyyMMddHHmmss}".ToLowerInvariant();
        var (ext, mime, bytes) = format switch
        {
            ReportExportFormat.Csv => ("csv", "text/csv; charset=utf-8", BuildCsvBytes(dataset)),
            ReportExportFormat.ExcelXlsx => ("xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", BuildXlsxBytes(dataset)),
            ReportExportFormat.Pdf => ("pdf", "application/pdf", BuildPdfBytes(dataset)),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Formato de exportação inválido.")
        };

        var sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return new ExportedReportFile($"{slug}.{ext}", mime, bytes, sha256, dataset.Rows.Count);
    }

    private static byte[] BuildCsvBytes(ReportDataset dataset)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"\"Estabelecimento\",\"{EscapeCsvCell(dataset.StoreTradeName)}\",\"CNPJ\",\"{EscapeCsvCell(dataset.StoreFormattedCnpj)}\"");
        sb.AppendLine($"\"Relatório\",\"{EscapeCsvCell(dataset.ReportTitle)}\",\"Período\",\"{dataset.PeriodStartUtc:yyyy-MM-dd} a {dataset.PeriodEndUtc:yyyy-MM-dd}\"");
        sb.AppendLine(string.Join(",", dataset.Columns.Select(c => $"\"{EscapeCsvCell(c)}\"")));

        foreach (var row in dataset.Rows)
            sb.AppendLine(string.Join(",", row.Select(cell => $"\"{EscapeCsvCell(cell)}\"")));

        var utf8Bom = Encoding.UTF8.GetPreamble();
        var body = Encoding.UTF8.GetBytes(sb.ToString());
        var result = new byte[utf8Bom.Length + body.Length];
        Buffer.BlockCopy(utf8Bom, 0, result, 0, utf8Bom.Length);
        Buffer.BlockCopy(body, 0, result, utf8Bom.Length, body.Length);
        return result;
    }

    private static byte[] BuildXlsxBytes(ReportDataset dataset)
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteZipEntry(archive, "[Content_Types].xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                </Types>
                """);

            WriteZipEntry(archive, "_rels/.rels",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
                </Relationships>
                """);

            WriteZipEntry(archive, "xl/workbook.xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets>
                    <sheet name="Relatorio" sheetId="1" r:id="rId1"/>
                  </sheets>
                </workbook>
                """);

            WriteZipEntry(archive, "xl/_rels/workbook.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                </Relationships>
                """);

            var sheetXml = BuildWorksheetXml(dataset);
            WriteZipEntry(archive, "xl/worksheets/sheet1.xml", sheetXml);
        }

        return ms.ToArray();
    }

    private static string BuildWorksheetXml(ReportDataset dataset)
    {
        var sb = new StringBuilder();
        sb.Append("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>""");

        int rowIdx = 1;
        AppendXmlRow(sb, rowIdx++, [dataset.StoreTradeName, $"CNPJ: {dataset.StoreFormattedCnpj}", dataset.ReportTitle, $"{dataset.PeriodStartUtc:dd/MM/yyyy} - {dataset.PeriodEndUtc:dd/MM/yyyy}"]);
        AppendXmlRow(sb, rowIdx++, dataset.Columns);
        foreach (var row in dataset.Rows)
            AppendXmlRow(sb, rowIdx++, row);

        sb.Append("</sheetData></worksheet>");
        return sb.ToString();
    }

    private static void AppendXmlRow(StringBuilder sb, int rowIndex, IReadOnlyList<string> cells)
    {
        sb.Append(CultureInfo.InvariantCulture, $"<row r=\"{rowIndex}\">");
        for (var col = 0; col < cells.Count; col++)
        {
            var colRef = GetExcelColumnName(col) + rowIndex.ToString(CultureInfo.InvariantCulture);
            var safe = SecurityElement.Escape(NeutralizeSpreadsheetFormula(cells[col] ?? string.Empty)) ?? string.Empty;
            sb.Append(CultureInfo.InvariantCulture, $"<c r=\"{colRef}\" t=\"inlineStr\"><is><t>{safe}</t></is></c>");
        }
        sb.Append("</row>");
    }

    private static string GetExcelColumnName(int index)
    {
        var colName = string.Empty;
        var dividend = index + 1;
        while (dividend > 0)
        {
            var modulo = (dividend - 1) % 26;
            colName = Convert.ToChar(65 + modulo) + colName;
            dividend = (dividend - modulo) / 26;
        }
        return colName;
    }

    private static byte[] BuildPdfBytes(ReportDataset dataset)
    {
        var lines = new List<string>
        {
            $"{dataset.StoreTradeName} - CNPJ: {dataset.StoreFormattedCnpj}",
            $"RELATORIO: {dataset.ReportTitle}",
            $"Periodo: {dataset.PeriodStartUtc:dd/MM/yyyy} a {dataset.PeriodEndUtc:dd/MM/yyyy} | Emitido: {dataset.GeneratedAtUtc:dd/MM/yyyy HH:mm}",
            new string('-', 78)
        };

        if (dataset.Kpis.Count > 0)
        {
            var kpiLine = string.Join(" | ", dataset.Kpis.Select(kv => $"{kv.Key}: {kv.Value:N2}"));
            lines.Add($"Indicadores: {kpiLine}");
            lines.Add(new string('-', 78));
        }

        lines.Add(string.Join(" | ", dataset.Columns));
        lines.Add(new string('-', 78));

        foreach (var row in dataset.Rows.Take(38))
            lines.Add(string.Join(" | ", row));

        var contentSb = new StringBuilder();
        contentSb.AppendLine("BT");
        contentSb.AppendLine("/F1 10 Tf");
        contentSb.AppendLine("40 800 Td");
        contentSb.AppendLine("14 TL");
        foreach (var line in lines)
        {
            contentSb.Append('(').Append(EscapePdfText(line)).AppendLine(") Tj T*");
        }
        contentSb.AppendLine("ET");

        var enc = Encoding.Latin1;
        var streamBytes = enc.GetBytes(contentSb.ToString());

        using var ms = new MemoryStream();
        void WriteAscii(string s) => ms.Write(enc.GetBytes(s));

        WriteAscii("%PDF-1.4\n");
        var offsets = new List<long>();

        void BeginObj(int id)
        {
            offsets.Add(ms.Position);
            WriteAscii($"{id} 0 obj\n");
        }

        BeginObj(1);
        WriteAscii("<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        BeginObj(2);
        WriteAscii("<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

        BeginObj(3);
        WriteAscii("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>\nendobj\n");

        BeginObj(4);
        WriteAscii("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>\nendobj\n");

        BeginObj(5);
        WriteAscii($"<< /Length {streamBytes.Length} >>\nstream\n");
        ms.Write(streamBytes);
        WriteAscii("\nendstream\nendobj\n");

        var xrefOffset = ms.Position;
        WriteAscii($"xref\n0 {offsets.Count + 1}\n");
        WriteAscii("0000000000 65535 f \n");
        foreach (var off in offsets)
            WriteAscii($"{off:D10} 00000 n \n");

        WriteAscii($"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF\n");
        return ms.ToArray();
    }

    private static void WriteZipEntry(ZipArchive archive, string entryPath, string content)
    {
        var entry = archive.CreateEntry(entryPath, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(content.TrimStart());
    }

    private static string EscapeCsvCell(string? value)
    {
        var neutralized = NeutralizeSpreadsheetFormula(value ?? string.Empty);
        return neutralized.Replace("\"", "\"\"", StringComparison.Ordinal);
    }

    private static string NeutralizeSpreadsheetFormula(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var trimmed = value.TrimStart();
        if (trimmed.Length > 0 && trimmed[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
            return $"'{value}";

        return value;
    }

    private static string EscapePdfText(string input) =>
        (input ?? string.Empty)
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("(", "\\(", StringComparison.Ordinal)
            .Replace(")", "\\)", StringComparison.Ordinal);
}
