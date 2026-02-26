using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Helper;
using BlazorApp.Shared.Models.HBweb;
using SqlSugar;

namespace BlazorApp.Api.Services.React
{
    public class PromotionReactService : IPromotionReactService
    {
        private readonly SqlSugarContext _context;

        public PromotionReactService(SqlSugarContext context)
        {
            _context = context;
        }

        public async Task<GridResponseDto<PromotionListDto>> GetGridAsync(GridRequestDto request)
        {
            var query = _context.PromotionDb.AsQueryable();

            if (!string.IsNullOrWhiteSpace(request.GlobalSearch))
            {
                var keyword = request.GlobalSearch.Trim();
                query = query.Where(s => s.Name.Contains(keyword));
            }

            if (request.SortModel != null && request.SortModel.Count > 0)
            {
                foreach (var sm in request.SortModel)
                {
                    if (sm.ColId.Equals("name", StringComparison.OrdinalIgnoreCase))
                    {
                        query =
                            sm.Sort == "asc"
                                ? query.OrderBy(s => s.Name)
                                : query.OrderBy(s => s.Name, OrderByType.Desc);
                    }
                    else if (sm.ColId.Equals("priority", StringComparison.OrdinalIgnoreCase))
                    {
                        query =
                            sm.Sort == "asc"
                                ? query.OrderBy(s => s.Priority)
                                : query.OrderBy(s => s.Priority, OrderByType.Desc);
                    }
                    else if (sm.ColId.Equals("effectiveStart", StringComparison.OrdinalIgnoreCase))
                    {
                        query =
                            sm.Sort == "asc"
                                ? query.OrderBy(s => s.EffectiveStart)
                                : query.OrderBy(s => s.EffectiveStart, OrderByType.Desc);
                    }
                }
            }

            var total = await query.CountAsync();
            var items = await query.Skip(request.StartRow).Take(request.PageSize).ToListAsync();
            var ids = items.Select(x => x.Id).ToList();
            var allProducts =
                ids.Count > 0
                    ? await _context
                        .PromotionProductDb.AsQueryable()
                        .Where(p => ids.Contains(p.PromotionId))
                        .ToListAsync()
                    : new List<PromotionProduct>();
            var allStores =
                ids.Count > 0
                    ? await _context
                        .PromotionStoreDb.AsQueryable()
                        .Where(s => ids.Contains(s.PromotionId))
                        .ToListAsync()
                    : new List<PromotionStore>();

            var list = items
                .Select(s => new PromotionListDto
                {
                    Id = s.Id,
                    Name = s.Name,
                    EffectiveStart = s.EffectiveStart,
                    EffectiveEnd = s.EffectiveEnd,
                    IsEnabled = s.IsEnabled,
                    IsExclusive = s.IsExclusive,
                    Priority = s.Priority,
                    ApplyQuantity = s.ApplyQuantity,
                    FixedPrice = s.FixedPrice,
                    ProductsCount = allProducts.Count(p => p.PromotionId == s.Id),
                    StoresCount = allStores.Count(t => t.PromotionId == s.Id),
                })
                .ToList();

            return GridResponseDto<PromotionListDto>.OK(list, total);
        }

        public async Task<ApiResponse<PromotionDetailDto>> GetByIdAsync(string id)
        {
            var s = await _context.PromotionDb.AsQueryable().InSingleAsync(id);
            if (s == null)
            {
                return new ApiResponse<PromotionDetailDto>
                {
                    Success = false,
                    Message = "not found",
                };
            }
            var products = await _context.PromotionProductDb.GetListAsync(d => d.PromotionId == id);
            var stores = await _context.PromotionStoreDb.GetListAsync(t => t.PromotionId == id);
            var dto = new PromotionDetailDto
            {
                Id = s.Id,
                Name = s.Name,
                Description = s.Description,
                EffectiveStart = s.EffectiveStart,
                EffectiveEnd = s.EffectiveEnd,
                IsEnabled = s.IsEnabled,
                IsExclusive = s.IsExclusive,
                Priority = s.Priority,
                ApplyQuantity = s.ApplyQuantity,
                FixedPrice = s.FixedPrice,
                MaxApplicationsPerOrder = s.MaxApplicationsPerOrder,
                Products = (products ?? new List<PromotionProduct>())
                    .Select(p => new PromotionProductItemDto
                    {
                        Id = p.Id,
                        ProductCode = p.ProductCode,
                        UnitWeight = p.UnitWeight,
                    })
                    .ToList(),
                Stores = (stores ?? new List<PromotionStore>())
                    .Select(t => new PromotionStoreItemDto { Id = t.Id, StoreCode = t.StoreCode })
                    .ToList(),
            };
            return ApiResponse<PromotionDetailDto>.OK(dto);
        }

        public async Task<ApiResponse<PromotionDetailDto>> CreateAsync(CreatePromotionDto dto)
        {
            var id = UuidHelper.GenerateUuid7();
            var entity = new Promotion
            {
                Id = id,
                Name = dto.Name,
                Description = dto.Description,
                EffectiveStart = dto.EffectiveStart,
                EffectiveEnd = dto.EffectiveEnd,
                IsEnabled = dto.IsEnabled,
                IsExclusive = dto.IsExclusive,
                Priority = dto.Priority,
                ApplyQuantity = dto.ApplyQuantity,
                FixedPrice = dto.FixedPrice,
                MaxApplicationsPerOrder = dto.MaxApplicationsPerOrder,
            };
            await _context.PromotionDb.InsertAsync(entity);

            if (dto.Products != null && dto.Products.Count > 0)
            {
                var rows = dto
                    .Products.Select(d => new PromotionProduct
                    {
                        Id = UuidHelper.GenerateUuid7(),
                        PromotionId = id,
                        ProductCode = d.ProductCode,
                        UnitWeight = d.UnitWeight,
                    })
                    .ToList();
                await _context.PromotionProductDb.InsertRangeAsync(rows);
            }
            if (dto.Stores != null && dto.Stores.Count > 0)
            {
                var rows = dto
                    .Stores.Select(t => new PromotionStore
                    {
                        Id = UuidHelper.GenerateUuid7(),
                        PromotionId = id,
                        StoreCode = t.StoreCode,
                    })
                    .ToList();
                await _context.PromotionStoreDb.InsertRangeAsync(rows);
            }

            return await GetByIdAsync(id);
        }

        public async Task<ApiResponse<PromotionDetailDto>> UpdateAsync(
            string id,
            UpdatePromotionDto dto
        )
        {
            var s = await _context.PromotionDb.AsQueryable().InSingleAsync(id);
            if (s == null)
            {
                return new ApiResponse<PromotionDetailDto>
                {
                    Success = false,
                    Message = "not found",
                };
            }
            s.Name = dto.Name;
            s.Description = dto.Description;
            s.EffectiveStart = dto.EffectiveStart;
            s.EffectiveEnd = dto.EffectiveEnd;
            s.IsEnabled = dto.IsEnabled;
            s.IsExclusive = dto.IsExclusive;
            s.Priority = dto.Priority;
            s.ApplyQuantity = dto.ApplyQuantity;
            s.FixedPrice = dto.FixedPrice;
            s.MaxApplicationsPerOrder = dto.MaxApplicationsPerOrder;
            await _context.PromotionDb.UpdateAsync(s);

            await _context.PromotionProductDb.DeleteAsync(d => d.PromotionId == id);
            await _context.PromotionStoreDb.DeleteAsync(t => t.PromotionId == id);

            if (dto.Products != null && dto.Products.Count > 0)
            {
                var rows = dto
                    .Products.Select(d => new PromotionProduct
                    {
                        Id = UuidHelper.GenerateUuid7(),
                        PromotionId = id,
                        ProductCode = d.ProductCode,
                        UnitWeight = d.UnitWeight,
                    })
                    .ToList();
                await _context.PromotionProductDb.InsertRangeAsync(rows);
            }

            if (dto.Stores != null && dto.Stores.Count > 0)
            {
                var rows = dto
                    .Stores.Select(t => new PromotionStore
                    {
                        Id = UuidHelper.GenerateUuid7(),
                        PromotionId = id,
                        StoreCode = t.StoreCode,
                    })
                    .ToList();
                await _context.PromotionStoreDb.InsertRangeAsync(rows);
            }

            return await GetByIdAsync(id);
        }

        public async Task<ApiResponse<bool>> DeleteAsync(string id)
        {
            await _context.PromotionProductDb.DeleteAsync(d => d.PromotionId == id);
            await _context.PromotionStoreDb.DeleteAsync(t => t.PromotionId == id);
            var ok = await _context.PromotionDb.DeleteAsync(s => s.Id == id);
            return new ApiResponse<bool>
            {
                Success = ok,
                Message = ok ? "ok" : "failed",
                Data = ok,
            };
        }

        public async Task<ApiResponse<bool>> EnableAsync(string id, bool enable)
        {
            var s = await _context.PromotionDb.AsQueryable().InSingleAsync(id);
            if (s == null)
            {
                return new ApiResponse<bool>
                {
                    Success = false,
                    Message = "not found",
                    Data = false,
                };
            }

            s.IsEnabled = enable;

            if (enable && s.IsExclusive)
            {
                var storeCodes = await _context
                    .PromotionStoreDb.AsQueryable()
                    .Where(x => x.PromotionId == id)
                    .Select(x => x.StoreCode)
                    .ToListAsync();

                if (storeCodes.Count > 0)
                {
                    var nowOverlap = await _context
                        .PromotionDb.AsQueryable()
                        .Where(p => p.IsEnabled && p.IsExclusive && p.Id != id)
                        .Where(p =>
                            p.EffectiveStart <= s.EffectiveEnd && p.EffectiveEnd >= s.EffectiveStart
                        )
                        .Where(p =>
                            SqlFunc
                                .Subqueryable<PromotionStore>()
                                .Where(ps =>
                                    ps.PromotionId == p.Id && storeCodes.Contains(ps.StoreCode)
                                )
                                .Any()
                        )
                        .ToListAsync();

                    if (nowOverlap.Any(x => x.Priority >= s.Priority))
                    {
                        return new ApiResponse<bool>
                        {
                            Success = false,
                            Message = "exclusive_conflict",
                            Data = false,
                        };
                    }
                }
            }

            var ok = await _context.PromotionDb.UpdateAsync(s);
            return new ApiResponse<bool>
            {
                Success = ok,
                Message = ok ? "ok" : "failed",
                Data = ok,
            };
        }

        public async Task<ApiResponse<PromotionEvaluateResponse>> EvaluateAsync(
            PromotionEvaluateRequest req
        )
        {
            var now = DateTime.UtcNow;
            var enabledPromotions = await _context
                .PromotionDb.AsQueryable()
                .Where(p => p.IsEnabled && p.EffectiveStart <= now && p.EffectiveEnd >= now)
                .Where(p =>
                    SqlFunc
                        .Subqueryable<PromotionStore>()
                        .Where(ps => ps.PromotionId == p.Id && ps.StoreCode == req.StoreCode)
                        .Any()
                )
                .ToListAsync();

            if (enabledPromotions.Count == 0)
            {
                return ApiResponse<PromotionEvaluateResponse>.OK(new PromotionEvaluateResponse());
            }

            var productMap = await _context
                .PromotionProductDb.AsQueryable()
                .Where(pp => enabledPromotions.Select(x => x.Id).Contains(pp.PromotionId))
                .ToListAsync();

            var groupByPromotion = productMap
                .GroupBy(x => x.PromotionId)
                .ToDictionary(g => g.Key, g => g.ToList());

            List<Promotion> candidates = enabledPromotions;
            var exclusive = candidates.Any(c => c.IsExclusive);
            if (exclusive)
            {
                candidates = candidates
                    .Where(c => c.IsExclusive)
                    .OrderByDescending(c => c.Priority)
                    .Take(1)
                    .ToList();
            }

            var resp = new PromotionEvaluateResponse();

            foreach (var promo in candidates)
            {
                if (!groupByPromotion.TryGetValue(promo.Id, out var pproducts))
                    continue;

                var setCodes = pproducts.Select(x => x.ProductCode).ToHashSet();
                var weights = pproducts.ToDictionary(x => x.ProductCode, x => x.UnitWeight);

                var itemsInSet = req.Items.Where(i => setCodes.Contains(i.ProductCode)).ToList();
                if (itemsInSet.Count == 0)
                    continue;

                var totalCount = 0;
                var expandedItems =
                    new List<(string productCode, decimal unitPrice, int weight, int qty)>();
                foreach (var item in itemsInSet)
                {
                    var w = weights.TryGetValue(item.ProductCode, out var ww) ? ww : 1;
                    totalCount += item.Qty * w;
                    expandedItems.Add((item.ProductCode, item.UnitPrice, w, item.Qty));
                }

                var bundles = totalCount / promo.ApplyQuantity;
                if (promo.MaxApplicationsPerOrder.HasValue)
                {
                    bundles = Math.Min(bundles, promo.MaxApplicationsPerOrder.Value);
                }

                if (bundles <= 0)
                    continue;

                resp.AppliedPromotions.Add(
                    new AppliedPromotionInfo { PromotionId = promo.Id, AppliedBundles = bundles }
                );

                var remaining = bundles * promo.ApplyQuantity;

                var cartUnits = new List<(string code, decimal price)>();
                foreach (var x in expandedItems)
                {
                    for (int i = 0; i < x.qty * x.weight; i++)
                    {
                        cartUnits.Add((x.productCode, x.unitPrice));
                    }
                }

                int index = 0;
                while (remaining > 0 && index < cartUnits.Count)
                {
                    var take = Math.Min(remaining, promo.ApplyQuantity);
                    var group = cartUnits.Skip(index).Take(take).ToList();
                    var sum = group.Sum(g => g.price);
                    var target = promo.FixedPrice;
                    var discount = sum - target;
                    resp.TotalDiscount += discount;

                    foreach (var g in group)
                    {
                        var ratio = sum == 0 ? 0m : g.price / sum;
                        var newPrice = g.price - (discount * ratio);
                        resp.AdjustedItems.Add(
                            new PriceAdjustmentDto
                            {
                                ProductCode = g.code,
                                QtyAdjusted = 1,
                                AdjustedUnitPrice = Math.Round(
                                    newPrice,
                                    2,
                                    MidpointRounding.AwayFromZero
                                ),
                            }
                        );
                    }

                    index += take;
                    remaining -= take;
                }
            }

            return ApiResponse<PromotionEvaluateResponse>.OK(resp);
        }

        public async Task<ApiResponse<List<PromotionListDto>>> GetValidByStoreAsync(
            string storeCode,
            DateTime? asOf = null
        )
        {
            var now = asOf ?? DateTime.UtcNow;
            var query = _context
                .PromotionDb.AsQueryable()
                .Where(p => p.IsEnabled && p.EffectiveStart <= now && p.EffectiveEnd >= now)
                .Where(p =>
                    SqlFunc
                        .Subqueryable<PromotionStore>()
                        .Where(ps => ps.PromotionId == p.Id && ps.StoreCode == storeCode)
                        .Any()
                );

            var items = await query.ToListAsync();
            var ids = items.Select(x => x.Id).ToList();
            var allProducts =
                ids.Count > 0
                    ? await _context
                        .PromotionProductDb.AsQueryable()
                        .Where(p => ids.Contains(p.PromotionId))
                        .ToListAsync()
                    : new List<PromotionProduct>();
            var allStores =
                ids.Count > 0
                    ? await _context
                        .PromotionStoreDb.AsQueryable()
                        .Where(s => ids.Contains(s.PromotionId))
                        .ToListAsync()
                    : new List<PromotionStore>();

            var list = items
                .Select(s => new PromotionListDto
                {
                    Id = s.Id,
                    Name = s.Name,
                    EffectiveStart = s.EffectiveStart,
                    EffectiveEnd = s.EffectiveEnd,
                    IsEnabled = s.IsEnabled,
                    IsExclusive = s.IsExclusive,
                    Priority = s.Priority,
                    ApplyQuantity = s.ApplyQuantity,
                    FixedPrice = s.FixedPrice,
                    ProductsCount = allProducts.Count(p => p.PromotionId == s.Id),
                    StoresCount = allStores.Count(t => t.PromotionId == s.Id),
                })
                .ToList();

            return ApiResponse<List<PromotionListDto>>.OK(list);
        }

        public async Task<ApiResponse<List<PromotionListDto>>> GetValidByProductAndStoreAsync(
            string productCode,
            string storeCode,
            DateTime? asOf = null
        )
        {
            var now = asOf ?? DateTime.UtcNow;
            var query = _context
                .PromotionDb.AsQueryable()
                .Where(p => p.IsEnabled && p.EffectiveStart <= now && p.EffectiveEnd >= now)
                .Where(p =>
                    SqlFunc
                        .Subqueryable<PromotionStore>()
                        .Where(ps => ps.PromotionId == p.Id && ps.StoreCode == storeCode)
                        .Any()
                )
                .Where(p =>
                    SqlFunc
                        .Subqueryable<PromotionProduct>()
                        .Where(pp => pp.PromotionId == p.Id && pp.ProductCode == productCode)
                        .Any()
                );

            var items = await query.ToListAsync();
            var ids = items.Select(x => x.Id).ToList();
            var allProducts =
                ids.Count > 0
                    ? await _context
                        .PromotionProductDb.AsQueryable()
                        .Where(p => ids.Contains(p.PromotionId))
                        .ToListAsync()
                    : new List<PromotionProduct>();
            var allStores =
                ids.Count > 0
                    ? await _context
                        .PromotionStoreDb.AsQueryable()
                        .Where(s => ids.Contains(s.PromotionId))
                        .ToListAsync()
                    : new List<PromotionStore>();

            var list = items
                .Select(s => new PromotionListDto
                {
                    Id = s.Id,
                    Name = s.Name,
                    EffectiveStart = s.EffectiveStart,
                    EffectiveEnd = s.EffectiveEnd,
                    IsEnabled = s.IsEnabled,
                    IsExclusive = s.IsExclusive,
                    Priority = s.Priority,
                    ApplyQuantity = s.ApplyQuantity,
                    FixedPrice = s.FixedPrice,
                    ProductsCount = allProducts.Count(p => p.PromotionId == s.Id),
                    StoresCount = allStores.Count(t => t.PromotionId == s.Id),
                })
                .ToList();

            return ApiResponse<List<PromotionListDto>>.OK(list);
        }
    }
}
