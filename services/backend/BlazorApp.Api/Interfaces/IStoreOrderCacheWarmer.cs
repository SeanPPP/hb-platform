namespace BlazorApp.Api.Interfaces
{
    /// <summary>
    /// 订货商品列表缓存预热服务接口
    /// </summary>
    public interface IStoreOrderCacheWarmer
    {
        /// <summary>
        /// 预热首页默认数据缓存
        /// </summary>
        Task WarmUpHomePageAsync();

        /// <summary>
        /// 清除所有商品列表缓存
        /// </summary>
        Task ClearCacheAsync();
    }
}




