using System.Net;
using GardenHelper.Core.Cameras;

namespace GardenHelper.Tests;

public class CameraTests
{
    private static readonly byte[] Jpeg = { 0xFF, 0xD8, 1, 2, 3, 4, 5, 0xFF, 0xD9 };

    private sealed class Fake(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        public HttpRequestMessage? Last;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) { Last = r; return Task.FromResult(reply(r)); }
    }

    private static CameraClient Client(Fake f) => new(new HttpClient(f));

    [Theory]
    [InlineData("snapshot", "http://192.168.1.50/snap.jpg", CameraUrlProblem.None)]
    [InlineData("mjpeg", "https://cam.local:8080/stream", CameraUrlProblem.None)]
    [InlineData("rtsp", "rtsp://192.168.1.50:554/live", CameraUrlProblem.None)]
    [InlineData("snapshot", "rtsp://192.168.1.50/live", CameraUrlProblem.UnsupportedScheme)]
    [InlineData("snapshot", "file:///c:/secret.jpg", CameraUrlProblem.UnsupportedScheme)]
    [InlineData("snapshot", "ftp://host/x.jpg", CameraUrlProblem.UnsupportedScheme)]
    [InlineData("snapshot", "not a url", CameraUrlProblem.NotAUrl)]
    [InlineData("snapshot", "  ", CameraUrlProblem.Empty)]
    public void Camera_addresses_are_validated(string kind, string url, CameraUrlProblem expected) =>
        Assert.Equal(expected, CameraUrl.Check(kind, url, out _));

    [Fact]
    public void A_complete_jpeg_is_cut_out_of_mjpeg_data_and_partial_data_waits()
    {
        var stream = new byte[] { 45, 45, 98, 0xFF, 0xD8, 9, 8, 7, 0xFF, 0xD9, 13, 10, 0xFF, 0xD8, 1 };
        Assert.Equal(new byte[] { 0xFF, 0xD8, 9, 8, 7, 0xFF, 0xD9 }, CameraClient.ExtractJpeg(stream));
        Assert.Null(CameraClient.ExtractJpeg(new byte[] { 0xFF, 0xD8, 1, 2, 3 }));
        Assert.Null(CameraClient.ExtractJpeg(new byte[] { 1, 2, 3 }));
    }

    [Fact]
    public async Task Snapshot_is_fetched_with_basic_login()
    {
        var fake = new Fake(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Jpeg) });
        var cam = new Camera { Kind = "snapshot", Url = "http://cam/snap.jpg", Username = "pi" };
        var bytes = await Client(fake).GrabFrameAsync(cam, "secret");
        Assert.Equal(Jpeg, bytes);
        Assert.Equal("Basic", fake.Last!.Headers.Authorization!.Scheme);
        Assert.Equal(Convert.ToBase64String("pi:secret"u8.ToArray()), fake.Last.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task Mjpeg_stream_returns_its_first_frame()
    {
        var body = new byte[] { 45, 45, 98 }.Concat(Jpeg).Concat(new byte[] { 13, 10, 45, 45, 98, 0xFF, 0xD8, 1 }).ToArray();
        var fake = new Fake(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
        Assert.Equal(Jpeg, await Client(fake).GrabFrameAsync(new Camera { Kind = "mjpeg", Url = "http://cam/stream" }, null));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "refused the login")]
    [InlineData(HttpStatusCode.NotFound, "HTTP 404")]
    public async Task Camera_errors_are_explained(HttpStatusCode code, string expected)
    {
        var ex = await Assert.ThrowsAsync<CameraException>(() =>
            Client(new Fake(_ => new HttpResponseMessage(code))).GrabFrameAsync(new Camera { Kind = "snapshot", Url = "http://cam/x" }, null));
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public async Task Non_images_rtsp_and_bad_addresses_are_refused_without_a_request()
    {
        var html = new Fake(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>login</html>") });
        await Assert.ThrowsAsync<CameraException>(() => Client(html).GrabFrameAsync(new Camera { Kind = "snapshot", Url = "http://cam/x" }, null));
        var none = new Fake(_ => throw new InvalidOperationException("must not be called"));
        await Assert.ThrowsAsync<CameraException>(() => Client(none).GrabFrameAsync(new Camera { Kind = "rtsp", Url = "rtsp://cam/live" }, null));
        await Assert.ThrowsAsync<CameraException>(() => Client(none).GrabFrameAsync(new Camera { Kind = "snapshot", Url = "file:///c:/x.jpg" }, null));
    }
}
