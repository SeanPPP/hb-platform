using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BlazorApp.Api.Controllers;
using BlazorApp.Api.Services.RustDeskCompat;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class RustDeskCompatControllerTests
{
    [Fact]
    public async Task Login_returns_rustdesk_access_token_contract_without_echoing_password()
    {
        var service = new FakeRustDeskCompatService();
        await using var host = await RustDeskTestHost.StartAsync(service);

        using var response = await host.Client.PostAsJsonAsync(
            "/api/rustdesk/api/login",
            new
            {
                username = "admin",
                password = "secret-value",
                id = "ios-1",
                uuid = "uuid-1",
                autoLogin = true,
                type = "account",
                deviceInfo = new { os = "ios", type = "client", name = "iPhone" },
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("access_token", json.GetProperty("type").GetString());
        Assert.Equal("rrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrr", json.GetProperty("access_token").GetString());
        Assert.Equal("admin", json.GetProperty("user").GetProperty("name").GetString());
        Assert.DoesNotContain("secret-value", await response.Content.ReadAsStringAsync());
        Assert.Equal("account", service.LastLoginRequest?.Type);
    }

    [Fact]
    public async Task Login_rate_limit_rejects_the_eleventh_request_with_generic_json()
    {
        await using var host = await RustDeskTestHost.StartAsync(new FakeRustDeskCompatService());

        for (var i = 0; i < 10; i++)
        {
            using var allowed = await host.Client.PostAsJsonAsync(
                "/api/rustdesk/api/login",
                new { username = "admin", password = "secret", id = $"ios-{i}", uuid = $"uuid-{i}" });
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        using var rejected = await host.Client.PostAsJsonAsync(
            "/api/rustdesk/api/login",
            new { username = "admin", password = "secret", id = "ios-11", uuid = "uuid-11" });
        var rejectedText = await rejected.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.Equal("no-store", rejected.Headers.CacheControl?.ToString());
        Assert.True(JsonDocument.Parse(rejectedText).RootElement.TryGetProperty("error", out _));
        Assert.DoesNotContain("secret", rejectedText);
    }

    [Fact]
    public async Task Current_user_requires_rustdesk_bearer_token()
    {
        await using var host = await RustDeskTestHost.StartAsync(new FakeRustDeskCompatService());

        using var response = await host.Client.PostAsJsonAsync(
            "/api/rustdesk/api/currentUser", new { id = "ios-1", uuid = "uuid-1" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Invalid token", (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("error").GetString());
    }

    [Fact]
    public async Task Current_user_rejects_a_regular_hb_cookie_or_jwt_token()
    {
        var service = new FakeRustDeskCompatService();
        await using var host = await RustDeskTestHost.StartAsync(service);
        host.Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "ordinary-hb-token");
        host.Client.DefaultRequestHeaders.Add("Cookie", "HBAuth=ordinary-hb-cookie");

        using var response = await host.Client.PostAsJsonAsync(
            "/api/rustdesk/api/currentUser", new { id = "ios-1", uuid = "uuid-1" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(1, service.AuthenticateCalls);
    }

    [Theory]
    [InlineData("/api/rustdesk/api/ab")]
    [InlineData("/api/rustdesk/api/ab/peer/add/hb-company-devices")]
    [InlineData("/api/rustdesk/api/ab/unknown")]
    public async Task Address_book_mutations_are_read_only(string path)
    {
        await using var host = await RustDeskTestHost.StartAsync(new FakeRustDeskCompatService());
        host.Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "rrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrr");

        using var response = await host.Client.PostAsync(path, content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Address book is read-only", (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("error").GetString());
    }

    [Fact]
    public async Task Peers_are_paged_and_never_include_password_or_hash()
    {
        var service = new FakeRustDeskCompatService
        {
            Peers = new[]
            {
                new RustDeskPeer("1", "", "", "Windows", "One", []),
                new RustDeskPeer("2", "", "", "Windows", "Two", []),
                new RustDeskPeer("3", "", "", "Windows", "Three", []),
            },
        };
        await using var host = await RustDeskTestHost.StartAsync(service);
        host.Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "rrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrr");

        using var response = await host.Client.PostAsync(
            "/api/rustdesk/api/ab/peers?ab=hb-company-devices&current=2&pageSize=2", null);
        var text = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(text).RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, json.GetProperty("total").GetInt32());
        Assert.Single(json.GetProperty("data").EnumerateArray());
        Assert.Equal("3", json.GetProperty("data")[0].GetProperty("id").GetString());
        Assert.DoesNotContain("password", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hash", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Service_not_ready_is_a_generic_503()
    {
        var service = new FakeRustDeskCompatService { ThrowNotReady = true };
        await using var host = await RustDeskTestHost.StartAsync(service);
        host.Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "rrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrr");

        using var response = await host.Client.PostAsync(
            "/api/rustdesk/api/ab/peers?ab=hb-company-devices", null);
        var text = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("Service unavailable", text);
        Assert.DoesNotContain("database secret", text);
    }

    [Fact]
    public async Task Heartbeat_and_sysinfo_are_authenticated_noop_compatibility_endpoints()
    {
        await using var host = await RustDeskTestHost.StartAsync(new FakeRustDeskCompatService());
        host.Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "rrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrr");

        using var heartbeat = await host.Client.PostAsync("/api/rustdesk/api/heartbeat", null);
        using var sysinfo = await host.Client.PostAsync("/api/rustdesk/api/sysinfo", null);

        Assert.Equal(HttpStatusCode.OK, heartbeat.StatusCode);
        Assert.Equal("{}", await heartbeat.Content.ReadAsStringAsync());
        Assert.Equal("SYSINFO_UPDATED", await sysinfo.Content.ReadAsStringAsync());
        Assert.Empty(((FakeRustDeskCompatService)host.Service).HeartbeatWrites);
    }

    [Fact]
    public async Task RealJwtPipelineKeepsHbAndRustDeskCredentialsSeparate()
    {
        await using var host = await RustDeskTestHost.StartAsync(new FakeRustDeskCompatService());
        host.Client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "rrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrr");
        Assert.Equal(HttpStatusCode.OK, (await host.Client.PostAsync("/api/rustdesk/api/currentUser", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.GetAsync("/test/hb-protected")).StatusCode);
        var jwt = new JwtSecurityToken("HBTest", "HBTest", expires: DateTime.UtcNow.AddMinutes(5), signingCredentials:
            new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes("RustDesk-Tests-Only-Hmac-Key-At-Least-32-Bytes")), SecurityAlgorithms.HmacSha256));
        var token = new JwtSecurityTokenHandler().WriteToken(jwt);
        host.Client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync("/test/hb-protected")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.PostAsync("/api/rustdesk/api/currentUser", null)).StatusCode);
        host.Client.DefaultRequestHeaders.Authorization = null;
        host.Client.DefaultRequestHeaders.Add("Cookie", "access_token=" + token);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync("/test/hb-protected")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.PostAsync("/api/rustdesk/api/currentUser", null)).StatusCode);
    }

    [Fact]
    public async Task OfficialLoginDialogCanLoadEmptyProviderOptionsAnonymously()
    {
        await using var host = await RustDeskTestHost.StartAsync(new FakeRustDeskCompatService());
        using var response = await host.Client.GetAsync("/api/rustdesk/api/login-options");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("[]", await response.Content.ReadAsStringAsync());
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task Group_panel_loads_company_group_and_nested_mac_and_pos_payloads()
    {
        var service = new FakeRustDeskCompatService
        {
            Peers = [
                new RustDeskPeer("100", "sean", "mac-host", "Mac OS", "公司 Mac", []),
                new RustDeskPeer("200", "cashier", "POS-01", "Windows", "", []),
            ],
        };
        await using var host = await RustDeskTestHost.StartAsync(service);
        host.Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "rrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrr");

        using var groups = await host.Client.GetAsync("/api/rustdesk/api/device-group/accessible?current=1&pageSize=100");
        using var users = await host.Client.GetAsync("/api/rustdesk/api/users?current=1&pageSize=100&accessible=&status=1");
        using var peers = await host.Client.GetAsync("/api/rustdesk/api/peers?current=1&pageSize=100&accessible=&status=1");
        Assert.Equal(HttpStatusCode.OK, groups.StatusCode);
        Assert.Equal(HttpStatusCode.OK, users.StatusCode);
        Assert.Equal(HttpStatusCode.OK, peers.StatusCode);
        Assert.True(peers.Headers.CacheControl?.NoStore);
        var groupJson = await groups.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, groupJson.GetProperty("total").GetInt32());
        var groupName = groupJson.GetProperty("data")[0].GetProperty("name").GetString();
        Assert.Equal("公司设备", groupName);
        // 公司设备按组共享，不把当前管理员伪装成每台设备的登录用户。
        var userJson = await users.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, userJson.GetProperty("total").GetInt32());
        Assert.Empty(userJson.GetProperty("data").EnumerateArray());
        var peerText = await peers.Content.ReadAsStringAsync();
        var peerJson = JsonDocument.Parse(peerText).RootElement;
        Assert.Equal(2, peerJson.GetProperty("total").GetInt32());
        var data = peerJson.GetProperty("data");
        Assert.All(data.EnumerateArray(), peer => Assert.Equal(groupName, peer.GetProperty("device_group_name").GetString()));
        Assert.Equal("100", data[0].GetProperty("id").GetString());
        Assert.Equal("macos", data[0].GetProperty("info").GetProperty("os").GetString());
        Assert.Equal("公司 Mac", data[0].GetProperty("info").GetProperty("device_name").GetString());
        Assert.Equal("sean", data[0].GetProperty("info").GetProperty("username").GetString());
        Assert.Equal("windows", data[1].GetProperty("info").GetProperty("os").GetString());
        Assert.Equal("POS-01", data[1].GetProperty("info").GetProperty("device_name").GetString());
        Assert.DoesNotContain("password", peerText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hash", peerText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("access_token", peerText, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("device-group/accessible")]
    [InlineData("users")]
    [InlineData("peers")]
    public async Task Group_endpoints_require_dedicated_bearer_and_reject_invalid_paging(string path)
    {
        await using var host = await RustDeskTestHost.StartAsync(new FakeRustDeskCompatService());
        var url = "/api/rustdesk/api/" + path;
        using var anonymous = await host.Client.GetAsync(url);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "ordinary-hb-token");
        using var wrongToken = await host.Client.GetAsync(url);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongToken.StatusCode);
        host.Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "rrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrr");
        foreach (var query in new[] { "current=0", "pageSize=0", "pageSize=101" })
        {
            using var invalid = await host.Client.GetAsync(url + "?" + query);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        using var lastPage = await host.Client.GetAsync(url + "?current=2147483647&pageSize=100");
        Assert.Equal(HttpStatusCode.OK, lastPage.StatusCode);
        var json = await lastPage.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(path == "users" ? 0 : 1, json.GetProperty("total").GetInt32());
        Assert.Empty(json.GetProperty("data").EnumerateArray());
    }

    [Fact]
    public async Task Group_peers_preserve_total_across_pages()
    {
        var service = new FakeRustDeskCompatService
        {
            Peers = [new RustDeskPeer("1", "", "", "Windows", "One", []), new RustDeskPeer("2", "", "", "Mac OS", "Two", [])],
        };
        await using var host = await RustDeskTestHost.StartAsync(service);
        host.Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "rrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrr");
        using var response = await host.Client.GetAsync("/api/rustdesk/api/peers?current=2&pageSize=1");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, json.GetProperty("total").GetInt32());
        Assert.Single(json.GetProperty("data").EnumerateArray());
        Assert.Equal("2", json.GetProperty("data")[0].GetProperty("id").GetString());
    }

    [Theory]
    [InlineData("device-group/accessible")]
    [InlineData("users")]
    [InlineData("peers")]
    public async Task Group_endpoints_return_generic_json_when_auth_service_is_not_ready(string path)
    {
        await using var host = await RustDeskTestHost.StartAsync(new FakeRustDeskCompatService { ThrowNotReady = true });
        host.Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "rrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrr");
        using var response = await host.Client.GetAsync("/api/rustdesk/api/" + path);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.Equal("Service unavailable", JsonDocument.Parse(text).RootElement.GetProperty("error").GetString());
        Assert.DoesNotContain("database secret", text);
    }

    private sealed class RustDeskTestHost : IAsyncDisposable
    {
        private readonly WebApplication _app;
        public HttpClient Client { get; }
        public IRustDeskCompatService Service { get; }

        private RustDeskTestHost(WebApplication app, HttpClient client, IRustDeskCompatService service)
        {
            _app = app;
            Client = client;
            Service = service;
        }

        public static async Task<RustDeskTestHost> StartAsync(FakeRustDeskCompatService service)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = "Testing",
            });
            builder.WebHost.UseTestServer();
            builder.Services.AddSingleton<IRustDeskCompatService>(service);
            builder.Services.AddControllers()
                .AddApplicationPart(typeof(RustDeskCompatController).Assembly);
            builder.Services.AddRateLimiter(RustDeskLoginRateLimits.Configure);
            builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true, ValidIssuer = "HBTest", ValidateAudience = true, ValidAudience = "HBTest",
                    ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("RustDesk-Tests-Only-Hmac-Key-At-Least-32-Bytes")),
                    ValidateLifetime = true,
                };
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        if (string.IsNullOrWhiteSpace(context.Request.Headers.Authorization)) context.Token = context.Request.Cookies["access_token"];
                        return Task.CompletedTask;
                    },
                };
            });
            builder.Services.AddAuthorization();

            var app = builder.Build();
            app.UseRouting();
            app.UseAuthentication();
            app.UseRateLimiter();
            app.UseAuthorization();
            app.MapControllers();
            app.MapGet("/test/hb-protected", () => Results.Ok()).RequireAuthorization();
            await app.StartAsync();
            return new RustDeskTestHost(app, app.GetTestClient(), service);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.DisposeAsync();
        }
    }

    private sealed class FakeRustDeskCompatService : IRustDeskCompatService
    {
        public RustDeskLoginRequest? LastLoginRequest { get; private set; }
        public int AuthenticateCalls { get; private set; }
        public bool ThrowNotReady { get; init; }
        public IReadOnlyList<RustDeskPeer> Peers { get; init; } =
            new[] { new RustDeskPeer("1", "", "", "Windows", "Company POS", []) };
        public List<string> HeartbeatWrites { get; } = new();

        public Task<RustDeskLoginResult?> LoginAsync(
            RustDeskLoginRequest request,
            string remoteIp,
            CancellationToken cancellationToken)
        {
            LastLoginRequest = request;
            return Task.FromResult<RustDeskLoginResult?>(new RustDeskLoginResult(
                "rrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrr", new RustDeskAuthenticatedUser(
                    "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", request.Username, "RustDesk Admin")));
        }

        public Task<RustDeskAuthenticatedUser?> AuthenticateAsync(
            string token,
            CancellationToken cancellationToken)
        {
            AuthenticateCalls++;
            if (ThrowNotReady)
            {
                throw new InvalidOperationException("database secret");
            }

            return Task.FromResult<RustDeskAuthenticatedUser?>(
                token == "rrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrr"
                    ? new RustDeskAuthenticatedUser("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", "admin", "RustDesk Admin")
                    : null);
        }

        public Task LogoutAsync(string token, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<RustDeskPeer>> GetPeersAsync(
            RustDeskAuthenticatedUser user,
            CancellationToken cancellationToken)
        {
            if (ThrowNotReady)
            {
                throw new InvalidOperationException("database secret");
            }

            return Task.FromResult(Peers);
        }
    }
}
