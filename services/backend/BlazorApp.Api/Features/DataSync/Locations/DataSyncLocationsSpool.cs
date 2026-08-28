using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text.Json;
using BlazorApp.Shared.Models;

namespace BlazorApp.Api.Features.DataSync.Locations;

/// <summary>
/// 全量货位同步的磁盘暂存。只保存持久化字段，避免导航属性意外扩展单批内存占用。
/// </summary>
internal sealed class DataSyncLocationsSpool : IAsyncDisposable
{
    private const int MaximumRecordBytes = 32 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ILogger _logger;
    private readonly string _path;
    private FileStream? _writer;
    private bool _writingCompleted;

    public DataSyncLocationsSpool(ILogger logger)
    {
        _logger = logger;
        _path = Path.Combine(Path.GetTempPath(), $"hb-datasync-locations-{Guid.NewGuid():N}.spool");
        _writer = new FileStream(
            _path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.WriteThrough
        );
    }

    public async Task WriteBatchAsync(IReadOnlyList<Location> locations, CancellationToken cancellationToken)
    {
        if (_writingCompleted || _writer is null)
            throw new InvalidOperationException("货位暂存文件已关闭，不能继续写入");

        var entries = locations.Select(DataSyncLocationSpoolEntry.FromLocation).ToList();
        var payload = JsonSerializer.SerializeToUtf8Bytes(entries, JsonOptions);
        if (payload.Length == 0 || payload.Length > MaximumRecordBytes)
            throw new InvalidOperationException("货位暂存批次大小无效");

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
            throw new InvalidOperationException("货位暂存文件写入器不可用");

        _writer.Flush(flushToDisk: true);
        _writer.Dispose();
        _writer = null;
        _writingCompleted = true;
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<List<Location>> ReadBatchesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        if (!_writingCompleted)
            throw new InvalidOperationException("货位暂存文件尚未完成写入");

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
                throw new InvalidOperationException("货位暂存文件已损坏");

            var payload = new byte[payloadLength];
            await reader.ReadExactlyAsync(payload, cancellationToken);
            var entries = JsonSerializer.Deserialize<List<DataSyncLocationSpoolEntry>>(payload, JsonOptions);
            if (entries is null)
                throw new InvalidOperationException("货位暂存文件包含无效批次");

            yield return entries.Select(entry => entry.ToLocation()).ToList();
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
            if (File.Exists(_path))
                File.Delete(_path);
        }
        catch (Exception cleanupException)
        {
            cleanupFailure = cleanupFailure is null
                ? cleanupException
                : new AggregateException(cleanupFailure, cleanupException);
        }

        if (cleanupFailure is not null)
        {
            // 已提交的同步不能因遗留临时文件被误报失败。
            _logger.LogWarning(cleanupFailure, "DataSync locations spool cleanup failed for {SpoolPath}", _path);
        }

        return ValueTask.CompletedTask;
    }

    private sealed record DataSyncLocationSpoolEntry(
        string LocationGuid,
        int? LocationType,
        string? LocationCode,
        string? LocationBarcode,
        int? Status,
        DateTime CreatedAt,
        string? CreatedBy,
        DateTime? UpdatedAt,
        string? UpdatedBy,
        bool IsDeleted
    )
    {
        public static DataSyncLocationSpoolEntry FromLocation(Location location) => new(
            location.LocationGuid,
            location.LocationType,
            location.LocationCode,
            location.LocationBarcode,
            location.Status,
            location.CreatedAt,
            location.CreatedBy,
            location.UpdatedAt,
            location.UpdatedBy,
            location.IsDeleted
        );

        public Location ToLocation() => new()
        {
            LocationGuid = LocationGuid,
            LocationType = LocationType,
            LocationCode = LocationCode,
            LocationBarcode = LocationBarcode,
            Status = Status,
            CreatedAt = CreatedAt,
            CreatedBy = CreatedBy,
            UpdatedAt = UpdatedAt,
            UpdatedBy = UpdatedBy,
            IsDeleted = IsDeleted,
        };
    }
}
