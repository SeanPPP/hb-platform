using System.Net;
using System.Text;
using Hbpos.Client.Wpf.Services;

namespace Hbpos.Client.Tests;

public sealed class SquareTerminalSetupClientTests
{
    private const string TestAccessToken = "opaque-square-setup-token";
    private static readonly Uri HbposApiBaseAddress = new("http://localhost:5159/");

    [Fact]
    public async Task ListLocationsAsync_UsesProductionEndpointAndParsesLocations()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            capturedRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "success": true,
                      "data": [
                        { "id": "LOC-1", "name": "Main Store", "status": "ACTIVE" },
                        { "id": "LOC-2", "name": "Outlet", "status": "INACTIVE" }
                      ]
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            });
        });

        var client = CreateClient(handler);

        var result = await client.ListLocationsAsync(TestAccessToken, CardTerminalEnvironment.Production);

        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Get, capturedRequest!.Method);
        AssertHbposApiRequest(capturedRequest, "api/v1/square/locations?environment=Production");
        AssertNoSquareHeaders(capturedRequest);
        Assert.Collection(
            result,
            location =>
            {
                Assert.Equal("LOC-1", location.Id);
                Assert.Equal("Main Store", location.Name);
            },
            location =>
            {
                Assert.Equal("LOC-2", location.Id);
                Assert.Equal("Outlet", location.Name);
            });
    }

    [Fact]
    public async Task ListDevicesAsync_UsesSandboxEndpointAndParsesDevices()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            capturedRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "success": true,
                      "data": [
                        {
                          "id": "device:123",
                          "name": "Counter Terminal",
                          "status": "AVAILABLE"
                        },
                        {
                          "id": "device:456",
                          "name": "Spare Terminal",
                          "status": "OFFLINE"
                        }
                      ]
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            });
        });

        var client = CreateClient(handler);

        var result = await client.ListDevicesAsync(TestAccessToken, CardTerminalEnvironment.Sandbox, "LOC/ 01");

        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Get, capturedRequest!.Method);
        AssertHbposApiRequest(capturedRequest, "api/v1/square/devices?environment=Sandbox&locationId=LOC%2F%2001");
        AssertNoSquareHeaders(capturedRequest);
        Assert.Collection(
            result,
            device =>
            {
                Assert.Equal("device:123", device.Id);
                Assert.Equal("Counter Terminal", device.Name);
                Assert.Equal("AVAILABLE", device.Status);
            },
            device =>
            {
                Assert.Equal("device:456", device.Id);
                Assert.Equal("Spare Terminal", device.Name);
                Assert.Equal("OFFLINE", device.Status);
            });
    }

    [Fact]
    public async Task ListLocationsAsync_SanitizesErrorMessages()
    {
        const string accessToken = TestAccessToken;
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                ReasonPhrase = "Unauthorized",
                Content = new StringContent("{ \"errors\": [{ \"detail\": \"bad token " + accessToken + "\" }] }", Encoding.UTF8, "application/json")
            }));

        var client = CreateClient(handler);
        var exception = await Assert.ThrowsAsync<SquareApiException>(() =>
            client.ListLocationsAsync(accessToken, CardTerminalEnvironment.Production));

        Assert.Contains("401", exception.Message);
        Assert.True(
            !exception.Message.Contains(accessToken, StringComparison.Ordinal),
            "Square error messages should not include access tokens");
    }

    [Fact]
    public async Task ListDeviceCodesAsync_UsesProductionEndpointAndParsesDeviceCodes()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            capturedRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "success": true,
                      "data": [
                        {
                          "id": "DC-1",
                          "name": "Counter 2",
                          "code": "ABC123",
                          "status": "UNPAIRED",
                          "locationId": "LOC-1",
                          "deviceId": null
                        }
                      ]
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            });
        });

        var client = CreateClient(handler);

        var result = await client.ListDeviceCodesAsync(TestAccessToken, CardTerminalEnvironment.Production, "LOC-1");

        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Get, capturedRequest!.Method);
        AssertHbposApiRequest(capturedRequest, "api/v1/square/device-codes?environment=Production&locationId=LOC-1");
        AssertNoSquareHeaders(capturedRequest);
        var deviceCode = Assert.Single(result);
        Assert.Equal("DC-1", deviceCode.Id);
        Assert.Equal("Counter 2", deviceCode.Name);
        Assert.Equal("ABC123", deviceCode.Code);
        Assert.Equal("UNPAIRED", deviceCode.Status);
        Assert.Equal("LOC-1", deviceCode.LocationId);
    }

    [Fact]
    public async Task CreateDeviceCodeAsync_PostsExpectedRequestBody()
    {
        HttpRequestMessage? capturedRequest = null;
        string? requestBody = null;
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            capturedRequest = request;
            return CreateResponseAsync();

            async Task<HttpResponseMessage> CreateResponseAsync()
            {
                requestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        {
                          "success": true,
                          "data": {
                            "id": "DC-1",
                            "name": "Counter 2",
                            "code": "ABC123",
                            "status": "UNPAIRED",
                            "locationId": "LOC-1",
                            "deviceId": null
                          }
                        }
                        """,
                        Encoding.UTF8,
                        "application/json")
                };
            }
        });

        var client = CreateClient(handler);

        var result = await client.CreateDeviceCodeAsync(TestAccessToken, CardTerminalEnvironment.Production, "LOC-1", "Counter 2");

        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Post, capturedRequest!.Method);
        AssertHbposApiRequest(capturedRequest, "api/v1/square/device-codes");
        AssertNoSquareHeaders(capturedRequest);
        Assert.NotNull(requestBody);
        Assert.Contains("\"environment\":\"Production\"", requestBody, StringComparison.Ordinal);
        Assert.Contains("\"locationId\":\"LOC-1\"", requestBody, StringComparison.Ordinal);
        Assert.Contains("\"name\":\"Counter 2\"", requestBody, StringComparison.Ordinal);
        Assert.Contains("\"idempotencyKey\":\"", requestBody, StringComparison.Ordinal);
        Assert.Equal("ABC123", result.Code);
    }

    [Fact]
    public async Task GetDeviceCodeAsync_UsesHbposApiAndParsesPairedDeviceId()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            capturedRequest = request;
            return Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "success": true,
                      "data": {
                        "id": "DC-1",
                        "name": "Counter 2",
                        "code": "ABC123",
                        "status": "PAIRED",
                        "locationId": "LOC-1",
                        "deviceId": "DEV-2"
                      }
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            });
        });

        var client = CreateClient(handler);

        var result = await client.GetDeviceCodeAsync(TestAccessToken, CardTerminalEnvironment.Production, "DC-1");

        Assert.NotNull(capturedRequest);
        AssertHbposApiRequest(capturedRequest!, "api/v1/square/device-codes/DC-1?environment=Production");
        AssertNoSquareHeaders(capturedRequest!);
        Assert.Equal("PAIRED", result.Status);
        Assert.Equal("DEV-2", result.DeviceId);
    }

    private static SquareTerminalSetupClient CreateClient(HttpMessageHandler handler)
    {
        return new SquareTerminalSetupClient(new HttpClient(handler)
        {
            BaseAddress = HbposApiBaseAddress
        });
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return await handler(request, cancellationToken);
        }
    }

    private static void AssertHbposApiRequest(HttpRequestMessage request, string relativePathAndQuery)
    {
        Assert.Equal(new Uri(HbposApiBaseAddress, relativePathAndQuery), request.RequestUri);
        var absoluteUri = request.RequestUri!.AbsoluteUri;
        Assert.DoesNotContain("connect.squareup.com", absoluteUri, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("connect.squareupsandbox.com", absoluteUri, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertNoSquareHeaders(HttpRequestMessage request)
    {
        Assert.Null(request.Headers.Authorization);
        Assert.False(request.Headers.Contains("Square-Version"));
    }
}
