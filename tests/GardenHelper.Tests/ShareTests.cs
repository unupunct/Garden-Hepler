using GardenHelper.Core.Planning;
using GardenHelper.Core.Services;
using Xunit;

namespace GardenHelper.Tests;

public class ShareTests
{
    [Fact]
    public void Password_hash_verifies_only_the_right_password_and_is_salted()
    {
        var a = PasswordLock.Hash("correct horse"); var b = PasswordLock.Hash("correct horse");
        Assert.NotEqual(a, b);
        Assert.True(PasswordLock.Verify("correct horse", a)); Assert.False(PasswordLock.Verify("wrong", a));
        Assert.False(PasswordLock.Verify("x", null)); Assert.False(PasswordLock.Verify("x", "garbage")); Assert.False(PasswordLock.Verify("x", "v1:1:AA==:AA=="));
        Assert.Equal("short", PasswordLock.Problem("abc")); Assert.Null(PasswordLock.Problem("abcd"));
    }

    [Fact]
    public void Html_report_escapes_text_and_refuses_non_image_sources()
    {
        var html = HtmlReport.Build("A <b>title</b>", "foot", new[] { ("1", "tile") }, new[] { ("Sec", (IEnumerable<string>)new[] { "x < y & z" }) },
            new[] { new ReportImage("ok", "1 May", "data:image/jpeg;base64,AAAA"), new ReportImage("bad", "2 May", "javascript:alert(1)") }, "Photos");
        Assert.DoesNotContain("<b>title</b>", html); Assert.Contains("A &lt;b&gt;title&lt;/b&gt;", html);
        Assert.Contains("x &lt; y &amp; z", html); Assert.Contains("data:image/jpeg;base64,AAAA", html); Assert.DoesNotContain("javascript:", html);
        Assert.DoesNotContain("<script", html);
    }
}
