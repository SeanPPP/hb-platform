using System.Net.Http.Json;
using System.Text.Json;
using Hbpos.Contracts.RemoteMaintenance;
using Microsoft.Extensions.Options;

namespace Hbpos.Api.Services;

public sealed class RemoteMaintenanceGatewayOptions
{
    public const string SectionName = "RemoteMaintenance";
    public bool Enabled { get; set; }
    public string CenterBaseUrl { get; set; } = string.Empty;
    public string InterApiKey { get; set; } = string.Empty;
}

public sealed record RemoteMaintenanceGatewayResult<T>(bool Success, T? Data, string? Code, string? Message, int StatusCode = 0)
{
    public static RemoteMaintenanceGatewayResult<T> Ok(T data) => new(true, data, null, null, 200);
    public static RemoteMaintenanceGatewayResult<T> Fail(string code, string message, int statusCode = 0) => new(false, default, code, message, statusCode);
}

public interface IRemoteMaintenanceGateway
{
    Task<RemoteMaintenanceGatewayResult<RemoteMaintenancePrepareResponse>> PrepareAsync(
        string hardwareId, RemoteMaintenancePrepareRequest request, CancellationToken cancellationToken);
    Task<RemoteMaintenanceGatewayResult<RemoteMaintenanceCommitResponse>> CommitAsync(
        string hardwareId, RemoteMaintenanceCommitRequest request, CancellationToken cancellationToken);
    Task<RemoteMaintenanceGatewayResult<Stream>> DownloadArtifactAsync(
        string hardwareId, string kind, CancellationToken cancellationToken);
}

public sealed class RemoteMaintenanceGateway(
    HttpClient httpClient,
    IOptions<RemoteMaintenanceGatewayOptions> options,
    ILogger<RemoteMaintenanceGateway> logger) : IRemoteMaintenanceGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<RemoteMaintenanceGatewayResult<RemoteMaintenancePrepareResponse>> PrepareAsync(
        string hardwareId, RemoteMaintenancePrepareRequest request, CancellationToken cancellationToken) =>
        SendAsync<RemoteMaintenancePrepareResponse>(hardwareId, "prepare", request, cancellationToken);

    public Task<RemoteMaintenanceGatewayResult<RemoteMaintenanceCommitResponse>> CommitAsync(
        string hardwareId, RemoteMaintenanceCommitRequest request, CancellationToken cancellationToken) =>
        SendAsync<RemoteMaintenanceCommitResponse>(hardwareId, "commit", request, cancellationToken);

    public async Task<RemoteMaintenanceGatewayResult<Stream>> DownloadArtifactAsync(
        string hardwareId, string kind, CancellationToken cancellationToken)
    {
        var configuration = options.Value;
        if (!configuration.Enabled || string.IsNullOrWhiteSpace(configuration.CenterBaseUrl) || string.IsNullOrWhiteSpace(configuration.InterApiKey))
            return RemoteMaintenanceGatewayResult<Stream>.Fail("REMOTE_MAINTENANCE_DISABLED", "远程维护功能未配置", 503);
        if (kind is not ("rustdesk" or "status-agent"))
            return RemoteMaintenanceGatewayResult<Stream>.Fail("REMOTE_MAINTENANCE_ARTIFACT_KIND_INVALID", "artifact 类型无效", 400);
        if (!Uri.TryCreate(configuration.CenterBaseUrl, UriKind.Absolute, out var baseUri))
            return RemoteMaintenanceGatewayResult<Stream>.Fail("REMOTE_MAINTENANCE_GATEWAY_INVALID", "远程维护中心地址无效", 503);
        HttpResponseMessage? response = null;
        using var message = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, "api/remote-maintenance/artifacts/" + kind));
        message.Headers.TryAddWithoutValidation("X-HBPOS-Remote-Maintenance-Key", configuration.InterApiKey);
        message.Headers.TryAddWithoutValidation("X-HBPOS-Hardware-Id", hardwareId.Trim());
        try
        {
            response = await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var code = await TryReadCodeAsync(response, cancellationToken);
                response.Dispose();
                return RemoteMaintenanceGatewayResult<Stream>.Fail(code ?? "REMOTE_MAINTENANCE_GATEWAY_REJECTED", "远程维护文件下载失败", (int)response.StatusCode);
            }
            var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return RemoteMaintenanceGatewayResult<Stream>.Ok(new ResponseOwnedStream(stream, response));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            response?.Dispose();
            logger.LogWarning(ex, "Remote maintenance gateway artifact download failed {Kind}", kind);
            return RemoteMaintenanceGatewayResult<Stream>.Fail("REMOTE_MAINTENANCE_GATEWAY_UNAVAILABLE", "远程维护中心暂不可用", 503);
        }
    }

    private async Task<RemoteMaintenanceGatewayResult<TResponse>> SendAsync<TResponse>(
        string hardwareId, string operation, object request, CancellationToken cancellationToken)
    {
        var configuration = options.Value;
        if (!configuration.Enabled || string.IsNullOrWhiteSpace(configuration.CenterBaseUrl) || string.IsNullOrWhiteSpace(configuration.InterApiKey))
            return RemoteMaintenanceGatewayResult<TResponse>.Fail("REMOTE_MAINTENANCE_DISABLED", "远程维护功能未配置");
        if (!Uri.TryCreate(configuration.CenterBaseUrl, UriKind.Absolute, out var baseUri))
            return RemoteMaintenanceGatewayResult<TResponse>.Fail("REMOTE_MAINTENANCE_GATEWAY_INVALID", "远程维护中心地址无效");
        var endpoint = new Uri(baseUri, "api/remote-maintenance/" + operation);
        using var message = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };
        message.Headers.TryAddWithoutValidation("X-HBPOS-Remote-Maintenance-Key", configuration.InterApiKey);
        message.Headers.TryAddWithoutValidation("X-HBPOS-Hardware-Id", hardwareId.Trim());
        try
        {
            using var response = await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var data = await response.Content.ReadFromJsonAsync<TResponse>(JsonOptions, cancellationToken);
                return data is null
                    ? RemoteMaintenanceGatewayResult<TResponse>.Fail("REMOTE_MAINTENANCE_EMPTY_RESPONSE", "远程维护中心返回为空")
                    : RemoteMaintenanceGatewayResult<TResponse>.Ok(data);
            }
            logger.LogWarning("Remote maintenance gateway rejected {Operation}. StatusCode={StatusCode}", operation, (int)response.StatusCode);
            var code = await TryReadCodeAsync(response, cancellationToken);
            return RemoteMaintenanceGatewayResult<TResponse>.Fail(code ?? "REMOTE_MAINTENANCE_GATEWAY_REJECTED", "远程维护中心拒绝请求", (int)response.StatusCode);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Remote maintenance gateway failed {Operation}", operation);
            return RemoteMaintenanceGatewayResult<TResponse>.Fail("REMOTE_MAINTENANCE_GATEWAY_UNAVAILABLE", "远程维护中心暂不可用", 503);
        }
    }

    private static async Task<string?> TryReadCodeAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode || response.Content.Headers.ContentLength is > 16_384) return null;
        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
            return document.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
        }
        catch (JsonException) { return null; }
    }

    private sealed class ResponseOwnedStream(Stream inner, HttpResponseMessage response) : Stream
    {
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
                response.Dispose();
            }
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await inner.DisposeAsync();
            response.Dispose();
            GC.SuppressFinalize(this);
        }

        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() => inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => inner.ReadAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
