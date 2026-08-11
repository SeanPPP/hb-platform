using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Hbpos.Contracts.Common;
using Hbpos.Contracts.HeldOrders;

namespace Hbpos.Client.Wpf.Services;

/// <summary>
/// 服务端共享挂单 API 错误稳定分类：
/// Disabled=功能未启用；Retryable=网络/5xx/Busy/未知，可安全重试；
/// Conflict=幂等/状态冲突；Forbidden=权限/跨店/认证；Invalid=请求或资源无效。
/// 网络不可用或 disabled 绝不影响本机挂单（调用方自行决定本地兜底）。
/// </summary>
public enum SharedHeldOrderApiErrorKind
{
    Disabled,
    Retryable,
    Conflict,
    Forbidden,
    Invalid
}

/// <summary>
/// API 错误载体。Message 只允许服务端返回的通用文案，绝不含 canonical/购物车 payload。
/// </summary>
public sealed class SharedHeldOrderApiException(
    SharedHeldOrderApiErrorKind kind,
    string message,
    string? errorCode,
    HttpStatusCode statusCode,
    Exception? innerException = null) : Exception(message, innerException)
{
    public SharedHeldOrderApiErrorKind Kind { get; } = kind;

    public string? ErrorCode { get; } = errorCode;

    public HttpStatusCode StatusCode { get; } = statusCode;
}

public interface ISharedHeldOrderApiClient
{
    Task<SharedHeldOrderCapabilitiesResponse> GetCapabilitiesAsync(
        CancellationToken cancellationToken = default);

    Task<SharedHeldOrderPublishResponse> PublishAsync(
        SharedHeldOrderPublishRequest request,
        CancellationToken cancellationToken = default);

    Task<SharedHeldOrderCancelResponse> CancelAsync(
        Guid holdGuid,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SharedHeldOrderListItemDto>> ListPendingAsync(
        CancellationToken cancellationToken = default);

    Task<SharedHeldOrderClaimPrepareResponse> PrepareAsync(
        Guid holdGuid,
        SharedHeldOrderClaimPrepareRequest request,
        CancellationToken cancellationToken = default);

    Task<SharedHeldOrderClaimDto> ActivateAsync(
        Guid holdGuid,
        Guid claimGuid,
        CancellationToken cancellationToken = default);

    Task<SharedHeldOrderClaimDto> ReleaseAsync(
        Guid holdGuid,
        Guid claimGuid,
        CancellationToken cancellationToken = default);

    Task<SharedHeldOrderClaimDto> ForceReleaseAsync(
        Guid holdGuid,
        Guid claimGuid,
        SharedHeldOrderForceReleaseRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>崩溃恢复入口：仅返回本人设备的 claim（Prepared/Active），含解密 payload。</summary>
    Task<IReadOnlyList<SharedHeldOrderRecoveryClaimDto>> ClaimsMineAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 严格解析 ApiResult envelope 的 Http adapter：校验 success/data/errorCode，解析
/// 状态/Guid/revision/时间/summary/payload；任何路径不记录 payload（异常消息只含
/// 服务端通用文案，绝不拼接 canonical/购物车 JSON）。
/// </summary>
public sealed class SharedHeldOrderApiClient(
    HttpClient httpClient,
    ISharedHeldOrderPublicationGate publicationGate) : ISharedHeldOrderApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<SharedHeldOrderCapabilitiesResponse> GetCapabilitiesAsync(
        CancellationToken cancellationToken = default)
    {
        return GetAsync<SharedHeldOrderCapabilitiesResponse>(
            "api/v1/held-orders/capabilities",
            cancellationToken);
    }

    public Task<SharedHeldOrderPublishResponse> PublishAsync(
        SharedHeldOrderPublishRequest request,
        CancellationToken cancellationToken = default)
    {
        return PostAsync<SharedHeldOrderPublishRequest, SharedHeldOrderPublishResponse>(
            "api/v1/held-orders",
            request,
            cancellationToken);
    }

    public Task<SharedHeldOrderCancelResponse> CancelAsync(
        Guid holdGuid,
        CancellationToken cancellationToken = default)
    {
        // 等待正在进行的发布轮次结束，再发送取消；与 iPad 的 pause-and-wait 语义一致。
        return publicationGate.RunExclusiveAsync(
            () => PostAsync<object?, SharedHeldOrderCancelResponse>(
                $"api/v1/held-orders/{holdGuid:D}/cancel",
                null,
                cancellationToken),
            cancellationToken);
    }

    public Task<IReadOnlyList<SharedHeldOrderListItemDto>> ListPendingAsync(
        CancellationToken cancellationToken = default)
    {
        return GetAsync<IReadOnlyList<SharedHeldOrderListItemDto>>(
            "api/v1/held-orders",
            cancellationToken);
    }

    public Task<SharedHeldOrderClaimPrepareResponse> PrepareAsync(
        Guid holdGuid,
        SharedHeldOrderClaimPrepareRequest request,
        CancellationToken cancellationToken = default)
    {
        return PostAsync<SharedHeldOrderClaimPrepareRequest, SharedHeldOrderClaimPrepareResponse>(
            $"api/v1/held-orders/{holdGuid:D}/claims/prepare",
            request,
            cancellationToken);
    }

    public Task<SharedHeldOrderClaimDto> ActivateAsync(
        Guid holdGuid,
        Guid claimGuid,
        CancellationToken cancellationToken = default)
    {
        return PostAsync<object?, SharedHeldOrderClaimDto>(
            $"api/v1/held-orders/{holdGuid:D}/claims/{claimGuid:D}/activate",
            null,
            cancellationToken);
    }

    public Task<SharedHeldOrderClaimDto> ReleaseAsync(
        Guid holdGuid,
        Guid claimGuid,
        CancellationToken cancellationToken = default)
    {
        return PostAsync<object?, SharedHeldOrderClaimDto>(
            $"api/v1/held-orders/{holdGuid:D}/claims/{claimGuid:D}/release",
            null,
            cancellationToken);
    }

    public Task<SharedHeldOrderClaimDto> ForceReleaseAsync(
        Guid holdGuid,
        Guid claimGuid,
        SharedHeldOrderForceReleaseRequest request,
        CancellationToken cancellationToken = default)
    {
        return PostAsync<SharedHeldOrderForceReleaseRequest, SharedHeldOrderClaimDto>(
            $"api/v1/held-orders/{holdGuid:D}/claims/{claimGuid:D}/force-release",
            request,
            cancellationToken);
    }

    public Task<IReadOnlyList<SharedHeldOrderRecoveryClaimDto>> ClaimsMineAsync(
        CancellationToken cancellationToken = default)
    {
        return GetAsync<IReadOnlyList<SharedHeldOrderRecoveryClaimDto>>(
            "api/v1/held-orders/claims/mine",
            cancellationToken);
    }

    private async Task<TResponse> GetAsync<TResponse>(
        string path,
        CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            () => httpClient.GetAsync(path, cancellationToken),
            cancellationToken);
        return await ReadEnvelopeAsync<TResponse>(response, cancellationToken);
    }

    private async Task<TResponse> PostAsync<TRequest, TResponse>(
        string path,
        TRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            () => httpClient.PostAsJsonAsync(path, request, JsonOptions, cancellationToken),
            cancellationToken);
        return await ReadEnvelopeAsync<TResponse>(response, cancellationToken);
    }

    private static async Task<HttpResponseMessage> SendAsync(
        Func<Task<HttpResponseMessage>> send,
        CancellationToken cancellationToken)
    {
        try
        {
            return await send().ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            // 网络不可达/连接失败：稳定分类为 Retryable，调用方本地挂单不受影响。
            throw new SharedHeldOrderApiException(
                SharedHeldOrderApiErrorKind.Retryable,
                "无法连接共享挂单服务，请稍后重试。",
                null,
                HttpStatusCode.ServiceUnavailable,
                exception);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient.Timeout 超时（调用方未取消）：按 Retryable 处理。
            throw new SharedHeldOrderApiException(
                SharedHeldOrderApiErrorKind.Retryable,
                "共享挂单服务响应超时，请稍后重试。",
                null,
                HttpStatusCode.RequestTimeout);
        }
    }

    private static async Task<TResponse> ReadEnvelopeAsync<TResponse>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        ApiResult<TResponse>? result = null;
        try
        {
            result = string.IsNullOrWhiteSpace(content)
                ? null
                : JsonSerializer.Deserialize<ApiResult<TResponse>>(content, JsonOptions);
        }
        catch (JsonException)
        {
            // 响应体不是合法 envelope：按状态码分类，不把 body 拼进消息。
        }

        if (result is null)
        {
            // 无法解析 envelope：2xx 的成功响应视为 Invalid（重试无意义），
            // 5xx/429 按 Retryable，其余按状态码分类。
            throw new SharedHeldOrderApiException(
                (int)response.StatusCode is >= 200 and < 300
                    ? SharedHeldOrderApiErrorKind.Invalid
                    : Classify(response.StatusCode, null),
                $"共享挂单服务返回了无法解析的响应（HTTP {(int)response.StatusCode}）。",
                null,
                response.StatusCode);
        }

        if (!result.Success)
        {
            throw new SharedHeldOrderApiException(
                Classify(response.StatusCode, result.ErrorCode),
                string.IsNullOrWhiteSpace(result.Message)
                    ? $"共享挂单服务请求失败（{result.ErrorCode ?? "unknown"}）。"
                    : result.Message,
                result.ErrorCode,
                response.StatusCode);
        }

        if (result.Data is null)
        {
            throw new SharedHeldOrderApiException(
                SharedHeldOrderApiErrorKind.Invalid,
                "共享挂单服务成功响应缺少 data，拒绝使用。",
                "SHARED_HELD_ORDER_EMPTY_DATA",
                response.StatusCode);
        }

        return result.Data;
    }

    private static SharedHeldOrderApiErrorKind Classify(
        HttpStatusCode statusCode,
        string? errorCode)
    {
        if (string.Equals(
                errorCode,
                "SHARED_HELD_ORDER_DISABLED",
                StringComparison.Ordinal))
        {
            return SharedHeldOrderApiErrorKind.Disabled;
        }

        if (string.Equals(errorCode, "SHARED_HELD_ORDER_BUSY", StringComparison.Ordinal)
            || statusCode == HttpStatusCode.TooManyRequests
            || (int)statusCode >= 500)
        {
            return SharedHeldOrderApiErrorKind.Retryable;
        }

        if (string.Equals(errorCode, "SHARED_HELD_ORDER_MISMATCH", StringComparison.Ordinal)
            || string.Equals(errorCode, "SHARED_HELD_ORDER_CLAIM_EXPIRED", StringComparison.Ordinal)
            || statusCode == HttpStatusCode.Conflict)
        {
            return SharedHeldOrderApiErrorKind.Conflict;
        }

        if (statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
            || string.Equals(errorCode, "SHARED_HELD_ORDER_PERMISSION_DENIED", StringComparison.Ordinal)
            || string.Equals(errorCode, "SHARED_HELD_ORDER_CROSS_STORE", StringComparison.Ordinal)
            || string.Equals(errorCode, "DEVICE_SCOPE_FORBIDDEN", StringComparison.Ordinal)
            || string.Equals(errorCode, "CASHIER_AUTH_REQUIRED", StringComparison.Ordinal))
        {
            return SharedHeldOrderApiErrorKind.Forbidden;
        }

        if (statusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound
            || string.Equals(errorCode, "SHARED_HELD_ORDER_INVALID", StringComparison.Ordinal)
            || string.Equals(errorCode, "SHARED_HELD_ORDER_NOT_FOUND", StringComparison.Ordinal))
        {
            return SharedHeldOrderApiErrorKind.Invalid;
        }

        // 2xx 成功状态却带未知 errorCode/无 errorCode：按 Invalid（envelope 不可信）。
        // 其余未知错误保守按 Retryable 处理，配合发布/对账的幂等重试。
        return (int)statusCode is >= 200 and < 300
            ? SharedHeldOrderApiErrorKind.Invalid
            : SharedHeldOrderApiErrorKind.Retryable;
    }
}
