using BlazorApp.Api.Features.StoreOrders.ProductPicker.Domain;
using BlazorApp.Api.Features.StoreOrders.ProductPicker.Infrastructure;

namespace BlazorApp.Api.Features.StoreOrders.ProductPicker.Application;

internal sealed record WarmUpProductPickerHomePageCommand;

internal sealed class WarmUpProductPickerHomePageValidator
{
    internal ProductPickerHomePageWarmUpInput Validate(
        WarmUpProductPickerHomePageCommand command
    )
    {
        return new ProductPickerHomePageWarmUpInput(
            ProductPickerRules.HomePageWarmUpPageSizes,
            ProductPickerRules.HomePageWarmUpTimeout
        );
    }
}

internal sealed class WarmUpProductPickerHomePageHandler(
    WarmUpProductPickerHomePageValidator validator,
    GetHomePageWarmUpPageHandler lightweightPageHandler,
    GetHomePageCachePageHandler accuratePageHandler,
    ProductPickerHomePageCacheStore cacheStore,
    ILogger<WarmUpProductPickerHomePageHandler> logger
)
{
    private int _isRunning;

    internal async Task HandleAsync(WarmUpProductPickerHomePageCommand command)
    {
        var input = validator.Validate(command);
        if (Interlocked.CompareExchange(ref _isRunning, 1, 0) != 0)
        {
            logger.LogWarning("已有首页商品列表缓存预热正在运行，本次请求跳过");
            return;
        }

        logger.LogInformation("开始预热首页商品列表缓存");
        using var timeoutSource = new CancellationTokenSource(input.Timeout);

        try
        {
            // 缓存命令不写数据库；两条查询均保持无事务，缓存写入也不引入嵌套边界。
            for (var index = 0; index < input.PageSizes.Count; index += 1)
            {
                await WarmUpPageSizeAsync(input.PageSizes[index], timeoutSource.Token);
                if (index < input.PageSizes.Count - 1)
                {
                    timeoutSource.Token.ThrowIfCancellationRequested();
                }
            }
        }
        catch (OperationCanceledException)
        {
            if (timeoutSource.IsCancellationRequested)
            {
                logger.LogWarning(
                    "首页商品列表缓存预热已取消，已在 {TimeoutSeconds} 秒超时边界内结束",
                    (int)input.Timeout.TotalSeconds
                );
            }
            else
            {
                logger.LogWarning("首页商品列表缓存预热已取消");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "预热首页商品列表缓存失败");
            throw;
        }
        finally
        {
            Volatile.Write(ref _isRunning, 0);
        }
    }

    private async Task WarmUpPageSizeAsync(
        int pageSize,
        CancellationToken cancellationToken
    )
    {
        var lightweightResult = await lightweightPageHandler.HandleAsync(
            new GetHomePageWarmUpPageQuery(pageSize),
            cancellationToken
        );
        var warmUpCacheKey = cacheStore.SetLightweightWarmUp(
            pageSize,
            lightweightResult
        );
        logger.LogInformation(
            "首页商品列表轻量预热完成，PageSize={PageSize}，共 {Count} 条商品，缓存键: {CacheKey}",
            pageSize,
            lightweightResult.Items?.Count ?? 0,
            warmUpCacheKey
        );

        var accurateResult = await accuratePageHandler.HandleAsync(
            new GetHomePageCachePageQuery(pageSize),
            cancellationToken
        );
        var homePageCacheKey = cacheStore.SetAccurateHomePage(pageSize, accurateResult);
        logger.LogInformation(
            "首页商品列表正常缓存预热完成，PageSize={PageSize}，共 {Count} 条商品，总数 {Total}，缓存键: {CacheKey}",
            pageSize,
            accurateResult.Items?.Count ?? 0,
            accurateResult.Total,
            homePageCacheKey
        );
    }
}
