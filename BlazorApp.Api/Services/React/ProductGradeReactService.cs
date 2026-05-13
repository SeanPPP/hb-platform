using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Helper;
using BlazorApp.Shared.Models;
using SqlSugar;

namespace BlazorApp.Api.Services.React
{
    public class ProductGradeReactService : IProductGradeReactService
    {
        private readonly SqlSugarContext _context;
        private readonly HqSqlSugarContext _hqContext;
        private readonly IMapper _mapper;
        private readonly ILogger<ProductGradeReactService> _logger;

        public ProductGradeReactService(
            SqlSugarContext context,
            HqSqlSugarContext hqContext,
            IMapper mapper,
            ILogger<ProductGradeReactService> logger
        )
        {
            _context = context;
            _hqContext = hqContext;
            _mapper = mapper;
            _logger = logger;
        }

        public async Task<ApiResponse<PagedResult<ProductGradeDto>>> GetProductGradesAsync(
            ProductGradeListQueryDto query
        )
        {
            try
            {
                var db = _context.Db;

                var gradeQuery = db.Queryable<ProductGrade>()
                    .LeftJoin<DomesticProduct>((g, d) => g.ProductCode == d.ProductCode)
                    .LeftJoin<ChinaSupplier>((g, d, s) => d.SupplierCode == s.SupplierCode)
                    .LeftJoin<WarehouseProduct>((g, d, s, w) => g.ProductCode == w.ProductCode)
                    .LeftJoin<Product>((g, d, s, w, p) => g.ProductCode == p.UUID)
                    .Where((g, d, s, w, p) => !g.IsDeleted);

                if (!string.IsNullOrWhiteSpace(query.Search))
                {
                    gradeQuery = gradeQuery.Where(
                        (g, d, s, w, p) =>
                            (d.HBProductNo != null && d.HBProductNo.Contains(query.Search))
                            || (d.ProductName != null && d.ProductName.Contains(query.Search))
                            || (s.SupplierName != null && s.SupplierName.Contains(query.Search))
                            || d.SupplierCode.Contains(query.Search)
                    );
                }

                if (!string.IsNullOrWhiteSpace(query.Grade))
                {
                    gradeQuery = gradeQuery.Where((g, d, s, w, p) => g.Grade == query.Grade);
                }

                if (!string.IsNullOrWhiteSpace(query.SupplierCode))
                {
                    gradeQuery = gradeQuery.Where(
                        (g, d, s, w, p) => d.SupplierCode == query.SupplierCode
                    );
                }

                if (!string.IsNullOrEmpty(query.SortField))
                {
                    var isDesc = query.SortDirection?.ToLower() == "desc";
                    gradeQuery = query.SortField.ToLower() switch
                    {
                        "grade" => isDesc
                            ? gradeQuery.OrderByDescending((g, d, s, w, p) => g.Grade)
                            : gradeQuery.OrderBy((g, d, s, w, p) => g.Grade),
                        "hbproductno" => isDesc
                            ? gradeQuery.OrderByDescending((g, d, s, w, p) => d.HBProductNo)
                            : gradeQuery.OrderBy((g, d, s, w, p) => d.HBProductNo),
                        "suppliername" => isDesc
                            ? gradeQuery.OrderByDescending((g, d, s, w, p) => s.SupplierName)
                            : gradeQuery.OrderBy((g, d, s, w, p) => s.SupplierName),
                        "domesticprice" => isDesc
                            ? gradeQuery.OrderByDescending((g, d, s, w, p) => d.DomesticPrice)
                            : gradeQuery.OrderBy((g, d, s, w, p) => d.DomesticPrice),
                        "createdat" => isDesc
                            ? gradeQuery.OrderByDescending((g, d, s, w, p) => g.CreatedAt)
                            : gradeQuery.OrderBy((g, d, s, w, p) => g.CreatedAt),
                        _ => gradeQuery.OrderByDescending((g, d, s, w, p) => g.CreatedAt),
                    };
                }
                else
                {
                    gradeQuery = gradeQuery.OrderByDescending((g, d, s, w, p) => g.CreatedAt);
                }

                var totalCount = await gradeQuery.CountAsync();

                var items = await gradeQuery
                    .Select(
                        (g, d, s, w, p) =>
                            new ProductGradeDto
                            {
                                Id = g.Id,
                                ProductCode = g.ProductCode,
                                Grade = g.Grade,
                                SupplierCode = d.SupplierCode,
                                SupplierName = s.SupplierName,
                                HbProductNo = d.HBProductNo,
                                ProductName = d.ProductName,
                                ProductImage = d.ProductImage,
                                DomesticPrice = d.DomesticPrice,
                                ImportPrice = w.ImportPrice,
                                OemPrice = w.OEMPrice,
                                RetailPrice = p.RetailPrice,
                                Barcode = d.Barcode,
                                CreatedAt = g.CreatedAt,
                                UpdatedAt = g.UpdatedAt,
                                CreatedBy = g.CreatedBy,
                                UpdatedBy = g.UpdatedBy,
                            }
                    )
                    .Skip(query.Skip)
                    .Take(query.Take)
                    .ToListAsync();

                var result = new PagedResult<ProductGradeDto>
                {
                    Items = items,
                    Total = totalCount,
                    Page = query.Page,
                    PageSize = query.PageSize,
                };

                return ApiResponse<PagedResult<ProductGradeDto>>.OK(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取商品等级列表失败");
                return ApiResponse<PagedResult<ProductGradeDto>>.Error(
                    "获取商品等级列表失败",
                    "GET_GRADES_ERROR"
                );
            }
        }

        public async Task<ApiResponse<ProductGradeDto>> CreateOrUpdateProductGradeAsync(
            CreateProductGradeDto dto
        )
        {
            try
            {
                var db = _context.Db;

                var existing = await db.Queryable<ProductGrade>()
                    .Where(g => g.ProductCode == dto.ProductCode && !g.IsDeleted)
                    .FirstAsync();

                if (existing != null)
                {
                    existing.Grade = dto.Grade;
                    existing.UpdatedAt = DateTime.UtcNow;
                    existing.UpdatedBy = "System";
                    await db.Updateable(existing).ExecuteCommandAsync();

                    var updateDto = await BuildGradeDto(db, existing);
                    return ApiResponse<ProductGradeDto>.OK(updateDto);
                }

                var entity = new ProductGrade
                {
                    Id = UuidHelper.GenerateUuid7(),
                    ProductCode = dto.ProductCode,
                    Grade = dto.Grade,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    CreatedBy = "System",
                    UpdatedBy = "System",
                };

                await db.Insertable(entity).ExecuteCommandAsync();

                var newDto = await BuildGradeDto(db, entity);
                return ApiResponse<ProductGradeDto>.OK(newDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "创建/更新商品等级失败，ProductCode: {ProductCode}",
                    dto.ProductCode
                );
                return ApiResponse<ProductGradeDto>.Error(
                    "创建/更新商品等级失败",
                    "CREATE_GRADE_ERROR"
                );
            }
        }

        public async Task<ApiResponse<bool>> BatchUpdateGradesAsync(BatchUpdateGradeDto dto)
        {
            try
            {
                var db = _context.Db;
                var productCodes = dto.Items.Select(i => i.ProductCode).Distinct().ToList();
                var gradeLookup = dto.Items.ToDictionary(i => i.ProductCode, i => i.Grade);

                var existingGrades = await db.Queryable<ProductGrade>()
                    .Where(g => productCodes.Contains(g.ProductCode) && !g.IsDeleted)
                    .ToListAsync();

                var existingCodeSet = new HashSet<string>(
                    existingGrades.Select(g => g.ProductCode)
                );
                var now = DateTime.UtcNow;

                foreach (var eg in existingGrades)
                {
                    if (gradeLookup.TryGetValue(eg.ProductCode, out var grade))
                    {
                        eg.Grade = grade;
                        eg.UpdatedAt = now;
                        eg.UpdatedBy = "System";
                    }
                }

                if (existingGrades.Count > 0)
                {
                    await db.Updateable(existingGrades).ExecuteCommandAsync();
                }

                var newCodes = productCodes.Where(c => !existingCodeSet.Contains(c)).ToList();
                if (newCodes.Count > 0)
                {
                    var newEntities = newCodes
                        .Select(c => new ProductGrade
                        {
                            Id = UuidHelper.GenerateUuid7(),
                            ProductCode = c,
                            Grade = gradeLookup[c],
                            CreatedAt = now,
                            UpdatedAt = now,
                            CreatedBy = "System",
                            UpdatedBy = "System",
                        })
                        .ToList();

                    await db.Insertable(newEntities).ExecuteCommandAsync();
                }

                _logger.LogInformation("批量更新商品等级成功，共 {Total} 条", dto.Items.Count);
                return ApiResponse<bool>.OK(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量更新商品等级失败");
                return ApiResponse<bool>.Error("批量更新商品等级失败", "BATCH_UPDATE_GRADE_ERROR");
            }
        }

        public async Task<ApiResponse<PasteImportResultDto>> PasteImportGradesAsync(
            PasteImportGradeDto dto
        )
        {
            try
            {
                var db = _context.Db;

                var productNumbers = dto
                    .ProductNumbers.Split(
                        new[] { '\n', '\r', '\t', ',' },
                        StringSplitOptions.RemoveEmptyEntries
                    )
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Distinct()
                    .ToList();

                if (productNumbers.Count == 0)
                {
                    return ApiResponse<PasteImportResultDto>.Error(
                        "货号列表为空",
                        "EMPTY_PRODUCT_NUMBERS"
                    );
                }

                var matchedProducts = await db.Queryable<DomesticProduct>()
                    .Where(d => d.SupplierCode == dto.SupplierCode && !d.IsDeleted)
                    .Where(d => d.HBProductNo != null && productNumbers.Contains(d.HBProductNo))
                    .ToListAsync();

                var matchedByNo = matchedProducts.ToDictionary(d => d.HBProductNo!);

                var previewItems = productNumbers
                    .Select(no =>
                    {
                        var matched = matchedByNo.TryGetValue(no, out var dp);
                        return new PasteImportPreviewItem
                        {
                            ProductNumber = no,
                            Matched = matched,
                            ProductCode = matched ? dp!.ProductCode : null,
                            ProductName = matched ? dp!.ProductName : null,
                            ProductImage = matched ? dp!.ProductImage : null,
                        };
                    })
                    .ToList();

                var matchedCodes = matchedProducts.Select(d => d.ProductCode).ToList();

                var existingGrades = await db.Queryable<ProductGrade>()
                    .Where(g => matchedCodes.Contains(g.ProductCode) && !g.IsDeleted)
                    .ToListAsync();

                var existingGradeMap = existingGrades.ToDictionary(g => g.ProductCode);

                foreach (var pi in previewItems.Where(p => p.Matched && p.ProductCode != null))
                {
                    if (existingGradeMap.TryGetValue(pi.ProductCode!, out var eg))
                    {
                        pi.ExistingGrade = eg.Grade;
                    }
                }

                var now = DateTime.UtcNow;
                var createdCount = 0;
                var updatedCount = 0;

                foreach (var eg in existingGrades.Where(g => matchedCodes.Contains(g.ProductCode)))
                {
                    eg.Grade = dto.Grade;
                    eg.UpdatedAt = now;
                    eg.UpdatedBy = "System";
                    updatedCount++;
                }

                if (existingGrades.Count > 0)
                {
                    await db.Updateable(existingGrades).ExecuteCommandAsync();
                }

                var existingCodeSet = new HashSet<string>(
                    existingGrades.Select(g => g.ProductCode)
                );
                var newCodes = matchedCodes.Where(c => !existingCodeSet.Contains(c)).ToList();

                if (newCodes.Count > 0)
                {
                    var newEntities = newCodes
                        .Select(c => new ProductGrade
                        {
                            Id = UuidHelper.GenerateUuid7(),
                            ProductCode = c,
                            Grade = dto.Grade,
                            CreatedAt = now,
                            UpdatedAt = now,
                            CreatedBy = "System",
                            UpdatedBy = "System",
                        })
                        .ToList();

                    await db.Insertable(newEntities).ExecuteCommandAsync();
                    createdCount = newCodes.Count;
                }

                var result = new PasteImportResultDto
                {
                    TotalCount = productNumbers.Count,
                    MatchedCount = matchedProducts.Count,
                    CreatedCount = createdCount,
                    UpdatedCount = updatedCount,
                    PreviewItems = previewItems,
                };

                _logger.LogInformation(
                    "粘贴导入商品等级完成，总计 {Total}，匹配 {Matched}，新建 {Created}，更新 {Updated}",
                    result.TotalCount,
                    result.MatchedCount,
                    result.CreatedCount,
                    result.UpdatedCount
                );

                return ApiResponse<PasteImportResultDto>.OK(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "粘贴导入商品等级失败");
                return ApiResponse<PasteImportResultDto>.Error(
                    "粘贴导入商品等级失败",
                    "PASTE_IMPORT_ERROR"
                );
            }
        }

        public async Task<ApiResponse<bool>> DeleteProductGradeAsync(string id)
        {
            try
            {
                var db = _context.Db;

                var grade = await db.Queryable<ProductGrade>()
                    .Where(g => g.Id == id && !g.IsDeleted)
                    .FirstAsync();

                if (grade == null)
                {
                    return ApiResponse<bool>.Error("商品等级不存在", "GRADE_NOT_FOUND");
                }

                grade.IsDeleted = true;
                grade.UpdatedAt = DateTime.UtcNow;
                grade.UpdatedBy = "System";

                await db.Updateable(grade).ExecuteCommandAsync();

                _logger.LogInformation("删除商品等级成功，Id: {Id}", id);
                return ApiResponse<bool>.OK(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除商品等级失败，Id: {Id}", id);
                return ApiResponse<bool>.Error("删除商品等级失败", "DELETE_GRADE_ERROR");
            }
        }

        public async Task<ApiResponse<List<ProductGradeBrief>>> GetProductGradesByProductCodesAsync(
            List<string> productCodes
        )
        {
            try
            {
                if (productCodes == null || productCodes.Count == 0)
                {
                    return ApiResponse<List<ProductGradeBrief>>.OK(new List<ProductGradeBrief>());
                }

                var db = _context.Db;

                var grades = await db.Queryable<ProductGrade>()
                    .Where(g => productCodes.Contains(g.ProductCode) && !g.IsDeleted)
                    .Select(g => new ProductGradeBrief
                    {
                        ProductCode = g.ProductCode,
                        Grade = g.Grade,
                    })
                    .ToListAsync();

                return ApiResponse<List<ProductGradeBrief>>.OK(grades);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量查询商品等级失败");
                return ApiResponse<List<ProductGradeBrief>>.Error(
                    "批量查询商品等级失败",
                    "GET_GRADES_BY_CODES_ERROR"
                );
            }
        }

        private async Task<ProductGradeDto> BuildGradeDto(ISqlSugarClient db, ProductGrade entity)
        {
            var row = await db.Queryable<ProductGrade>()
                .LeftJoin<DomesticProduct>((g, d) => g.ProductCode == d.ProductCode)
                .LeftJoin<ChinaSupplier>((g, d, s) => d.SupplierCode == s.SupplierCode)
                .LeftJoin<WarehouseProduct>((g, d, s, w) => g.ProductCode == w.ProductCode)
                .LeftJoin<Product>((g, d, s, w, p) => g.ProductCode == p.UUID)
                .Where((g, d, s, w, p) => g.Id == entity.Id)
                .Select(
                    (g, d, s, w, p) =>
                        new ProductGradeDto
                        {
                            Id = g.Id,
                            ProductCode = g.ProductCode,
                            Grade = g.Grade,
                            SupplierCode = d.SupplierCode,
                            SupplierName = s.SupplierName,
                            HbProductNo = d.HBProductNo,
                            ProductName = d.ProductName,
                            ProductImage = d.ProductImage,
                            DomesticPrice = d.DomesticPrice,
                            ImportPrice = w.ImportPrice,
                            OemPrice = w.OEMPrice,
                            RetailPrice = p.RetailPrice,
                            Barcode = d.Barcode,
                            CreatedAt = g.CreatedAt,
                            UpdatedAt = g.UpdatedAt,
                            CreatedBy = g.CreatedBy,
                            UpdatedBy = g.UpdatedBy,
                        }
                )
                .FirstAsync();

            return row ?? _mapper.Map<ProductGradeDto>(entity);
        }

        public async Task<ApiResponse<BatchUpdateGradePriceResult>> BatchUpdateGradePriceAsync(
            BatchUpdateGradePriceDto dto
        )
        {
            try
            {
                if (dto.ImportPrice == null && dto.OEMPrice == null)
                {
                    return ApiResponse<BatchUpdateGradePriceResult>.Error(
                        "至少需要填写一个价格",
                        "NO_PRICE_PROVIDED"
                    );
                }

                var db = _context.Db;
                var productCodes = dto.ProductCodes.Distinct().ToList();
                var totalAffected = 0;

                if (dto.TargetDatabase.Equals("HQ", StringComparison.OrdinalIgnoreCase))
                {
                    var hqDb = _hqContext.Db;
                    var now = DateTime.Now;

                    var hqInfoUpdate =
                        hqDb.Updateable<BlazorApp.Shared.Models.HqEntities.DIC_商品信息字典表>()
                            .Where(p => productCodes.Contains(p.H商品编码))
                            .SetColumns(p => p.FGC_LastModifier == "HBweb")
                            .SetColumns(p => p.FGC_LastModifyDate == now);
                    if (dto.ImportPrice.HasValue)
                        hqInfoUpdate = hqInfoUpdate.SetColumns(p =>
                            p.H进货价 == dto.ImportPrice.Value
                        );
                    if (dto.OEMPrice.HasValue)
                        hqInfoUpdate = hqInfoUpdate.SetColumns(p =>
                            p.H零售价 == dto.OEMPrice.Value
                        );
                    totalAffected += await hqInfoUpdate.ExecuteCommandAsync();

                    var hqStockUpdate =
                        hqDb.Updateable<BlazorApp.Shared.Models.HqEntities.CBP_DIC_商品库存表>()
                            .Where(p => productCodes.Contains(p.H商品编码))
                            .SetColumns(p => p.FGC_LastModifier == "HBweb")
                            .SetColumns(p => p.FGC_LastModifyDate == now);
                    if (dto.ImportPrice.HasValue)
                        hqStockUpdate = hqStockUpdate.SetColumns(p =>
                            p.H进口价格 == dto.ImportPrice.Value
                        );
                    if (dto.OEMPrice.HasValue)
                        hqStockUpdate = hqStockUpdate.SetColumns(p =>
                            p.H贴牌价格 == dto.OEMPrice.Value
                        );
                    totalAffected += await hqStockUpdate.ExecuteCommandAsync();

                    var hqRetailUpdate =
                        hqDb.Updateable<BlazorApp.Shared.Models.HqEntities.DIC_商品零售价表>()
                            .Where(p => productCodes.Contains(p.H商品编码))
                            .SetColumns(p => p.FGC_LastModifier == "HBweb")
                            .SetColumns(p => p.FGC_LastModifyDate == now);
                    if (dto.ImportPrice.HasValue)
                        hqRetailUpdate = hqRetailUpdate.SetColumns(p =>
                            p.H进货价 == dto.ImportPrice.Value
                        );
                    if (dto.OEMPrice.HasValue)
                        hqRetailUpdate = hqRetailUpdate.SetColumns(p =>
                            p.H分店零售价 == dto.OEMPrice.Value
                        );
                    totalAffected += await hqRetailUpdate.ExecuteCommandAsync();
                }
                else
                {
                    await db.Ado.UseTranAsync(async () =>
                    {
                        if (dto.ImportPrice.HasValue || dto.OEMPrice.HasValue)
                        {
                            var dpUpdate = db.Updateable<DomesticProduct>()
                                .Where(d => productCodes.Contains(d.ProductCode) && !d.IsDeleted);
                            if (dto.ImportPrice.HasValue)
                                dpUpdate = dpUpdate.SetColumns(d =>
                                    d.ImportPrice == dto.ImportPrice.Value
                                );
                            if (dto.OEMPrice.HasValue)
                                dpUpdate = dpUpdate.SetColumns(d =>
                                    d.OEMPrice == dto.OEMPrice.Value
                                );
                            dpUpdate = dpUpdate.SetColumns(d => d.UpdatedAt == DateTime.UtcNow);
                            totalAffected += await dpUpdate.ExecuteCommandAsync();

                            if (dto.ImportPrice.HasValue)
                            {
                                var wpList = await db.Queryable<WarehouseProduct>()
                                    .Where(wp =>
                                        productCodes.Contains(wp.ProductCode)
                                        && !wp.IsDeleted
                                        && wp.StockQuantity != null
                                    )
                                    .ToListAsync();
                                foreach (var wp in wpList)
                                {
                                    wp.ImportPrice = dto.ImportPrice.Value;
                                    wp.StockValue = wp.StockQuantity!.Value * dto.ImportPrice.Value;
                                    if (dto.OEMPrice.HasValue)
                                        wp.OEMPrice = dto.OEMPrice.Value;
                                    wp.UpdatedAt = DateTime.UtcNow;
                                }
                                if (wpList.Count > 0)
                                    totalAffected += await db.Updateable(wpList)
                                        .ExecuteCommandAsync();

                                var wpNoStock = db.Updateable<WarehouseProduct>()
                                    .Where(wp =>
                                        productCodes.Contains(wp.ProductCode)
                                        && !wp.IsDeleted
                                        && wp.StockQuantity == null
                                    )
                                    .SetColumns(wp => wp.ImportPrice == dto.ImportPrice.Value)
                                    .SetColumns(wp => wp.UpdatedAt == DateTime.UtcNow);
                                if (dto.OEMPrice.HasValue)
                                    wpNoStock = wpNoStock.SetColumns(wp =>
                                        wp.OEMPrice == dto.OEMPrice.Value
                                    );
                                totalAffected += await wpNoStock.ExecuteCommandAsync();
                            }
                            else if (dto.OEMPrice.HasValue)
                            {
                                var wpUpdate = db.Updateable<WarehouseProduct>()
                                    .Where(wp =>
                                        productCodes.Contains(wp.ProductCode) && !wp.IsDeleted
                                    )
                                    .SetColumns(wp => wp.OEMPrice == dto.OEMPrice.Value)
                                    .SetColumns(wp => wp.UpdatedAt == DateTime.UtcNow);
                                totalAffected += await wpUpdate.ExecuteCommandAsync();
                            }

                            var pUpdate = db.Updateable<Product>()
                                .Where(p => productCodes.Contains(p.ProductCode));
                            if (dto.ImportPrice.HasValue)
                                pUpdate = pUpdate.SetColumns(p =>
                                    p.PurchasePrice == dto.ImportPrice.Value
                                );
                            if (dto.OEMPrice.HasValue)
                                pUpdate = pUpdate.SetColumns(p =>
                                    p.RetailPrice == dto.OEMPrice.Value
                                );
                            pUpdate = pUpdate.SetColumns(p => p.UpdatedAt == DateTime.UtcNow);
                            totalAffected += await pUpdate.ExecuteCommandAsync();

                            var srpUpdate = db.Updateable<StoreRetailPrice>()
                                .Where(srp =>
                                    productCodes.Contains(srp.ProductCode) && !srp.IsDeleted
                                );
                            if (dto.ImportPrice.HasValue)
                                srpUpdate = srpUpdate.SetColumns(srp =>
                                    srp.PurchasePrice == dto.ImportPrice.Value
                                );
                            if (dto.OEMPrice.HasValue)
                                srpUpdate = srpUpdate.SetColumns(srp =>
                                    srp.StoreRetailPriceValue == dto.OEMPrice.Value
                                );
                            srpUpdate = srpUpdate.SetColumns(srp =>
                                srp.UpdatedAt == DateTime.UtcNow
                            );
                            totalAffected += await srpUpdate.ExecuteCommandAsync();
                        }
                    });
                }

                _logger.LogInformation(
                    "批量修改商品等级价格成功，数据库: {Db}，影响行数: {Count}",
                    dto.TargetDatabase,
                    totalAffected
                );
                return ApiResponse<BatchUpdateGradePriceResult>.OK(
                    new BatchUpdateGradePriceResult
                    {
                        AffectedCount = totalAffected,
                        Success = true,
                        Message = $"成功更新 {totalAffected} 条记录",
                    }
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量修改商品等级价格失败");
                return ApiResponse<BatchUpdateGradePriceResult>.Error(
                    "批量修改价格失败",
                    "BATCH_PRICE_ERROR"
                );
            }
        }
    }
}
