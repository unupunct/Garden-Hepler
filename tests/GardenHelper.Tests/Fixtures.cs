using System.IO.Compression;
using System.Text;
using GardenHelper.Core.Data;
using GardenHelper.Core.Library;
using Microsoft.Extensions.Logging.Abstractions;

namespace GardenHelper.Tests;

/// <summary>An isolated data root with a migrated database and the services under test.</summary>
public sealed class TestEnv : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "gh-test-" + Guid.NewGuid().ToString("N")[..8]);
    public AppPaths Paths { get; }
    public ContextFactory Factory { get; }
    public LibraryService Library { get; }

    private TestEnv()
    {
        Paths = new AppPaths(Root);
        Factory = new ContextFactory(Paths);
        Library = new LibraryService(Factory, Paths, NullLogger<LibraryService>.Instance);
    }

    public static async Task<TestEnv> CreateAsync()
    {
        var env = new TestEnv();
        await DbFactory.InitializeAsync(env.Paths);
        return env;
    }

    public string File(string name) => Path.Combine(Root, "src-" + name);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(Root, true); } catch (IOException) { }
    }
}

public static class Fixtures
{
    public const string Seeds = "Sămânța de roșii se păstrează într-un loc uscat și răcoros. Tomato seeds keep best when cool and dry. Compost improves soil structure and feeds soil life.";

    public static string RepoContent()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "content", "plants"))) dir = Path.GetDirectoryName(dir);
        return Path.Combine(dir ?? throw new DirectoryNotFoundException("content folder not found"), "content");
    }

    public static string Epub(string path, string title = "Mulching Handbook", string body = Seeds)
    {
        using var fs = File.Create(path);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        void Add(string name, string text)
        {
            var e = zip.CreateEntry(name);
            using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
            w.Write(text);
        }
        Add("mimetype", "application/epub+zip");
        Add("META-INF/container.xml", "<?xml version='1.0'?><container xmlns='urn:oasis:names:tc:opendocument:xmlns:container' version='1.0'><rootfiles><rootfile full-path='OEBPS/content.opf' media-type='application/oebps-package+xml'/></rootfiles></container>");
        Add("OEBPS/content.opf", $@"<?xml version='1.0'?><package xmlns='http://www.idpf.org/2007/opf' version='3.0'>
<metadata xmlns:dc='http://purl.org/dc/elements/1.1/'><dc:title>{title}</dc:title><dc:creator>A. Gardener</dc:creator><dc:language>en</dc:language><dc:subject>Permaculture</dc:subject></metadata>
<manifest><item id='c1' href='ch1.xhtml' media-type='application/xhtml+xml'/><item id='c2' href='ch2.xhtml' media-type='application/xhtml+xml'/><item id='ncx' href='toc.ncx' media-type='application/x-dtbncx+xml'/></manifest>
<spine><itemref idref='c1'/><itemref idref='c2'/></spine></package>");
        Add("OEBPS/toc.ncx", "<?xml version='1.0'?><ncx xmlns='http://www.daisy.org/z3986/2005/ncx/'><navMap><navPoint id='n1'><navLabel><text>Soil</text></navLabel><content src='ch1.xhtml'/></navPoint><navPoint id='n2'><navLabel><text>Water</text></navLabel><content src='ch2.xhtml'/></navPoint></navMap></ncx>");
        Add("OEBPS/ch1.xhtml", $"<html xmlns='http://www.w3.org/1999/xhtml'><body><h1>Chapter One</h1><p>{body}</p></body></html>");
        Add("OEBPS/ch2.xhtml", "<html xmlns='http://www.w3.org/1999/xhtml'><body><h1>Chapter Two</h1><p>Mulch suppresses weeds and keeps moisture in the ground. Mulch also protects the soil surface.</p></body></html>");
        return path;
    }

    public static string Docx(string path, string text)
    {
        using var fs = File.Create(path);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        var e = zip.CreateEntry("word/document.xml");
        using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
        w.Write($"<w:document xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main'><w:body><w:p><w:pPr><w:pStyle w:val='Heading1'/></w:pPr><w:r><w:t>Intro</w:t></w:r></w:p><w:p><w:r><w:t>{text}</w:t></w:r></w:p></w:body></w:document>");
        return path;
    }

    public static string Fb2(string path, string text)
    {
        File.WriteAllText(path, $"<?xml version='1.0' encoding='utf-8'?><FictionBook xmlns='http://www.gribuser.ru/xml/fictionbook/2.0'><description><title-info><genre>nonfiction</genre><author><first-name>Ana</first-name><last-name>Pop</last-name></author><book-title>Grădina mea</book-title><lang>ro</lang></title-info></description><body><section><title><p>Capitolul 1</p></title><p>{text}</p></section></body></FictionBook>", new UTF8Encoding(false));
        return path;
    }

    public static string Rtf(string path, string text)
    {
        File.WriteAllText(path, @"{\rtf1\ansi\deff0{\fonttbl{\f0 Arial;}}\f0\fs24 " + text.Replace("ă", @"\'e3") + @"\par Second paragraph about mulch.}", Encoding.Latin1);
        return path;
    }

    public static string Mobi(string path, string title, string html)
    {
        var text = Encoding.UTF8.GetBytes(html);
        var name = Encoding.UTF8.GetBytes(title);
        var r0 = new byte[16 + 232 + name.Length];
        void U16(int o, int v) { r0[o] = (byte)(v >> 8); r0[o + 1] = (byte)v; }
        void U32(int o, int v) { r0[o] = (byte)(v >> 24); r0[o + 1] = (byte)(v >> 16); r0[o + 2] = (byte)(v >> 8); r0[o + 3] = (byte)v; }
        U16(0, 1); U32(4, text.Length); U16(8, 1); U16(10, 4096);
        Encoding.ASCII.GetBytes("MOBI").CopyTo(r0, 16);
        U32(20, 232); U32(28, 65001);
        U32(84, 16 + 232); U32(88, name.Length);
        name.CopyTo(r0, 16 + 232);
        var header = new byte[78 + 2 * 8 + 2];
        Encoding.ASCII.GetBytes("BOOKMOBI").CopyTo(header, 60);
        header[76] = 0; header[77] = 2;
        var off0 = header.Length;
        var off1 = off0 + r0.Length;
        void H32(int o, int v) { header[o] = (byte)(v >> 24); header[o + 1] = (byte)(v >> 16); header[o + 2] = (byte)(v >> 8); header[o + 3] = (byte)v; }
        H32(78, off0); H32(86, off1);
        using var fs = File.Create(path);
        fs.Write(header); fs.Write(r0); fs.Write(text);
        return path;
    }

    public static string Pdf(string path, params string[] pages)
    {
        var sb = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        void Obj(string body) { offsets.Add(sb.Length); sb.Append(offsets.Count).Append(" 0 obj\n").Append(body).Append("\nendobj\n"); }
        var n = pages.Length;
        Obj("<</Type/Catalog/Pages 2 0 R>>");
        Obj("<</Type/Pages/Kids[" + string.Join(" ", Enumerable.Range(0, n).Select(i => $"{3 + i * 2} 0 R")) + $"]/Count {n}>>");
        for (var i = 0; i < n; i++)
        {
            Obj($"<</Type/Page/Parent 2 0 R/MediaBox[0 0 612 792]/Contents {4 + i * 2} 0 R/Resources<</Font<</F1 {3 + n * 2} 0 R>>>>>>");
            var content = $"BT /F1 12 Tf 72 700 Td ({pages[i]}) Tj ET";
            Obj($"<</Length {content.Length}>>\nstream\n{content}\nendstream");
        }
        Obj("<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>");
        var xref = sb.Length;
        sb.Append("xref\n0 ").Append(offsets.Count + 1).Append("\n0000000000 65535 f \n");
        foreach (var o in offsets) sb.Append(o.ToString("D10")).Append(" 00000 n \n");
        sb.Append("trailer\n<</Size ").Append(offsets.Count + 1).Append("/Root 1 0 R>>\nstartxref\n").Append(xref).Append("\n%%EOF");
        File.WriteAllText(path, sb.ToString(), Encoding.ASCII);
        return path;
    }
}
