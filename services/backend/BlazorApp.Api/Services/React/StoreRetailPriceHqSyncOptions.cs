namespace BlazorApp.Api.Services.React
{
    /// <summary>
    /// 分店零售价 HQ 同步参数。
    /// 大批量数据默认使用 keyset 分页，避免 Skip/Take 在千万级数据上退化。
    /// </summary>
    public class StoreRetailPriceHqSyncOptions
    {
        public int HqReadBatchSize { get; set; } = 50000;
        public int WriteBatchSize { get; set; } = 5000;
        public int CommandTimeoutSeconds { get; set; } = 1800;
        public int DefaultIncrementalDays { get; set; } = 30;
    }
}
