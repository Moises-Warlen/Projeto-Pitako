using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Pitako.Modules.Reports.Application.Export;
using Pitako.Modules.Reports.Domain;
using Xunit;

namespace Pitako.UnitTests;

public sealed class ReportsExportUnitTests
{
    [Fact]
    public void ReportDataset_ShouldForbidPitakoBranding_AndExportersShouldEmitRealCsvXlsxPdf()
    {
        var tenantId = Guid.NewGuid();
        var t0 = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        var t1 = new DateTimeOffset(2026, 11, 25, 23, 59, 59, TimeSpan.Zero);

        var pitakoDenied = ReportDataset.Create(
            tenantId,
            ReportCategory.SalesByPeriod,
            "Vendas",
            "PITAKO",
            "11222333000181",
            t0,
            t1,
            t1,
            ["Canal", "Total"],
            [["PDV", "100.00"]]);

        Assert.True(pitakoDenied.IsFailure);
        Assert.Equal("Report.PitakoBrandingForbidden", pitakoDenied.Error.Code);

        var dataset = ReportDataset.Create(
            tenantId,
            ReportCategory.SalesByChannel,
            "Vendas por Canal",
            "Açaí Imperial",
            "11222333000181",
            t0,
            t1,
            t1,
            ["Canal", "Pedidos", "Faturamento", "Observacao"],
            [
                ["Delivery Próprio", "12", "540.00", "=CMD|'/C CALC'!A0"],
                ["QR Code Mesa", "8", "320.50", "Normal"]
            ],
            new Dictionary<string, decimal> { ["Total"] = 860.50m, ["TicketMedio"] = 43.03m }).Value;

        // 1. CSV: BOM UTF-8 + proteção Formula Injection
        var csv = ReportFormatExporters.Export(dataset, ReportExportFormat.Csv);
        Assert.Equal("text/csv; charset=utf-8", csv.MimeType);
        Assert.Equal(0xEF, csv.ContentBytes[0]);
        Assert.Equal(0xBB, csv.ContentBytes[1]);
        Assert.Equal(0xBF, csv.ContentBytes[2]);
        var csvText = Encoding.UTF8.GetString(csv.ContentBytes, 3, csv.ContentBytes.Length - 3);
        Assert.Contains("Açaí Imperial", csvText);
        Assert.Contains("11.222.333/0001-81", csvText);
        Assert.Contains("'=CMD|'/C CALC'!A0", csvText);

        // 2. XLSX: Pacote OpenXML ZIP válido com parse XML verificando células decodificadas
        var xlsx = ReportFormatExporters.Export(dataset, ReportExportFormat.ExcelXlsx);
        using (var ms = new MemoryStream(xlsx.ContentBytes))
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Read))
        {
            Assert.NotNull(zip.GetEntry("[Content_Types].xml"));
            Assert.NotNull(zip.GetEntry("xl/workbook.xml"));
            var sheet = zip.GetEntry("xl/worksheets/sheet1.xml");
            Assert.NotNull(sheet);
            using var reader = new StreamReader(sheet!.Open(), Encoding.UTF8);
            var xdoc = XDocument.Parse(reader.ReadToEnd());
            var cellTexts = xdoc.Descendants().Where(e => e.Name.LocalName == "t").Select(e => e.Value).ToList();
            Assert.Contains(cellTexts, t => t.Contains("11.222.333/0001-81"));
            Assert.Contains("'=CMD|'/C CALC'!A0", cellTexts);
        }

        // 3. PDF: Cabeçalho %PDF-1.4, xref, startxref e %%EOF válidos
        var pdf = ReportFormatExporters.Export(dataset, ReportExportFormat.Pdf);
        Assert.Equal("application/pdf", pdf.MimeType);
        var pdfText = Encoding.Latin1.GetString(pdf.ContentBytes);
        Assert.StartsWith("%PDF-1.4", pdfText, StringComparison.Ordinal);
        Assert.Contains("xref", pdfText);
        Assert.Contains("startxref", pdfText);
        Assert.Contains("%%EOF", pdfText);
        Assert.Contains("11.222.333/0001-81", pdfText);
        Assert.DoesNotContain("PITAKO", pdfText, StringComparison.OrdinalIgnoreCase);
    }
}
