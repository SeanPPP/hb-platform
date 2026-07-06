using System.Diagnostics;
using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.Extensions.Caching.Memory;
using SqlSugar;

namespace BlazorApp.Api.Services.React
{
    public class WarehouseCategoryReactService : IWarehouseCategoryReactService
    {
        internal const string TreeCacheKey = "WarehouseCategoryReactService:Tree";
        private static readonly TimeSpan TreeCacheDuration = TimeSpan.FromMinutes(30);
        private readonly SqlSugarContext _context;
        private readonly IMapper _mapper;
        private readonly ILogger<WarehouseCategoryReactService> _logger;
        private readonly IMemoryCache _cache;

        public WarehouseCategoryReactService(
            SqlSugarContext context,
            IMapper mapper,
            ILogger<WarehouseCategoryReactService> logger,
            IMemoryCache cache
        )
        {
            _context = context;
            _mapper = mapper;
            _logger = logger;
            _cache = cache;
        }

        public async Task<List<WarehouseCategoryDto>> GetTreeAsync()
        {
            if (_cache.TryGetValue<List<WarehouseCategoryDto>>(TreeCacheKey, out var cachedTree))
            {
                return cachedTree ?? new List<WarehouseCategoryDto>();
            }

            var sw = Stopwatch.StartNew();
            var all = await _context
                .WarehouseCategoryDb.AsQueryable()
                .OrderBy(x => x.SortOrder)
                .OrderBy(x => x.CategoryName)
                .ToListAsync();

            var map = new Dictionary<string, WarehouseCategory>(StringComparer.Ordinal);
            foreach (var c in all)
            {
                if (!string.IsNullOrWhiteSpace(c.CategoryGUID) && !map.ContainsKey(c.CategoryGUID))
                {
                    map[c.CategoryGUID] = c;
                }
            }

            foreach (var c in all)
            {
                if (
                    !string.IsNullOrWhiteSpace(c.ParentGUID)
                    && map.TryGetValue(c.ParentGUID, out var p)
                )
                {
                    p.Children.Add(c);
                    c.Parent = p;
                }
            }

            var roots = all.Where(x => string.IsNullOrWhiteSpace(x.ParentGUID)).ToList();
            sw.Stop();
            _logger.LogInformation(
                "WarehouseCategory tree built: total={Total}, roots={Roots}, elapsedMs={Elapsed}",
                all.Count,
                roots.Count,
                sw.ElapsedMilliseconds
            );

            var dtoRoots = _mapper.Map<List<WarehouseCategoryDto>>(roots);
            if (dtoRoots.Count > 0)
            {
                _logger.LogInformation(
                    "First root: {Name} ({Guid}), children={ChildCount}",
                    dtoRoots[0].CategoryName,
                    dtoRoots[0].CategoryGUID,
                    dtoRoots[0].Children?.Count ?? 0
                );
            }
            _cache.Set(
                TreeCacheKey,
                dtoRoots,
                new MemoryCacheEntryOptions().SetAbsoluteExpiration(TreeCacheDuration)
            );
            return dtoRoots;
        }

        private void InvalidateTreeCache()
        {
            InvalidateTreeCache(_cache);
        }

        internal static void InvalidateTreeCache(IMemoryCache cache)
        {
            // 分类树变更后让移动端下次打开筛选拿到新树，HQ 同步服务也复用同一个失效入口。
            cache.Remove(TreeCacheKey);
        }

        public async Task<PagedResult<WarehouseCategoryDto>> GetListAsync(
            WarehouseCategoryFilterDto filter
        )
        {
            var query = _context.WarehouseCategoryDb.AsQueryable();
            if (!string.IsNullOrWhiteSpace(filter.CategoryName))
                query = query.Where(c => c.CategoryName.Contains(filter.CategoryName));
            if (!string.IsNullOrWhiteSpace(filter.ChineseName))
                query = query.Where(c =>
                    c.ChineseName != null && c.ChineseName.Contains(filter.ChineseName)
                );
            if (filter.IsActive.HasValue)
                query = query.Where(c => c.IsActive == filter.IsActive.Value);
            if (!string.IsNullOrWhiteSpace(filter.ParentGUID))
                query = query.Where(c => c.ParentGUID == filter.ParentGUID);

            query = ApplySorting(query, filter.SortBy, filter.SortDescending);

            var total = await query.CountAsync();
            var items = await query
                .Skip((filter.PageNumber - 1) * filter.PageSize)
                .Take(filter.PageSize)
                .ToListAsync();
            return new PagedResult<WarehouseCategoryDto>
            {
                Items = _mapper.Map<List<WarehouseCategoryDto>>(items),
                Total = total,
                Page = filter.PageNumber,
                PageSize = filter.PageSize,
            };
        }

        public async Task<WarehouseCategoryDto> CreateAsync(CreateWarehouseCategoryDto dto)
        {
            var entity = _mapper.Map<WarehouseCategory>(dto);
            entity.CategoryGUID = Guid.NewGuid().ToString();
            entity.CreatedAt = DateTime.UtcNow;
            entity.UpdatedAt = DateTime.UtcNow;
            var result = await _context.WarehouseCategoryDb.AsInsertable(entity).ExecuteCommandAsync();
            if (result <= 0)
                throw new InvalidOperationException("Create failed");
            InvalidateTreeCache();
            return _mapper.Map<WarehouseCategoryDto>(entity);
        }

        public async Task<WarehouseCategoryDto> UpdateAsync(UpdateWarehouseCategoryDto dto)
        {
            var existing = await _context.WarehouseCategoryDb.GetByIdAsync(dto.CategoryGUID);
            if (existing == null)
                throw new KeyNotFoundException(dto.CategoryGUID);
            if (dto.ParentGUID == dto.CategoryGUID)
                throw new InvalidOperationException("Parent cannot be self");
            _mapper.Map(dto, existing);
            existing.UpdatedAt = DateTime.UtcNow;
            var ok = await _context.WarehouseCategoryDb.UpdateAsync(existing);
            if (!ok)
                throw new InvalidOperationException("Update failed");
            InvalidateTreeCache();
            return _mapper.Map<WarehouseCategoryDto>(existing);
        }

        public async Task<bool> DeleteAsync(string categoryGuid)
        {
            if (
                await _context
                    .WarehouseCategoryDb.AsQueryable()
                    .AnyAsync(c => c.ParentGUID == categoryGuid)
            )
                throw new InvalidOperationException("Has children");
            var hasProducts = await _context.Db.Queryable<WarehouseProduct>()
                .InnerJoin<Product>((w, p) => w.ProductCode == p.ProductCode)
                .Where((w, p) => p.WarehouseCategoryGUID == categoryGuid)
                .AnyAsync();
            if (hasProducts)
                throw new InvalidOperationException("Has products");
            var deleted = await _context.WarehouseCategoryDb.DeleteByIdAsync(categoryGuid);
            if (deleted)
            {
                InvalidateTreeCache();
            }
            return deleted;
        }

        public async Task<bool> BatchMoveAsync(BatchMoveCategoriesDto dto)
        {
            var result = await _context.Db.Ado.UseTranAsync(async () =>
            {
                foreach (var id in dto.CategoryGuids)
                {
                    await _context
                        .WarehouseCategoryDb.AsUpdateable()
                        .SetColumns(c => new WarehouseCategory
                        {
                            ParentGUID = dto.NewParentGuid,
                            UpdatedAt = DateTime.UtcNow,
                        })
                        .Where(c => c.CategoryGUID == id)
                        .ExecuteCommandAsync();
                }
            });
            if (!result.IsSuccess)
            {
                _logger.LogError(result.ErrorException, "BatchMove failed");
            }
            else
            {
                InvalidateTreeCache();
            }
            return result.IsSuccess;
        }

        public async Task<int> BatchToggleActiveAsync(BatchToggleActiveDto dto)
        {
            var affected = 0;
            foreach (var id in dto.CategoryGuids)
            {
                affected += await _context
                    .WarehouseCategoryDb.AsUpdateable()
                    .SetColumns(c => new WarehouseCategory
                    {
                        IsActive = dto.IsActive,
                        UpdatedAt = DateTime.UtcNow,
                    })
                    .Where(c => c.CategoryGUID == id)
                    .ExecuteCommandAsync();
            }
            if (affected > 0)
            {
                InvalidateTreeCache();
            }
            return affected;
        }

        public async Task<bool> BatchSortAsync(BatchSortRequestDto dto)
        {
            var result = await _context.Db.Ado.UseTranAsync(async () =>
            {
                foreach (var item in dto.Items)
                {
                    await _context
                        .WarehouseCategoryDb.AsUpdateable()
                        .SetColumns(c => new WarehouseCategory
                        {
                            SortOrder = item.SortOrder,
                            UpdatedAt = DateTime.UtcNow,
                        })
                        .Where(c => c.CategoryGUID == item.CategoryGuid)
                        .ExecuteCommandAsync();
                }
            });
            if (!result.IsSuccess)
            {
                _logger.LogError(result.ErrorException, "BatchSort failed");
            }
            else
            {
                InvalidateTreeCache();
            }
            return result.IsSuccess;
        }

        public async Task<WarehouseProductPagedResultDto> GetProductsByCategoryAsync(
            string categoryGuid,
            WarehouseProductFilterDto filter
        )
        {
            var sw = Stopwatch.StartNew();
            var categoryIds = !string.IsNullOrWhiteSpace(categoryGuid)
                ? GetAllSubCategoryIds(categoryGuid)
                : new List<string>();

            var query = _context
                .Db.Queryable<WarehouseProduct>()
                .LeftJoin<DomesticProduct>(
                    (w, dp) => dp.ProductCode == w.ProductCode && !dp.IsDeleted
                )
                .LeftJoin<ChinaSupplier>(
                    (w, dp, s) => dp.SupplierCode == s.SupplierCode && !s.IsDeleted
                )
                .InnerJoin<Product>((w, dp, s, p) => p.ProductCode == w.ProductCode && !p.IsDeleted)
                .LeftJoin<WarehouseCategory>(
                    (w, dp, s, p, c) => p.WarehouseCategoryGUID == c.CategoryGUID && !c.IsDeleted
                )
                .Where((w, dp, s, p, c) => !w.IsDeleted);

            if (!string.IsNullOrWhiteSpace(categoryGuid))
            {
                query = query.Where(
                    (w, dp, s, p, c) =>
                        p.WarehouseCategoryGUID != null && categoryIds.Contains(p.WarehouseCategoryGUID)
                );
            }

            if (!string.IsNullOrWhiteSpace(filter.ProductName))
            {
                var keyword = filter.ProductName.Trim();
                query = query.Where(
                    (w, dp, s, p, c) => p.ProductName != null && p.ProductName.Contains(keyword)
                );
            }
            if (!string.IsNullOrWhiteSpace(filter.ItemNumber))
            {
                var itemNumber = filter.ItemNumber.Trim();
                query = query.Where(
                    (w, dp, s, p, c) => p.ItemNumber != null && p.ItemNumber.Contains(itemNumber)
                );
            }
            if (!string.IsNullOrWhiteSpace(filter.SupplierCode))
            {
                var supplierCode = filter.SupplierCode.Trim();
                query = query.Where(
                    (w, dp, s, p, c) =>
                        (dp.SupplierCode != null && dp.SupplierCode.Contains(supplierCode))
                        || (s.SupplierCode != null && s.SupplierCode.Contains(supplierCode))
                );
            }
            if (filter.IsActive.HasValue)
            {
                query = query.Where((w, dp, s, p, c) => w.IsActive == filter.IsActive.Value);
            }

            var total = await query.CountAsync();
            var items = await query
                .Select(
                    (w, dp, s, p, c) =>
                        new WarehouseProductListDto
                        {
                            ProductCode = w.ProductCode,
                            LocalSupplierCode = p.LocalSupplierCode,
                            DomesticSupplierCode = dp.SupplierCode,
                            DomesticSupplierName = s.SupplierName,
                            ItemNumber = p.ItemNumber,
                            ProductBarcode = p.Barcode,
                            ProductBaseName = p.ProductName,
                            Volume = w.Volume,
                            ProductType = p.ProductType,
                            PurchasePrice = p.PurchasePrice,
                            RetailPrice = p.RetailPrice,
                            IsAutoPricing = p.IsAutoPricing,
                            ProductImage = p.ProductImage,
                            IsSpecialProduct = p.IsSpecialProduct,
                            ProductCategoryGUID = p.WarehouseCategoryGUID,
                            ProductCategoryName = c.CategoryName,
                            DomesticPrice = w.DomesticPrice,
                            OEMPrice = w.OEMPrice,
                            ImportPrice = w.ImportPrice,
                            StockQuantity = w.StockQuantity,
                            MinOrderQuantity = w.MinOrderQuantity,
                            StockValue = w.StockValue,
                            StockAlertQuantity = w.StockAlertQuantity,
                            IsActive = w.IsActive,
                            CreatedAt = w.CreatedAt,
                            UpdatedAt = w.UpdatedAt ?? w.CreatedAt,
                        }
                )
                .Skip((filter.PageNumber - 1) * filter.PageSize)
                .Take(filter.PageSize)
                .ToListAsync();
            sw.Stop();
            _logger.LogInformation(
                "Warehouse products fetched: categoryGuid={CategoryGuid}, subCategoriesCount={SubCount}, total={Total}, page={Page}, pageSize={PageSize}, elapsedMs={Elapsed}",
                categoryGuid,
                categoryIds.Count > 0 ? categoryIds.Count - 1 : 0,
                total,
                filter.PageNumber,
                filter.PageSize,
                sw.ElapsedMilliseconds
            );
            return new WarehouseProductPagedResultDto
            {
                Items = items,
                Total = total,
                PageNumber = filter.PageNumber,
                PageSize = filter.PageSize,
            };
        }

        private List<string> GetAllSubCategoryIds(string categoryGuid)
        {
            try
            {
                // 一次性查询所有分类以构建树
                var allCategories = _context.WarehouseCategoryDb.AsQueryable().ToList();
                var result = new List<string> { categoryGuid };
                GetSubCategoriesRecursive(categoryGuid, allCategories, result);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get subcategories for {CategoryGuid}", categoryGuid);
                return new List<string> { categoryGuid };
            }
        }

        private void GetSubCategoriesRecursive(string parentGuid, List<WarehouseCategory> allCategories, List<string> result)
        {
            var children = allCategories.Where(c => c.ParentGUID == parentGuid).ToList();
            foreach (var child in children)
            {
                if (!string.IsNullOrEmpty(child.CategoryGUID) && !result.Contains(child.CategoryGUID))
                {
                    result.Add(child.CategoryGUID);
                    GetSubCategoriesRecursive(child.CategoryGUID, allCategories, result);
                }
            }
        }

        public async Task<int> BatchAssignProductsAsync(BatchAssignProductsRequestDto dto)
        {
            var affected = await _context
                .ProductDb.AsUpdateable()
                .SetColumns(p => new Product
                {
                    WarehouseCategoryGUID = dto.CategoryGuid,
                    UpdatedAt = DateTime.UtcNow,
                })
                .Where(p => p.ProductCode != null && dto.ProductCodes.Contains(p.ProductCode))
                .ExecuteCommandAsync();
            return affected;
        }

        public async Task<int> BatchUnassignProductsAsync(BatchUnassignProductsRequestDto dto)
        {
            var affected = await _context
                .ProductDb.AsUpdateable()
                .SetColumns(p => new Product
                {
                    WarehouseCategoryGUID = null,
                    UpdatedAt = DateTime.UtcNow,
                })
                .Where(p => p.ProductCode != null && dto.ProductCodes.Contains(p.ProductCode))
                .ExecuteCommandAsync();
            return affected;
        }

        private ISugarQueryable<WarehouseCategory> ApplySorting(
            ISugarQueryable<WarehouseCategory> query,
            string? sortBy,
            bool sortDescending
        )
        {
            sortBy = sortBy?.ToLower();
            return sortBy switch
            {
                "categoryname" => sortDescending
                    ? query.OrderByDescending(c => c.CategoryName)
                    : query.OrderBy(c => c.CategoryName),
                "chinesename" => sortDescending
                    ? query.OrderByDescending(c => c.ChineseName)
                    : query.OrderBy(c => c.ChineseName),
                "sortorder" => sortDescending
                    ? query.OrderByDescending(c => c.SortOrder)
                    : query.OrderBy(c => c.SortOrder),
                "createdat" => sortDescending
                    ? query.OrderByDescending(c => c.CreatedAt)
                    : query.OrderBy(c => c.CreatedAt),
                _ => sortDescending
                    ? query.OrderByDescending(c => c.CategoryName)
                    : query.OrderBy(c => c.CategoryName),
            };
        }
    }
}
