using BlazorApp.Api.Data;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HBSalesRecord;
using BlazorApp.Shared.Models.HBweb;
using BlazorApp.Shared.Models.POSM;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BlazorApp.Api.Services;

/// <summary>
/// 销售统计切片共享的运行时依赖。兼容入口只负责创建此上下文，不承载持久化逻辑。
/// </summary>
internal sealed class SalesStatisticsSliceContext
{
    public SalesStatisticsSliceContext(
        POSMSqlSugarContext posmContext,
        SqlSugarContext context,
        ILogger logger,
        IConfiguration configuration,
        IServiceScopeFactory serviceScopeFactory,
        Func<IServiceProvider, ISalesStatisticsRecalculationExecutor> resolveRecalculationExecutor,
        HBSalesRecordSqlSugarContext? hbSalesContext,
        int maxConcurrentUpdates,
        int maxDaysForConcurrentUpdate,
        int maxDaysPerChunk)
    {
        PosmContext = posmContext;
        Context = context;
        Logger = logger;
        Configuration = configuration;
        ServiceScopeFactory = serviceScopeFactory;
        ResolveRecalculationExecutor = resolveRecalculationExecutor;
        HBSalesContext = hbSalesContext;
        MaxConcurrentUpdates = maxConcurrentUpdates;
        MaxDaysForConcurrentUpdate = maxDaysForConcurrentUpdate;
        MaxDaysPerChunk = maxDaysPerChunk;
    }

    public POSMSqlSugarContext PosmContext { get; }
    public SqlSugarContext Context { get; }
    public ILogger Logger { get; }
    public IConfiguration Configuration { get; }
    public IServiceScopeFactory ServiceScopeFactory { get; }
    public Func<IServiceProvider, ISalesStatisticsRecalculationExecutor> ResolveRecalculationExecutor { get; }
    public HBSalesRecordSqlSugarContext? HBSalesContext { get; }
    public int MaxConcurrentUpdates { get; }
    public int MaxDaysForConcurrentUpdate { get; }
    public int MaxDaysPerChunk { get; }
}

/// <summary>
/// 切片基类集中提供只读依赖和 2025 来源选择；不包含 SQL、事务或锁实现。
/// </summary>
internal abstract class SalesStatisticsSliceBase
{
    protected const int BatchSize = 5000;
    protected const string UnknownSupplierCode = "UNKNOWN";
    protected const string UnknownSupplierName = "未匹配供应商";
    protected const int CommandTimeoutSeconds = 1800;
    protected const int HBSalesMainCheckoutDateWindowDays = 7;
    protected const int MaxProductStoreDailyBatchDays = 31;
    protected const int MaxHBSales2025BatchSnapshotRows = 1_250_000;
    protected const string ProvisionalFreshStatus = "ProvisionalFresh";
    protected const int StoreCostProductQueryBatchSize = 500;

    protected SalesStatisticsSliceBase(SalesStatisticsSliceContext shared)
    {
        Shared = shared;
    }

    protected SalesStatisticsSliceContext Shared { get; }
    protected POSMSqlSugarContext _posmContext => Shared.PosmContext;
    protected SqlSugarContext _context => Shared.Context;
    protected ILogger _logger => Shared.Logger;
    protected IConfiguration _configuration => Shared.Configuration;
    protected IServiceScopeFactory _serviceScopeFactory => Shared.ServiceScopeFactory;
    protected HBSalesRecordSqlSugarContext? _hbSalesContext => Shared.HBSalesContext;
    protected int _maxConcurrentUpdates => Shared.MaxConcurrentUpdates;
    protected int _maxDaysForConcurrentUpdate => Shared.MaxDaysForConcurrentUpdate;
    protected int _maxDaysPerChunk => Shared.MaxDaysPerChunk;

    /// <summary>
    /// 仅 2025 年允许读取 HBSales；年份门槛保持原有来源边界，不能由调用方绕过。
    /// </summary>
    protected HBSalesRecordSqlSugarContext? GetHBSalesContextFor2025(DateTime date)
    {
        return date.Year == 2025 ? _hbSalesContext : null;
    }

    protected BatchStatisticsUpdateResult ValidateDateRange(DateTime startDate, DateTime endDate)
    {
        var result = new BatchStatisticsUpdateResult { Success = false };
        if (startDate > endDate)
        {
            result.Message = "开始日期不能大于结束日期";
            _logger.LogWarning(result.Message);
            return result;
        }

        var totalDays = (int)(endDate - startDate).TotalDays + 1;
        if (totalDays > 30)
        {
            result.Message = $"日期范围过大，最多支持 30 天（当前: {totalDays} 天）";
            _logger.LogWarning(result.Message);
            return result;
        }

        result.Success = true;
        return result;
    }

    protected BatchStatisticsUpdateResult ValidateMonthRange(
        DateTime startDate,
        DateTime endDate,
        int maxMonths = 12)
    {
        var result = new BatchStatisticsUpdateResult { Success = false };
        if (startDate > endDate)
        {
            result.Message = "开始日期不能大于结束日期";
            _logger.LogWarning(result.Message);
            return result;
        }

        var totalMonths = ((endDate.Year - startDate.Year) * 12) + endDate.Month - startDate.Month + 1;
        if (totalMonths > maxMonths)
        {
            result.Message = $"月份范围过大，最多支持 {maxMonths} 个月（当前: {totalMonths} 个月）";
            _logger.LogWarning(result.Message);
            return result;
        }

        result.Success = true;
        return result;
    }

    protected void LogSkippedBranchCodeRows<T>(
        string statisticName,
        IEnumerable<T> rows,
        Func<T, string?> branchCodeSelector,
        Func<T, decimal> amountSelector,
        Func<T, decimal> quantitySelector)
    {
        var skippedRows = rows.Where(row => string.IsNullOrWhiteSpace(branchCodeSelector(row))).ToList();
        if (skippedRows.Count == 0)
            return;

        // 各统计切片采用同一缺失分店编码告警口径，避免静默丢弃来源行。
        _logger.LogWarning(
            "{StatisticName} 跳过 {Count} 条缺少分店编码的销售记录，金额合计 {Amount}，数量合计 {Quantity}",
            statisticName, skippedRows.Count, skippedRows.Sum(amountSelector), skippedRows.Sum(quantitySelector));
    }
}
