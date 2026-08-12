using System.Net;
using System.Text;
using System.Text.Json;
using Hbpos.Api.Services;

namespace Hbpos.Api.Tests;

public sealed class LinklyCloudPairingTransportTests
{
    [Fact]
    public async Task PairAsync_posts_once_to_cloudpos_with_the_official_json_shape()
    {
        var handler = new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"secret\":\"paired-secret\"}", Encoding.UTF8, "application/json")
        });
        var transport = new HttpLinklyCloudPairingTransport(new HttpClient(handler));

        var response = await transport.PairAsync(
            "https://auth.example/v1/",
            "merchant-user",
            "merchant-password",
            "123456",
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("paired-secret", response.Secret);
        Assert.Equal(1, handler.Calls);
        Assert.Equal(
            "https://auth.example/v1/pairing/cloudpos",
            handler.LastRequestUri?.AbsoluteUri);
        Assert.Equal(HttpMethod.Post, handler.LastMethod);

        using var document = JsonDocument.Parse(handler.LastBody!);
        Assert.Equal("merchant-user", document.RootElement.GetProperty("username").GetString());
        Assert.Equal("merchant-password", document.RootElement.GetProperty("password").GetString());
        Assert.Equal("123456", document.RootElement.GetProperty("pairCode").GetString());
        Assert.Equal(3, document.RootElement.EnumerateObject().Count());
    }

    [Fact]
    public async Task PairAsync_reads_secret_case_insensitively()
    {
        var handler = new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"SeCrEt\":\"paired-secret\"}", Encoding.UTF8, "application/json")
        });
        var transport = new HttpLinklyCloudPairingTransport(new HttpClient(handler));

        var response = await transport.PairAsync(
            "https://auth.example/v1",
            "merchant-user",
            "merchant-password",
            "123456",
            CancellationToken.None);

        Assert.Equal("paired-secret", response.Secret);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("http://auth.example/v1/")]
    [InlineData("not-a-url")]
    public async Task PairAsync_rejects_non_https_auth_base_url_without_sending(string authBaseUrl)
    {
        var handler = new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var transport = new HttpLinklyCloudPairingTransport(new HttpClient(handler));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            transport.PairAsync(
                authBaseUrl,
                "merchant-user",
                "merchant-password",
                "123456",
                CancellationToken.None));

        Assert.Equal(0, handler.Calls);
    }

    private sealed class CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        public Uri? LastRequestUri { get; private set; }

        public HttpMethod? LastMethod { get; private set; }

        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            LastRequestUri = request.RequestUri;
            LastMethod = request.Method;
            LastBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return responseFactory(request);
        }
    }
}
