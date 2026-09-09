using System.Net.Http.Headers;
using Microsoft.Extensions.Options;

namespace Hbpos.Api.Services;

public sealed class AttendanceFaceGatewayOptions
{
    public const string SectionName = "AttendanceFaceGateway";
    public bool Enabled { get; set; }
    public string? CenterBaseUrl { get; set; }
    public string? ServiceToken { get; set; }
}

public sealed record AttendanceFaceDeviceScope(string StoreCode, string DeviceCode, string HardwareId);
public sealed record AttendanceFaceActor(string UserGuid, DateTimeOffset AuthenticatedAtUtc);
public sealed record AttendanceFaceGatewayResponse(int StatusCode, string ContentType, byte[] Body);

public interface IAttendanceFaceGateway
{
    Task<AttendanceFaceGatewayResponse> SendAsync(
        HttpMethod method, string pathAndQuery, AttendanceFaceDeviceScope device,
        AttendanceFaceActor? actor, byte[]? body, string? contentType,
        CancellationToken cancellationToken);
}

/// <summary>设备身份仅来自已认证 claims；服务令牌和管理身份不向客户端返回。</summary>
public sealed class AttendanceFaceGateway(HttpClient httpClient, IOptions<AttendanceFaceGatewayOptions> options)
    : IAttendanceFaceGateway
{
    public const int MaximumBodyBytes = 8 * 1024 * 1024;

    public async Task<AttendanceFaceGatewayResponse> SendAsync(
        HttpMethod method, string pathAndQuery, AttendanceFaceDeviceScope device,
        AttendanceFaceActor? actor, byte[]? body, string? contentType,
        CancellationToken cancellationToken)
    {
        var config = options.Value;
        var token = Environment.GetEnvironmentVariable("HBPOS_ATTENDANCE_FACE_GATEWAY_TOKEN") ?? config.ServiceToken;
        if (!config.Enabled || string.IsNullOrWhiteSpace(token)
            || !Uri.TryCreate(config.CenterBaseUrl, UriKind.Absolute, out var origin)
            || !string.IsNullOrEmpty(origin.UserInfo) || !string.IsNullOrEmpty(origin.Query)
            || !string.IsNullOrEmpty(origin.Fragment)
            || (origin.Scheme != Uri.UriSchemeHttps && !(origin.Scheme == Uri.UriSchemeHttp && origin.IsLoopback)))
            return Error(503, "FACE_GATEWAY_UNAVAILABLE");

        // 路由由控制器构造，不接受客户端提供目标地址或代理路径。
        if (pathAndQuery.StartsWith('/') || pathAndQuery.Contains("..", StringComparison.Ordinal)
            || pathAndQuery.Contains('\\') || body?.Length > MaximumBodyBytes)
            return Error(400, "FACE_GATEWAY_INVALID_REQUEST");

        var baseUri = new Uri(origin.AbsoluteUri.TrimEnd('/') + "/", UriKind.Absolute);
        using var request = new HttpRequestMessage(method,
            new Uri(baseUri, "api/internal/attendance/face/" + pathAndQuery));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
        request.Headers.Add("X-HB-Face-Store", device.StoreCode);
        request.Headers.Add("X-HB-Face-Device", device.DeviceCode);
        request.Headers.Add("X-HB-Face-Hardware", device.HardwareId);
        if (actor is not null)
        {
            request.Headers.Add("X-HB-Face-Actor-User", actor.UserGuid);
            request.Headers.Add("X-HB-Face-Actor-Authenticated-At",
                actor.AuthenticatedAtUtc.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'",
                    System.Globalization.CultureInfo.InvariantCulture));
        }
        if (body is not null)
        {
            request.Content = new ByteArrayContent(body);
            if (!MediaTypeHeaderValue.TryParse(contentType, out var parsedType))
                return Error(415, "FACE_CONTENT_TYPE_INVALID");
            request.Content.Headers.ContentType = parsedType;
        }

        try
        {
            using var response = await httpClient.SendAsync(request,
                HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            // 不跟随中央重定向；避免令牌转发到其他地址，也不把重定向暴露成上传成功。
            if ((int)response.StatusCode is >= 300 and < 400)
                return Error(502, "FACE_GATEWAY_INVALID_RESPONSE");
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var bytes = await ReadBoundedAsync(stream, MaximumBodyBytes, cancellationToken);
            if (bytes is null) return Error(502, "FACE_GATEWAY_INVALID_RESPONSE");
            return new((int)response.StatusCode,
                response.Content.Headers.ContentType?.ToString() ?? "application/json", bytes);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Error(503, "FACE_GATEWAY_TIMEOUT");
        }
        catch (HttpRequestException)
        {
            return Error(503, "FACE_GATEWAY_UNAVAILABLE");
        }
    }

    public static async Task<byte[]?> ReadBoundedAsync(Stream stream, int limit, CancellationToken cancellationToken)
    {
        using var result = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (result.Length + read > limit) return null;
            await result.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        return result.ToArray();
    }

    private static AttendanceFaceGatewayResponse Error(int status, string code) =>
        new(status, "application/json", System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new { code, errorCode = code }));
}
