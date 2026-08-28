using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HqEntities;
using SqlSugar;

namespace BlazorApp.Api.Features.DataSync.Locations;

/// <summary>
/// 只负责从 HQ 分页读取货位源数据，不触碰本地库。
/// </summary>
internal sealed class DataSyncLocationsSourceReader
{
    private readonly HqSqlSugarContext _hqContext;

    public DataSyncLocationsSourceReader(HqSqlSugarContext hqContext)
    {
        _hqContext = hqContext;
    }

    public async IAsyncEnumerable<IReadOnlyList<CPT_DIC_货位编码信息表>> ReadBatchesAsync(
        int batchSize
    )
    {
        _hqContext.CheckConnection();
        var pageNumber = 1;

        while (true)
        {
            var batch = await _hqContext.CPT_DIC_货位编码信息表Db.AsQueryable()
                .Skip((pageNumber - 1) * batchSize)
                .Take(batchSize)
                .ToListAsync();
            if (batch.Count == 0)
                yield break;

            yield return batch;
            pageNumber++;
        }
    }
}

/// <summary>
/// 货位实体映射及本地审计字段规则集中在此，避免编排层混入 AutoMapper 细节。
/// </summary>
internal sealed class DataSyncLocationsEntityMapper
{
    private readonly IMapper _mapper;

    public DataSyncLocationsEntityMapper(IMapper mapper)
    {
        _mapper = mapper;
    }

    public List<Location> MapBatch(
        IReadOnlyList<CPT_DIC_货位编码信息表> sourceBatch,
        DateTime synchronizedAt
    )
    {
        var locations = _mapper.Map<List<Location>>(sourceBatch);
        foreach (var location in locations)
        {
            location.CreatedAt = synchronizedAt;
            location.UpdatedAt = synchronizedAt;
        }

        return locations;
    }
}

/// <summary>
/// 一个完整货位同步命令只打开一次本地事务，任一批失败即回滚全部批次。
/// </summary>
internal sealed class DataSyncLocationsTransactionWriter
{
    private readonly SqlSugarContext _localContext;

    public DataSyncLocationsTransactionWriter(SqlSugarContext localContext)
    {
        _localContext = localContext;
    }

    public async Task WriteAsync(DataSyncLocationsSpool spool)
    {
        await _localContext.Db.Ado.BeginTranAsync();
        try
        {
            // 事务只重放已完成的本地 spool，不覆盖 HQ 读取与 AutoMapper 映射阶段。
            await foreach (var locationBatch in spool.ReadBatchesAsync(CancellationToken.None))
            {
                if (locationBatch.Count == 0)
                    continue;

                await _localContext.Db.Fastest<Location>()
                    .AS("Location")
                    .BulkMergeAsync(locationBatch, new[] { "LocationGuid" });
            }

            await _localContext.Db.Ado.CommitTranAsync();
        }
        catch
        {
            await _localContext.Db.Ado.RollbackTranAsync();
            throw;
        }
    }
}

/// <summary>
/// 统一组装全量货位同步结果，确保错误不会被包装为成功。
/// </summary>
internal static class DataSyncLocationsResultAssembler
{
    public static SyncResult CreateSuccess(DateTime startTime, int processedCount)
    {
        var endTime = DateTime.Now;
        return new SyncResult
        {
            StartTime = startTime,
            EndTime = endTime,
            Duration = endTime - startTime,
            IsSuccess = true,
            AddedCount = 0,
            UpdatedCount = 0,
            ErrorCount = 0,
            Message = $"货位信息同步完成！总共处理: {processedCount}, 新增: 0, 更新: 0, 错误: 0",
        };
    }

    public static SyncResult CreateFailure(DateTime startTime, Exception exception)
    {
        var endTime = DateTime.Now;
        return new SyncResult
        {
            StartTime = startTime,
            EndTime = endTime,
            Duration = endTime - startTime,
            IsSuccess = false,
            ErrorCount = 1,
            Message = $"同步失败: {exception.Message}",
        };
    }
}
