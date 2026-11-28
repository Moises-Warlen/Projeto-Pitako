using System.IO.Compression;
using System.Text;
using Pitako.Api.Endpoints;
using Xunit;

namespace Pitako.IntegrationTests;

public sealed class SpreadsheetAndBackupAdaptersUnitTests
{
    [Fact]
    public void TemplateAndSpreadsheetParser_ShouldParseCsvAndExcelHtmlWithIngredientsAndVariations()
    {
        var csv = PitakoSpreadsheetAndBackupAdapters.BuildTemplateCsvBytes();
        var parseCsv = PitakoSpreadsheetAndBackupAdapters.ParseSpreadsheetPayload("modelo.csv", csv);
        Assert.True(parseCsv.Ok);
        Assert.Equal("CSV", parseCsv.DetectedFormat);
        Assert.Equal(3, parseCsv.Rows.Count);
        Assert.Equal("Tigela Tradicional", parseCsv.Rows[0].ProductName);

        var xls = PitakoSpreadsheetAndBackupAdapters.BuildTemplateExcelHtmlBytes();
        var parseXls = PitakoSpreadsheetAndBackupAdapters.ParseSpreadsheetPayload("modelo.xls", xls);
        Assert.True(parseXls.Ok);
        Assert.Equal("Excel HTML (.xls)", parseXls.DetectedFormat);
        Assert.Equal(2, parseXls.Rows.Count);
    }

    [Fact]
    public void SpreadsheetParser_ShouldSupportXlsxOpenXmlAndDetectValidationErrors()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            var sheetEntry = zip.CreateEntry("xl/worksheets/sheet1.xml");
            using var writer = new StreamWriter(sheetEntry.Open(), Encoding.UTF8);
            writer.Write("""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <sheetData>
                    <row r="1">
                      <c r="A1" t="inlineStr"><is><t>SKU</t></is></c>
                      <c r="B1" t="inlineStr"><is><t>Categoria</t></is></c>
                      <c r="C1" t="inlineStr"><is><t>Produto</t></is></c>
                      <c r="D1" t="inlineStr"><is><t>Preco</t></is></c>
                    </row>
                    <row r="2">
                      <c r="A2" t="inlineStr"><is><t>PIZ-01</t></is></c>
                      <c r="B2" t="inlineStr"><is><t>Pizzas</t></is></c>
                      <c r="C2" t="inlineStr"><is><t>Calabresa Especial</t></is></c>
                      <c r="D2"><v>49.90</v></c>
                    </row>
                    <row r="3">
                      <c r="A3" t="inlineStr"><is><t>PIZ-02</t></is></c>
                      <c r="B3" t="inlineStr"><is><t>Pizzas</t></is></c>
                      <c r="C3" t="inlineStr"><is><t>Pizza Sem Preco</t></is></c>
                      <c r="D3"><v>0</v></c>
                    </row>
                  </sheetData>
                </worksheet>
                """);
        }

        var parseRes = PitakoSpreadsheetAndBackupAdapters.ParseSpreadsheetPayload("pizzas.xlsx", ms.ToArray());
        Assert.True(parseRes.Ok);
        Assert.Equal("Excel OpenXML (.xlsx)", parseRes.DetectedFormat);
        Assert.Equal(2, parseRes.Rows.Count);

        var evaluated = PitakoSpreadsheetAndBackupAdapters.EvaluateAgainstCatalog(parseRes.Rows, [], [], []);
        Assert.Equal("New", evaluated[0].MatchStatus);
        Assert.Equal("Error", evaluated[1].MatchStatus);
        Assert.NotEmpty(evaluated[1].Errors);
    }

    [Fact]
    public void ZipImageExtractor_ShouldAllowValidPngAndRejectFakeExecutableInsideZip()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            var validPng = zip.CreateEntry("acai-copa.png");
            using (var s = validPng.Open())
            {
                s.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52]);
            }

            var maliciousTxtAsJpg = zip.CreateEntry("malware.jpg");
            using (var s = maliciousTxtAsJpg.Open())
            {
                s.Write(Encoding.UTF8.GetBytes("MZ_FAKE_PAYLOAD_NOT_AN_IMAGE"));
            }
        }

        var extracted = PitakoSpreadsheetAndBackupAdapters.ExtractValidatedZipImages(ms.ToArray());
        Assert.True(extracted.Ok);
        Assert.Single(extracted.Images);
        Assert.Equal("acai-copa.png", extracted.Images[0].FileName);
        Assert.Equal("acaicopa", extracted.Images[0].MatchKey);
    }

    [Fact]
    public void BackupAdapters_ShouldAnalyzeStructuredJsonAndPitakoJsonBackups()
    {
        var json = """
            {
              "groups": [
                {
                  "name": "Pizzas Clássicas",
                  "items": [
                    { "sku": "MARG-01", "name": "Margherita Napoletana", "description": "Molho pelati e manjericão", "price": 52.00 },
                    { "sku": "PORT-01", "name": "Portuguesa Tradicional", "description": "Presunto, ovo e ervilha", "price": 56.00 }
                  ]
                }
              ]
            }
            """;

        var res = PitakoSpreadsheetAndBackupAdapters.AnalyzeBackupPayload("cardapio-pdv.json", Encoding.UTF8.GetBytes(json), [], [], []);
        Assert.True(res.Ok);
        Assert.NotNull(res.Result);
        Assert.Equal("menu-json-generic", res.Result!.AdapterId);
        Assert.Equal(2, res.Result.CompatibleCount);
        Assert.Equal(0, res.Result.IncompatibleCount);
    }
}
