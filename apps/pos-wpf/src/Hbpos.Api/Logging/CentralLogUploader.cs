using System.Net;
using System.Net.Http.Json;
using BlazorApp.Shared.DTOs;

namespace Hbpos.Api.Logging;

internal interface ICentralLogDelay
{
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

internal sealed class SystemCentralLogDelay : ICentralLogDelay
{
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        return Task.Delay(delay, cancellationToken);
    }
}

// 此分类被远程 provider 排除，上传故障只会进入现有文件日志。
internal sealed class CentralLogUploadDiagnostic;

internal sealed class CentralLogUploader(
    CentralLoggingOptions options,
    CentralLogQueue queue,
    IHttpClientFactory httpClientFactory,
    ILogger<CentralLogUploadDiagnostic> logger,
    ICentralLogDelay delay,
    TimeProvider timeProvider) : BackgroundService
{
    public const string HttpClientName = "HbposCentralLogging";

    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(15),
        TimeSpan.FromSeconds(30)
    ];
    private readonly SemaphoreSlim flushGate = new(1, 1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.IsConfigured)
        {
            return;
        }

        try
        {
            while (await queue.WaitToReadAsync(stoppingToken))
            {
                await FlushOnceAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            // 最外层保护确保后台日志故障不会终止 Host。
            logger.LogWarning("中心日志后台循环已安全退出: {ExceptionType}", exception.GetType().Name);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        queue.StopAccepting();
        using var maximumBudget = new CancellationTokenSource(TimeSpan.FromSeconds(5), timeProvider);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            maximumBudget.Token);
        try
        {
            // 停止正在执行的上传与后续排空共享同一个五秒总预算。
            await base.StopAsync(timeout.Token);
            // 关闭阶段在同一预算内尽量排空多个批次。
            while (!timeout.IsCancellationRequested && await FlushOnceAsync(timeout.Token))
            {
            }
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogWarning("中心日志关闭刷新失败: {ExceptionType}", exception.GetType().Name);
        }
    }

    internal async Task<bool> FlushOnceAsync(CancellationToken cancellationToken)
    {
        if (!options.IsConfigured)
        {
            return false;
        }

        await flushGate.WaitAsync(cancellationToken);
        try
        {
            var batch = queue.TakeBatch(options.BatchSize);
            if (batch.Count == 0)
            {
                return false;
            }

            var retryIndex = 0;
            var requeueOnCancellation = true;
            try
            {
                while (true)
                {
                    try
                    {
                        var attempt = await SendOnceAsync(batch, cancellationToken);
                        if (attempt.Kind == UploadAttemptKind.Success)
                        {
                            requeueOnCancellation = false;
                            return true;
                        }

                        if (attempt.Kind == UploadAttemptKind.AuthenticationFailure)
                        {
                            requeueOnCancellation = false;
                            logger.LogWarning("中心日志鉴权失败，已丢弃批次并暂停五分钟: StatusCode={StatusCode}", (int)attempt.StatusCode);
                            await delay.DelayAsync(TimeSpan.FromMinutes(5), cancellationToken);
                            return true;
                        }

                        if (attempt.Kind == UploadAttemptKind.PermanentFailure)
                        {
                            // 其他 4xx 属于永久失败，只记录安全状态码，禁止输出 key 或日志正文。
                            requeueOnCancellation = false;
                            logger.LogWarning("中心日志请求被永久拒绝，已丢弃批次: StatusCode={StatusCode}", (int)attempt.StatusCode);
                            return true;
                        }

                        if (attempt.ProtocolInvalid)
                        {
                            logger.LogWarning("中心日志响应协议无效，将按瞬时失败退避");
                        }

                        if (retryIndex >= RetryDelays.Length)
                        {
                            queue.Requeue(batch);
                            logger.LogWarning("中心日志瞬时失败达到单轮上限，批次已重新排队: Count={Count}", batch.Count);
                            return true;
                        }

                        await DelayForRetryAsync(retryIndex++, cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (HttpRequestException exception)
                    {
                        logger.LogWarning("中心日志网络失败，将退避重试: {ExceptionType}", exception.GetType().Name);
                        if (retryIndex >= RetryDelays.Length)
                        {
                            queue.Requeue(batch);
                            return true;
                        }

                        await DelayForRetryAsync(retryIndex++, cancellationToken);
                    }
                    catch (TaskCanceledException exception)
                    {
                        logger.LogWarning("中心日志请求超时，将退避重试: {ExceptionType}", exception.GetType().Name);
                        if (retryIndex >= RetryDelays.Length)
                        {
                            queue.Requeue(batch);
                            return true;
                        }

                        await DelayForRetryAsync(retryIndex++, cancellationToken);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                if (requeueOnCancellation)
                {
                    // Execute 被停止令牌打断时先归还批次，随后由关闭刷新在剩余预算内继续发送。
                    queue.Requeue(batch);
                }

                throw;
            }
        }
        finally
        {
            flushGate.Release();
        }
    }

    private HttpRequestMessage CreateRequest(IReadOnlyList<ApplicationLogIngestItemDto> batch)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, options.IngestUri)
        {
            Content = JsonContent.Create(new CentralLogIngestWireRequest
            {
                Logs = batch.Select(CentralLogIngestWireItem.From).ToList()
            })
        };
        request.Headers.TryAddWithoutValidation("X-Log-Project", options.ProjectCode);
        request.Headers.TryAddWithoutValidation("X-Log-Key", options.ApiKey);
        return request;
    }

    private async Task<UploadAttemptResult> SendOnceAsync(
        IReadOnlyList<ApplicationLogIngestItemDto> batch,
        CancellationToken cancellationToken)
    {
        using var httpClient = httpClientFactory.CreateClient(HttpClientName);
        using var request = CreateRequest(batch);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            var success = await IsSuccessfulProtocolResponseAsync(response, cancellationToken);
            return success
                ? new UploadAttemptResult(UploadAttemptKind.Success, response.StatusCode)
                : new UploadAttemptResult(UploadAttemptKind.TransientFailure, response.StatusCode, ProtocolInvalid: true);
        }

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return new UploadAttemptResult(UploadAttemptKind.AuthenticationFailure, response.StatusCode);
        }

        if (response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
        {
            return new UploadAttemptResult(UploadAttemptKind.TransientFailure, response.StatusCode);
        }

        return new UploadAttemptResult(UploadAttemptKind.PermanentFailure, response.StatusCode);
    }

    private Task DelayForRetryAsync(int retryIndex, CancellationToken cancellationToken)
    {
        return delay.DelayAsync(RetryDelays[Math.Min(retryIndex, RetryDelays.Length - 1)], cancellationToken);
    }

    private async Task<bool> IsSuccessfulProtocolResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<ApplicationLogIngestResultDto>>(
                cancellationToken);
            if (envelope is not { Success: true, Data: not null })
            {
                return false;
            }

            // 只记录聚合计数，不输出逐项错误、事件正文或任何鉴权信息。
            logger.LogInformation(
                "中心日志批次响应: Accepted={Accepted} Rejected={Rejected} Duplicate={Duplicate}",
                envelope.Data.AcceptedCount,
                envelope.Data.RejectedCount,
                envelope.Data.DuplicateCount);
            return true;
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    private enum UploadAttemptKind
    {
        Success,
        AuthenticationFailure,
        TransientFailure,
        PermanentFailure
    }

    private readonly record struct UploadAttemptResult(
        UploadAttemptKind Kind,
        HttpStatusCode StatusCode,
        bool ProtocolInvalid = false);
}
