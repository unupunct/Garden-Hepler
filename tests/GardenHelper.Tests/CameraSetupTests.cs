using System.Net;
using System.Net.Sockets;
using GardenHelper.Core.Cameras;
using GardenHelper.Core.Services;
using Xunit;

namespace GardenHelper.Tests;

public class CameraSetupTests
{
    [Theory]
    [InlineData("generic", "192.168.1.50", null, 1, "http://192.168.1.50/snapshot.jpg")]
    [InlineData("ipwebcam", "192.168.1.60", null, 1, "http://192.168.1.60:8080/shot.jpg")]
    [InlineData("hikvision", "http://10.0.0.5/", null, 2, "http://10.0.0.5/ISAPI/Streaming/channels/201/picture")]
    [InlineData("dahua", "cam.local", 8081, 1, "http://cam.local:8081/cgi-bin/snapshot.cgi?channel=1")]
    [InlineData("generic", "192.168.1.50:8000", null, 1, "http://192.168.1.50:8000/snapshot.jpg")]
    [InlineData("rtsp", "192.168.1.70", null, 1, "rtsp://192.168.1.70:554/stream1")]
    public void Addresses_are_built_from_an_ip(string preset, string host, int? port, int ch, string expected) =>
        Assert.Equal(expected, CameraPresets.BuildUrl(CameraPresets.Get(preset)!, host, port, ch));

    [Theory]
    [InlineData("")] [InlineData("a b")] [InlineData("host/../x")] [InlineData("host?x=1")]
    public void Unusable_hosts_are_refused(string host) => Assert.Null(CameraPresets.BuildUrl(CameraPresets.Get("generic")!, host));

    [Theory]
    [InlineData("Basic realm=\"DNVRS-Webs\"", "hikvision")] [InlineData("Digest realm=\"Login to Amcrest\"", "dahua")] [InlineData("AXIS 2100", "axis")] [InlineData("nginx", null)]
    public void Devices_can_be_recognised(string text, string? expected) => Assert.Equal(expected, CameraPresets.Guess(text));

    [Fact]
    public void Qr_text_with_a_login_is_split_into_address_and_credentials()
    {
        var r = CameraQrParser.Parse("http://admin:s3cr%40t@192.168.1.50:8080/shot.jpg")!;
        Assert.Equal("snapshot", r.Kind); Assert.Equal("http://192.168.1.50:8080/shot.jpg", r.Url);
        Assert.Equal("admin", r.Username); Assert.Equal("s3cr@t", r.Password);
        var rtsp = CameraQrParser.Parse("rtsp://user:pw@10.0.0.9/stream1")!;
        Assert.Equal("rtsp", rtsp.Kind); Assert.DoesNotContain("pw", rtsp.Url);
        Assert.Equal("http://192.168.1.50:8080/shot.jpg", CameraQrParser.Parse("192.168.1.50:8080/shot.jpg")!.Url);
    }

    [Theory]
    [InlineData("")] [InlineData("SN: 12345ABCDE")] [InlineData("WIFI:S:home;T:WPA;P:secret;;")] [InlineData("file:///c:/secret.txt")] [InlineData("javascript:alert(1)")]
    public void Qr_text_that_is_not_a_camera_address_is_ignored(string text) => Assert.Null(CameraQrParser.Parse(text));

    [Fact]
    public void Motion_is_a_local_change_not_a_global_one()
    {
        var still = new byte[MotionDetector.Width * MotionDetector.Height]; Array.Fill(still, (byte)100);
        var moved = (byte[])still.Clone(); for (var i = 0; i < 300; i++) moved[i] = 220;            // ~10% of the picture
        var flash = new byte[still.Length]; Array.Fill(flash, (byte)230);                           // everything changed: lighting, not movement
        Assert.True(MotionDetector.IsMotion(still, moved, 50, out var pct)); Assert.InRange(pct, 9, 11);
        Assert.False(MotionDetector.IsMotion(still, flash, 50, out _));
        Assert.False(MotionDetector.IsMotion(still, still, 100, out var none)); Assert.Equal(0, none);
        Assert.True(MotionDetector.ThresholdPercent(100) < MotionDetector.ThresholdPercent(1));
        Assert.Equal(0, MotionDetector.ChangedPercent(still, new byte[5]));
    }

    [Fact]
    public void Ptz_requests_follow_each_brand_and_never_for_others()
    {
        var dahua = new Camera { Brand = "dahua", Kind = "snapshot", Url = "http://10.0.0.5/cgi-bin/snapshot.cgi?channel=1", Channel = 1 };
        Assert.Equal("http://10.0.0.5/cgi-bin/ptz.cgi?action=start&channel=0&code=Left&arg1=0&arg2=3&arg3=0", CameraPtz.Build(dahua, "left")!.Uri.AbsoluteUri);
        Assert.Contains("action=stop", CameraPtz.Build(dahua, "stop")!.Uri.Query);
        var hik = new Camera { Brand = "hikvision", Kind = "snapshot", Url = "http://10.0.0.6/ISAPI/Streaming/channels/101/picture" };
        var req = CameraPtz.Build(hik, "right")!;
        Assert.Equal(HttpMethod.Put, req.Method); Assert.Contains("<pan>60</pan>", req.Body);
        var axis = new Camera { Brand = "axis", Kind = "snapshot", Url = "http://10.0.0.7/axis-cgi/jpg/image.cgi" };
        Assert.Contains("continuouspantiltmove=-40,0", CameraPtz.Build(axis, "left")!.Uri.Query);
        Assert.Null(CameraPtz.Build(new Camera { Brand = "generic", Kind = "snapshot", Url = "http://10.0.0.8/snapshot.jpg" }, "left"));
        Assert.Null(CameraPtz.Build(dahua, "explode"));
    }

    [Fact]
    public void Scanner_only_touches_private_addresses_and_finds_open_ports()
    {
        Assert.Equal(253, CameraScanner.Subnet24Hosts("192.168.1.10").Count);
        Assert.DoesNotContain("192.168.1.10", CameraScanner.Subnet24Hosts("192.168.1.10"));
        Assert.Empty(CameraScanner.Subnet24Hosts("8.8.8.8"));
        Assert.True(CameraScanner.IsPrivate(IPAddress.Parse("10.1.2.3"))); Assert.False(CameraScanner.IsPrivate(IPAddress.Parse("93.184.216.34")));
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var found = CameraScanner.ScanAsync(new[] { "127.0.0.1", "93.184.216.34" }, new[] { port, 9 }).GetAwaiter().GetResult();
        var hit = Assert.Single(found);                                  // the public address was never contacted
        Assert.Equal("127.0.0.1", hit.Host); Assert.Equal(new[] { port }, hit.OpenPorts);
    }

    [Fact]
    public async Task Recordings_are_saved_listed_purged_and_cannot_escape_their_folder()
    {
        using var env = await TestEnv.CreateAsync();
        var store = new RecordingStore(env.Paths);
        var events = new CameraEvents(new Repository(env.Factory), store);
        var jpg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3 };
        var rel = await store.SaveAsync(7, jpg, new DateTime(2026, 6, 10, 14, 3, 9));
        Assert.True(File.Exists(store.PathOf(rel)));
        var fresh = await events.AddAsync(7, "motion", 4.2, rel);
        var old = await events.AddAsync(7, "motion", 3, await store.SaveAsync(7, jpg, new DateTime(2026, 5, 1, 1, 2, 3)), atUtc: new DateTime(2026, 5, 1, 1, 2, 3, DateTimeKind.Utc));
        Assert.Equal(2, (await events.ListAsync(7)).Count);
        Assert.Equal(1, await events.PurgeAsync(7, 14, new DateTime(2026, 6, 10, 12, 0, 0, DateTimeKind.Utc)));
        Assert.Single(await events.ListAsync(7));
        Assert.False(File.Exists(store.PathOf(old.File)));
        Assert.True(File.Exists(store.PathOf(fresh.File)));
        Assert.Null(store.PathOf("..\\..\\outside.txt"));
        Assert.True(store.TotalBytes() >= jpg.Length);
    }
}
