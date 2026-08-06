using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.Models;

namespace BlazorApp.Api.Services.React
{
    /// <summary>
    /// 货位到商品编码的纯数据解析服务。
    /// </summary>
    public sealed class StoreOrderLocationProductLookupService
        : IStoreOrderLocationProductLookupService
    {
        private const int PickingLocationType = 1;
        private const int StorageLocationType = 2;
        private const int EnabledLocationStatus = 1;

        private readonly SqlSugarContext _context;

        public StoreOrderLocationProductLookupService(SqlSugarContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public async Task<StoreOrderLocationProductLookupResult?> LookupAsync(
            string locationIdentifier,
            CancellationToken cancellationToken = default
        )
        {
            var normalizedIdentifier = locationIdentifier?.Trim();
            if (string.IsNullOrWhiteSpace(normalizedIdentifier))
            {
                return null;
            }

            cancellationToken.ThrowIfCancellationRequested();

            // 条码和编号都必须先命中完整、有效货位；条码命中后不再回退到编号。
            var locationGuids = await FindLocationGuidsAsync(
                normalizedIdentifier,
                matchBarcode: true
            );
            var matchType = "locationBarcode";

            if (locationGuids.Count == 0)
            {
                locationGuids = await FindLocationGuidsAsync(
                    normalizedIdentifier,
                    matchBarcode: false
                );
                matchType = "locationCode";
            }

            if (locationGuids.Count == 0)
            {
                return null;
            }

            cancellationToken.ThrowIfCancellationRequested();

            var productCodes = await _context
                .Db.Queryable<ProductLocation>()
                .Where(pl =>
                    !pl.IsDeleted
                    && pl.LocationGuid != null
                    && locationGuids.Contains(pl.LocationGuid)
                )
                .Select(pl => pl.ProductCode)
                .ToListAsync();

            // 同一个商品可能有多条绑定记录；只返回非空且不重复的商品编码。
            var distinctProductCodes = productCodes
                .Where(productCode => !string.IsNullOrWhiteSpace(productCode))
                .Select(productCode => productCode!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            cancellationToken.ThrowIfCancellationRequested();

            return new StoreOrderLocationProductLookupResult
            {
                MatchType = matchType,
                ProductCodes = distinctProductCodes,
            };
        }

        private async Task<List<string>> FindLocationGuidsAsync(
            string locationIdentifier,
            bool matchBarcode
        )
        {
            var query = _context.Db.Queryable<Location>().Where(location =>
                !location.IsDeleted
                && location.Status == EnabledLocationStatus
                && (
                    location.LocationType == PickingLocationType
                    || location.LocationType == StorageLocationType
                )
            );

            query = matchBarcode
                ? query.Where(location => location.LocationBarcode == locationIdentifier)
                : query.Where(location => location.LocationCode == locationIdentifier);

            return await query.Select(location => location.LocationGuid).ToListAsync();
        }
    }
}
