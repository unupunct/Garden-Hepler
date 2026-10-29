using System.Net;
using System.Text;

namespace GardenHelper.Core.Planning;

public sealed record ReportImage(string Caption, string Date, string DataUri);

/// <summary>A single, self-contained web page of a report (pictures included) that anyone can open in a browser, to share a season with family or friends. Read-only, no scripts.</summary>
public static class HtmlReport
{
    public static string Build(string title, string footer, IEnumerable<(string Value, string Label)> tiles, IEnumerable<(string Title, IEnumerable<string> Lines)> sections, IEnumerable<ReportImage> images, string galleryTitle)
    {
        string E(string s) => WebUtility.HtmlEncode(s);
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><title>").Append(E(title)).Append("</title><style>")
          .Append("body{margin:0;background:#f4f1e8;color:#1f2a1b;font:16px/1.5 system-ui,Segoe UI,sans-serif}main{max-width:860px;margin:0 auto;padding:24px 18px 48px}h1{font-family:Georgia,serif;font-size:30px;margin:0 0 4px}")
          .Append("h2{font-family:Georgia,serif;font-size:20px;margin:26px 0 8px}.tiles{display:flex;flex-wrap:wrap;gap:10px;margin:16px 0}.tile{background:#fff;border:1px solid #ddd6c2;border-radius:16px;padding:10px 16px;min-width:130px}")
          .Append(".tile b{display:block;font-size:26px;color:#2f5d3a}.tile span{font-size:12px;color:#6c7a62}ul{padding-left:20px}li{margin:3px 0}.gal{display:grid;grid-template-columns:repeat(auto-fill,minmax(200px,1fr));gap:12px}")
          .Append(".gal figure{margin:0;background:#fff;border:1px solid #ddd6c2;border-radius:14px;overflow:hidden}.gal img{width:100%;height:150px;object-fit:cover;display:block}.gal figcaption{padding:6px 10px;font-size:13px}footer{margin-top:32px;color:#6c7a62;font-size:12px}")
          .Append("@media (prefers-color-scheme:dark){body{background:#141a12;color:#e8efe4}.tile,.gal figure{background:#1f281c;border-color:#33402f}.tile b{color:#79c288}}</style></head><body><main>");
        sb.Append("<h1>").Append(E(title)).Append("</h1>");
        var tileList = tiles.ToList();
        if (tileList.Count > 0)
        {
            sb.Append("<div class=\"tiles\">");
            foreach (var (value, label) in tileList) sb.Append("<div class=\"tile\"><b>").Append(E(value)).Append("</b><span>").Append(E(label)).Append("</span></div>");
            sb.Append("</div>");
        }
        foreach (var (sectionTitle, lines) in sections)
        {
            sb.Append("<h2>").Append(E(sectionTitle)).Append("</h2><ul>");
            foreach (var line in lines) sb.Append("<li>").Append(E(line)).Append("</li>");
            sb.Append("</ul>");
        }
        var imgs = images.ToList();
        if (imgs.Count > 0)
        {
            sb.Append("<h2>").Append(E(galleryTitle)).Append("</h2><div class=\"gal\">");
            foreach (var i in imgs) sb.Append("<figure><img alt=\"").Append(E(i.Caption)).Append("\" src=\"").Append(i.DataUri.StartsWith("data:image/", StringComparison.Ordinal) ? E(i.DataUri) : "").Append("\"><figcaption>").Append(E(i.Caption)).Append(" <small>").Append(E(i.Date)).Append("</small></figcaption></figure>");
            sb.Append("</div>");
        }
        sb.Append("<footer>").Append(E(footer)).Append("</footer></main></body></html>");
        return sb.ToString();
    }
}
