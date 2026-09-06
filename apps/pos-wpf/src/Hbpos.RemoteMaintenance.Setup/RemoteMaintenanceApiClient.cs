using System.Net.Http.Json;
using System.Text.Json;

namespace Hbpos.RemoteMaintenance.Setup;

public sealed class RemoteMaintenanceApiClient(HttpClient httpClient) : IRemoteMaintenanceApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<RemoteMaintenancePrepareResponse> PrepareAsync(
        RemoteMaintenancePrepareRequest request,
        CancellationToken cancellationToken = default) =>
        SendAsync<RemoteMaintenancePrepareResponse>(
            HttpMethod.Post,
            "api/remote-maintenance/prepare",
            request,
            cancellationToken);

    public async Task<Stream> DownloadArtifactAsync(
        string downloadUrl,
        CancellationToken cancellationToken = default)
    {
        var uri = CreateSafeUri(downloadUrl);
        var response = await httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var code = await TryReadCodeAsync(response, cancellationToken);
            response.Dispose();
            throw new RemoteMaintenanceApiException(
                $"远程维护文件下载失败（HTTP {(int)response.StatusCode}）。",
                (int)response.StatusCode,
                code);
        }

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return new ResponseStream(stream, response);
    }

    public Task<RemoteMaintenanceCommitResponse> CommitAsync(
        RemoteMaintenanceCommitRequest request,
        CancellationToken cancellationToken = default) =>
        SendAsync<RemoteMaintenanceCommitResponse>(
            HttpMethod.Post,
            "api/remote-maintenance/commit",
            request,
            cancellationToken);

    private async Task<T> SendAsync<T>(
        HttpMethod method,
        string path,
        object body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(body, options: JsonOptions)
        };
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var code = await TryReadCodeAsync(response, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            // Problem Details 文案可能含敏感输入；异常只保留固定状态描述与稳定 code。
            throw new RemoteMaintenanceApiException(
                $"远程维护请求失败（HTTP {(int)response.StatusCode}）。",
                (int)response.StatusCode,
                code);
        }

        try
        {
            var result = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
            return result ?? throw new RemoteMaintenanceApiException("远程维护返回为空。", (int)response.StatusCode, code);
        }
        catch (JsonException)
        {
            throw new RemoteMaintenanceApiException("远程维护返回格式无效。", (int)response.StatusCode, code);
        }
    }

    private static Uri CreateSafeUri(string value)
    {
        // 下载只允许 POS 设备接口的两个资源；保持相对路径以保留 /pos-api/ 前缀。
        // 禁止绝对 URL，防止设备授权与收银员票据被消息处理器发往第三方站点。
        var path = value?.TrimStart('/');
        if (path is not ("api/remote-maintenance/artifacts/rustdesk" or "api/remote-maintenance/artifacts/status-agent"))
            throw new RemoteMaintenanceApiException("远程维护下载地址不是公司设备接口。", 400);
        return new Uri(path, UriKind.Relative);
    }

    private static async Task<string?> TryReadCodeAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode || response.Content.Headers.ContentLength is > 16_384)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
            if (document.RootElement.TryGetProperty("code", out var code))
            {
                return code.GetString();
            }

            if (document.RootElement.TryGetProperty("extensions", out var extensions) &&
                extensions.TryGetProperty("code", out code))
            {
                return code.GetString();
            }
        }
        catch (JsonException)
        {
            // 错误体不是 JSON 时保持固定异常，不透传服务器原文。
        }

        return null;
    }

    private sealed class ResponseStream(Stream inner, HttpResponseMessage response) : Stream
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
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => inner.ReadAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
