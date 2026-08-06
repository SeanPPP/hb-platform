using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BlazorApp.Api.Interfaces.React
{
    /// <summary>
    /// 只解析货位到商品编码，不包含用户、门店、购物车或商品展示数据。
    /// </summary>
    public interface IStoreOrderLocationProductLookupService
    {
        /// <summary>
        /// 按完整货位条码或货位编号查询绑定的商品编码。
        /// </summary>
        Task<StoreOrderLocationProductLookupResult?> LookupAsync(
            string locationIdentifier,
            CancellationToken cancellationToken = default
        );
    }

    /// <summary>
    /// 货位解析结果。MatchType 只允许 locationBarcode 或 locationCode。
    /// </summary>
    public sealed class StoreOrderLocationProductLookupResult
    {
        public string MatchType { get; init; } = string.Empty;

        public IReadOnlyList<string> ProductCodes { get; init; } = Array.Empty<string>();
    }
}
