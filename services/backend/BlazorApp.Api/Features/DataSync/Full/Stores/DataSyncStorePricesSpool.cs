using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text.Json;
using BlazorApp.Shared.Models;

namespace BlazorApp.Api.Features.DataSync.Full.Stores;

/// <summary>
/// 单个门店零售价来源批次；每个实例的行数由生产者固定限制。
/// </summary>
internal sealed record StorePriceSourceBatch(string StoreCode, List<StoreRetailPrice> Prices);

/// <summary>
/// 将远程读取结果顺序写入临时文件，避免在替换本地 live 数据前占用整店内存。
/// </summary>
internal sealed class DataSyncStorePricesSpool : IAsyncDisposable
{
    private const int MaximumRecordBytes = 128 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ILogger _logger;
    private readonly string _path;
    private FileStream? _writer;
    private bool _writingCompleted;

    public DataSyncStorePricesSpool(ILogger logger)
    {
        _logger = logger;
        // 使用随机且精确的临时文件名，绝不在仓库目录落盘，也不依赖通配符清理。
        _path = Path.Combine(Path.GetTempPath(), $"hb-datasync-store-prices-{Guid.NewGuid():N}.spool");
        _writer = new FileStream(
            _path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.WriteThrough
        );
    }

    public async Task WriteBatchAsync(StorePriceSourceBatch batch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (_writingCompleted || _writer is null)
            throw new InvalidOperationException("门店零售价暂存文件已关闭，不能继续写入");

        var payload = JsonSerializer.SerializeToUtf8Bytes(batch, JsonOptions);
        if (payload.Length == 0 || payload.Length > MaximumRecordBytes)
            throw new InvalidOperationException("门店零售价暂存批次大小无效");

        var lengthPrefix = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(lengthPrefix, payload.Length);
        await _writer.WriteAsync(lengthPrefix, cancellationToken);
        await _writer.WriteAsync(payload, cancellationToken);
    }

    public Task CompleteWritingAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_writingCompleted)
            return Task.CompletedTask;

        if (_writer is null)
            throw new InvalidOperationException("门店零售价暂存文件写入器不可用");

        // 只有强制刷盘完成，才允许后续开启本地替换事务。
        _writer.Flush(flushToDisk: true);
        _writer.Dispose();
        _writer = null;
        _writingCompleted = true;
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<StorePriceSourceBatch> ReadBatchesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        if (!_writingCompleted)
            throw new InvalidOperationException("门店零售价暂存文件尚未完成写入");

        await using var reader = new FileStream(
            _path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan
        );
        var lengthPrefix = new byte[sizeof(int)];
        while (reader.Position < reader.Length)
        {
            await reader.ReadExactlyAsync(lengthPrefix, cancellationToken);
            var payloadLength = BinaryPrimitives.ReadInt32LittleEndian(lengthPrefix);
            if (payloadLength <= 0 || payloadLength > MaximumRecordBytes)
                throw new InvalidOperationException("门店零售价暂存文件已损坏");

            var payload = new byte[payloadLength];
            await reader.ReadExactlyAsync(payload, cancellationToken);
            var batch = JsonSerializer.Deserialize<StorePriceSourceBatch>(payload, JsonOptions);
            if (batch?.Prices is null)
                throw new InvalidOperationException("门店零售价暂存文件包含无效批次");

            yield return batch;
        }
    }

    public ValueTask DisposeAsync()
    {
        Exception? cleanupFailure = null;
        try
        {
            _writer?.Dispose();
        }
        catch (Exception cleanupException)
        {
            cleanupFailure = cleanupException;
        }
        finally
        {
            _writer = null;
        }

        try
        {
            // 清理目标始终是构造时生成的单一随机路径，不使用通配符。
            if (File.Exists(_path))
                File.Delete(_path);
        }
        catch (Exception cleanupException)
        {
            cleanupFailure = cleanupFailure == null
                ? cleanupException
                : new AggregateException(cleanupFailure, cleanupException);
        }

        if (cleanupFailure != null)
        {
            // 数据替换可能已经提交；清理故障只能告警，不能把成功结果反报为失败。
            _logger.LogWarning(
                cleanupFailure,
                "DataSync store price spool cleanup failed for {SpoolPath}",
                _path
            );
        }

        return ValueTask.CompletedTask;
    }
}
