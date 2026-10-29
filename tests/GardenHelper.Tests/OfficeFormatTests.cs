using System.IO.Compression;
using System.Text;
using GardenHelper.Core.Library;
using Xunit;

namespace GardenHelper.Tests;

public class OfficeFormatTests
{
    private static string MakeZip(string path, params (string Name, string Content)[] files)
    {
        using var fs = File.Create(path);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        foreach (var (name, content) in files)
        {
            var e = zip.CreateEntry(name);
            using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
            w.Write(content);
        }
        return path;
    }

    [Fact]
    public async Task Pptx_slides_become_sections()
    {
        using var env = await TestEnv.CreateAsync();
        var slide = "<p:sld xmlns:p='p' xmlns:a='a'><p:cSld><p:spTree><a:p><a:r><a:t>Soil health</a:t></a:r></a:p><a:p><a:r><a:t>Earthworms aerate the beds</a:t></a:r></a:p></p:spTree></p:cSld></p:sld>";
        var file = MakeZip(env.File("talk.pptx"), ("ppt/slides/slide2.xml", slide.Replace("Soil health", "Second slide")), ("ppt/slides/slide1.xml", slide),
            ("docProps/core.xml", "<cp:coreProperties xmlns:cp='cp' xmlns:dc='dc'><dc:title>Garden Talk</dc:title></cp:coreProperties>"));
        var r = (await env.Library.ImportAsync(new[] { file })).Single();
        Assert.Null(r.Error);
        Assert.Equal("Garden Talk", r.Book!.Title);
        Assert.Equal("PPTX", r.Book.Format);
        Assert.NotEmpty(await env.Library.SearchAsync("earthworms aerate"));
        var heads = await env.Library.GetSectionHeadersAsync(r.Book.Id);
        Assert.StartsWith("1. Soil health", heads[0].Title);
    }

    [Fact]
    public async Task Xlsx_sheets_use_shared_strings_and_numbers()
    {
        using var env = await TestEnv.CreateAsync();
        var sheet = "<worksheet xmlns='x'><sheetData><row r='1'><c t='s'><v>0</v></c><c><v>12</v></c></row><row r='2'><c t='s'><v>1</v></c><c><v>7.5</v></c></row></sheetData></worksheet>";
        var file = MakeZip(env.File("plan.xlsx"),
            ("xl/workbook.xml", "<workbook xmlns='x'><sheets><sheet name='Seeds'/></sheets></workbook>"),
            ("xl/sharedStrings.xml", "<sst xmlns='x'><si><t>Tomato</t></si><si><t>Beetroot</t></si></sst>"),
            ("xl/worksheets/sheet1.xml", sheet));
        var r = (await env.Library.ImportAsync(new[] { file })).Single();
        Assert.Null(r.Error);
        var section = await env.Library.GetSectionAsync(r.Book!.Id, 1);
        Assert.Equal("Seeds", section!.Title);
        Assert.Contains("Tomato\t12", section.Text);
        Assert.Contains("Beetroot\t7.5", section.Text);
    }

    [Fact]
    public async Task Ods_and_odp_are_read()
    {
        using var env = await TestEnv.CreateAsync();
        var ods = MakeZip(env.File("beds.ods"), ("content.xml",
            "<office:document-content xmlns:office='o' xmlns:table='urn:oasis:names:tc:opendocument:xmlns:table:1.0' xmlns:text='t'><table:table table:name='Beds'><table:table-row><table:table-cell><text:p>Bed 1</text:p></table:table-cell><table:table-cell><text:p>Leeks</text:p></table:table-cell></table:table-row></table:table></office:document-content>"));
        var odp = MakeZip(env.File("pitch.odp"), ("content.xml",
            "<office:document-content xmlns:office='o' xmlns:draw='urn:oasis:names:tc:opendocument:xmlns:drawing:1.0' xmlns:text='t'><draw:page><text:p>Permaculture basics</text:p><text:p>Observe first</text:p></draw:page></office:document-content>"));
        var results = await env.Library.ImportAsync(new[] { ods, odp });
        Assert.All(results, r => Assert.Null(r.Error));
        Assert.Contains("Leeks", (await env.Library.GetSectionAsync(results[0].Book!.Id, 1))!.Text);
        Assert.NotEmpty(await env.Library.SearchAsync("observe first"));
    }

    [Theory]
    [InlineData("notes.csv")] [InlineData("diary.log")] [InlineData("guide.rst")] [InlineData("paper.tex")]
    public async Task Text_like_files_import(string name)
    {
        using var env = await TestEnv.CreateAsync();
        var file = env.File(name);
        await File.WriteAllTextAsync(file, "Garden record\n\nPlant the beans after the last frost when the soil is warm.");
        var r = (await env.Library.ImportAsync(new[] { file })).Single();
        Assert.Null(r.Error);
        Assert.NotEmpty(await env.Library.SearchAsync("beans frost"));
    }

    [Fact]
    public async Task Damaged_office_files_give_a_clear_error()
    {
        using var env = await TestEnv.CreateAsync();
        var file = env.File("broken.pptx"); await File.WriteAllTextAsync(file, "this is not a zip");
        var r = (await env.Library.ImportAsync(new[] { file })).Single();
        Assert.Null(r.Book);
        Assert.False(string.IsNullOrWhiteSpace(r.Error));
    }
}
