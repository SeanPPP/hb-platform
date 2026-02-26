using BlazorApp.Api.Data;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.DTOs;
using Microsoft.Extensions.Logging;
using SqlSugar;
using BlazorApp.Api.Interfaces;

namespace BlazorApp.Api.Services
{
    /// <summary>
    /// 国内供应商服务实现
    /// </summary>
    public class ChinaSupplierService : IChinaSupplierService
    {
        private readonly SqlSugarContext _context;
        private readonly ILogger<ChinaSupplierService> _logger;

        public ChinaSupplierService(SqlSugarContext context, ILogger<ChinaSupplierService> logger)
        {
            _context = context;
            _logger = logger;
        }

        /// <summary>
        /// 获取国内供应商列表
        /// </summary>
        public async Task<ApiResponse<PagedResult<ChinaSupplierDto>>> GetChinaSuppliersAsync(ChinaSupplierQueryDto query)
        {
            try
            {
                var db = _context.Db;
                
                // 构建基础查询
                var supplierQuery = db.Queryable<ChinaSupplier>();

                // 搜索条件 - 供应商名称、代码、联系人
                if (!string.IsNullOrEmpty(query.Search))
                {
                    supplierQuery = supplierQuery.Where(s => 
                        (s.SupplierName != null && s.SupplierName.Contains(query.Search)) ||
                        (s.SupplierCode != null && s.SupplierCode.Contains(query.Search)) ||
                        (s.ShopNumber != null && s.ShopNumber.Contains(query.Search)) ||
                        (s.ContactPerson != null && s.ContactPerson.Contains(query.Search)));
                }

                // 状态筛选
                if (query.Status.HasValue)
                {
                    supplierQuery = supplierQuery.Where(s => s.Status == query.Status.Value);
                }

                // 供应商代码筛选
                if (!string.IsNullOrEmpty(query.SupplierCode))
                {
                    supplierQuery = supplierQuery.Where(s => s.SupplierCode == query.SupplierCode);
                }

                // 获取总数
                var total = await supplierQuery.CountAsync();

                // 排序处理
                if (!string.IsNullOrEmpty(query.SortField))
                {
                    var isDescending = !string.IsNullOrEmpty(query.SortDirection) && 
                                     query.SortDirection.ToLower() == "desc";
                    
                    supplierQuery = query.SortField.ToLower() switch
                    {
                        "suppliercode" => isDescending 
                            ? supplierQuery.OrderByDescending(s => s.SupplierCode)
                            : supplierQuery.OrderBy(s => s.SupplierCode),
                        "suppliername" => isDescending 
                            ? supplierQuery.OrderByDescending(s => s.SupplierName)
                            : supplierQuery.OrderBy(s => s.SupplierName),
                        "shopnumber" => isDescending 
                            ? supplierQuery.OrderByDescending(s => s.ShopNumber)
                            : supplierQuery.OrderBy(s => s.ShopNumber),
                        "contactperson" => isDescending 
                            ? supplierQuery.OrderByDescending(s => s.ContactPerson)
                            : supplierQuery.OrderBy(s => s.ContactPerson),
                        "phone" => isDescending 
                            ? supplierQuery.OrderByDescending(s => s.Phone)
                            : supplierQuery.OrderBy(s => s.Phone),
                        "status" => isDescending 
                            ? supplierQuery.OrderByDescending(s => s.Status)
                            : supplierQuery.OrderBy(s => s.Status),
                        "fgc_createdate" or "createdate" => isDescending 
                            ? supplierQuery.OrderByDescending(s => s.FGC_CreateDate)
                            : supplierQuery.OrderBy(s => s.FGC_CreateDate),
                        _ => supplierQuery.OrderByDescending(s => s.FGC_CreateDate) // 默认按创建时间降序
                    };
                }
                else
                {
                    // 默认排序：按创建时间降序
                    supplierQuery = supplierQuery.OrderByDescending(s => s.FGC_CreateDate);
                }

                // 获取分页数据
                var suppliers = await supplierQuery
                    .Skip((query.Page - 1) * query.PageSize)
                    .Take(query.PageSize)
                    .Select(s => new ChinaSupplierDto
                    {
                        Guid = s.Guid,
                        SupplierCode = s.SupplierCode,
                        SupplierName = s.SupplierName,
                        ShopNumber = s.ShopNumber,
                        ContactPerson = s.ContactPerson,
                        Phone = s.Phone,
                        Email = s.Email,
                        StorefrontPhoto = s.StorefrontPhoto,
                        Remarks = s.Remarks,
                        Status = s.Status,
                        FGC_Creator = s.FGC_Creator,
                        FGC_CreateDate = s.FGC_CreateDate,
                        FGC_LastModifier = s.FGC_LastModifier,
                        FGC_LastModifyDate = s.FGC_LastModifyDate
                    })
                    .ToListAsync();

                var result = new PagedResult<ChinaSupplierDto>
                {
                    Items = suppliers,
                    Total = total,
                    Page = query.Page,
                    PageSize = query.PageSize
                };

                return ApiResponse<PagedResult<ChinaSupplierDto>>.OK(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取国内供应商列表失败");
                return ApiResponse<PagedResult<ChinaSupplierDto>>.Error("获取国内供应商列表失败", "GET_CHINA_SUPPLIERS_ERROR");
            }
        }

        /// <summary>
        /// 根据GUID获取国内供应商详情
        /// </summary>
        public async Task<ApiResponse<ChinaSupplierDetailDto>> GetChinaSupplierByGuidAsync(string guid)
        {
            try
            {
                var db = _context.Db;

                var supplier = await db.Queryable<ChinaSupplier>()
                    .Where(s => s.Guid == guid)
                    .Select(s => new ChinaSupplierDetailDto
                    {
                        Guid = s.Guid,
                        SupplierCode = s.SupplierCode,
                        SupplierName = s.SupplierName,
                        ShopNumber = s.ShopNumber,
                        ContactPerson = s.ContactPerson,
                        Phone = s.Phone,
                        Email = s.Email,
                        StorefrontPhoto = s.StorefrontPhoto,
                        Remarks = s.Remarks,
                        Status = s.Status,
                        FGC_Creator = s.FGC_Creator,
                        FGC_CreateDate = s.FGC_CreateDate,
                        FGC_LastModifier = s.FGC_LastModifier,
                        FGC_LastModifyDate = s.FGC_LastModifyDate
                    })
                    .FirstAsync();

                if (supplier == null)
                {
                    return ApiResponse<ChinaSupplierDetailDto>.Error("国内供应商不存在", "CHINA_SUPPLIER_NOT_FOUND");
                }

             

                return ApiResponse<ChinaSupplierDetailDto>.OK(supplier);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取国内供应商详情失败，GUID: {SupplierGUID}", guid);
                return ApiResponse<ChinaSupplierDetailDto>.Error("获取国内供应商详情失败", "GET_CHINA_SUPPLIER_ERROR");
            }
        }

        /// <summary>
        /// 创建国内供应商
        /// </summary>
        public async Task<ApiResponse<ChinaSupplierDto>> CreateChinaSupplierAsync(CreateChinaSupplierDto dto)
        {
            try
            {
                var db = _context.Db;

                // 检查供应商代码是否已存在
                var existingSupplier = await db.Queryable<ChinaSupplier>()
                    .Where(s => s.SupplierCode == dto.SupplierCode)
                    .FirstAsync();

                if (existingSupplier != null)
                {
                    return ApiResponse<ChinaSupplierDto>.Error("供应商代码已存在", "SUPPLIER_CODE_EXISTS");
                }

                var supplier = new ChinaSupplier
                {
                    Guid = Guid.NewGuid().ToString(),
                    SupplierCode = dto.SupplierCode,
                    SupplierName = dto.SupplierName,
                    ShopNumber = dto.ShopNumber,
                    ContactPerson = dto.ContactPerson,
                    Phone = dto.Phone,
                    Email = dto.Email,
                    StorefrontPhoto = dto.StorefrontPhoto,
                    Remarks = dto.Remarks,
                    Status = dto.Status,
                    FGC_Creator = "System", // 这里应该从当前用户上下文获取
                    FGC_CreateDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    FGC_LastModifier = "System",
                    FGC_LastModifyDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    UpdatedAt = DateTime.Now,
                    FGC_Rowversion = Guid.NewGuid().ToString(),
                    FGC_UpdateHelp = ""
                };

                await db.Insertable(supplier).ExecuteCommandAsync();

                var result = new ChinaSupplierDto
                {
                    Guid = supplier.Guid,
                    SupplierCode = supplier.SupplierCode,
                    SupplierName = supplier.SupplierName,
                    ShopNumber = supplier.ShopNumber,
                    ContactPerson = supplier.ContactPerson,
                    Phone = supplier.Phone,
                    Email = supplier.Email,
                    StorefrontPhoto = supplier.StorefrontPhoto,
                    Remarks = supplier.Remarks,
                    Status = supplier.Status,
                    FGC_Creator = supplier.FGC_Creator,
                    FGC_CreateDate = supplier.FGC_CreateDate,
                    FGC_LastModifier = supplier.FGC_LastModifier,
                    FGC_LastModifyDate = supplier.FGC_LastModifyDate
                };

                return ApiResponse<ChinaSupplierDto>.OK(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建国内供应商失败");
                return ApiResponse<ChinaSupplierDto>.Error("创建国内供应商失败", "CREATE_CHINA_SUPPLIER_ERROR");
            }
        }

        /// <summary>
        /// 更新国内供应商
        /// </summary>
        public async Task<ApiResponse<ChinaSupplierDto>> UpdateChinaSupplierAsync(string guid, UpdateChinaSupplierDto dto)
        {
            try
            {
                var db = _context.Db;

                var supplier = await db.Queryable<ChinaSupplier>().Where(s => s.Guid == guid).FirstAsync();
                if (supplier == null)
                {
                    return ApiResponse<ChinaSupplierDto>.Error("国内供应商不存在", "CHINA_SUPPLIER_NOT_FOUND");
                }

                // 检查供应商代码是否已存在（排除当前供应商）
                var existingSupplier = await db.Queryable<ChinaSupplier>()
                    .Where(s => s.SupplierCode == dto.SupplierCode && s.Guid != guid)
                    .FirstAsync();

                if (existingSupplier != null)
                {
                    return ApiResponse<ChinaSupplierDto>.Error("供应商代码已存在", "SUPPLIER_CODE_EXISTS");
                }

                await db.Updateable<ChinaSupplier>()
                    .SetColumns(s => s.SupplierCode == dto.SupplierCode)
                    .SetColumns(s => s.SupplierName == dto.SupplierName)
                    .SetColumns(s => s.ShopNumber == dto.ShopNumber)
                    .SetColumns(s => s.ContactPerson == dto.ContactPerson)
                    .SetColumns(s => s.Phone == dto.Phone)
                    .SetColumns(s => s.Email == dto.Email)
                    .SetColumns(s => s.StorefrontPhoto == dto.StorefrontPhoto)
                    .SetColumns(s => s.Remarks == dto.Remarks)
                    .SetColumns(s => s.Status == dto.Status)
                    .SetColumns(s => s.FGC_LastModifier == "System") // 应该从当前用户上下文获取
                    .SetColumns(s => s.FGC_LastModifyDate == DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
                    .SetColumns(s => s.UpdatedAt == DateTime.Now)
                    .SetColumns(s => s.FGC_Rowversion == Guid.NewGuid().ToString())
                    .Where(s => s.Guid == guid)
                    .ExecuteCommandAsync();

                // 获取更新后的供应商信息
                var updatedSupplier = await db.Queryable<ChinaSupplier>()
                    .Where(s => s.Guid == guid)
                    .FirstAsync();

                var result = new ChinaSupplierDto
                {
                    Guid = updatedSupplier.Guid,
                    SupplierCode = updatedSupplier.SupplierCode,
                    SupplierName = updatedSupplier.SupplierName,
                    ShopNumber = updatedSupplier.ShopNumber,
                    ContactPerson = updatedSupplier.ContactPerson,
                    Phone = updatedSupplier.Phone,
                    Email = updatedSupplier.Email,
                    StorefrontPhoto = updatedSupplier.StorefrontPhoto,
                    Remarks = updatedSupplier.Remarks,
                    Status = updatedSupplier.Status,
                    FGC_Creator = updatedSupplier.FGC_Creator,
                    FGC_CreateDate = updatedSupplier.FGC_CreateDate,
                    FGC_LastModifier = updatedSupplier.FGC_LastModifier,
                    FGC_LastModifyDate = updatedSupplier.FGC_LastModifyDate
                };

                return ApiResponse<ChinaSupplierDto>.OK(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新国内供应商失败，GUID: {SupplierGUID}", guid);
                return ApiResponse<ChinaSupplierDto>.Error("更新国内供应商失败", "UPDATE_CHINA_SUPPLIER_ERROR");
            }
        }

        /// <summary>
        /// 删除国内供应商
        /// </summary>
        public async Task<ApiResponse<bool>> DeleteChinaSupplierAsync(string guid)
        {
            try
            {
                var db = _context.Db;

                var supplier = await db.Queryable<ChinaSupplier>().Where(s => s.Guid == guid).FirstAsync();
                if (supplier == null)
                {
                    return ApiResponse<bool>.Error("国内供应商不存在", "CHINA_SUPPLIER_NOT_FOUND");
                }

                // TODO: 检查是否有相关订单（当有订单表时取消注释）
                // var hasOrders = await db.Queryable<YiwuOrder>().Where(o => o.SupplierGuid == guid).AnyAsync();
                // if (hasOrders)
                // {
                //     return ApiResponse<bool>.Error("该供应商有相关订单，无法删除", "SUPPLIER_HAS_ORDERS");
                // }

                // 执行删除
                var deleteResult = await db.Deleteable<ChinaSupplier>().Where(s => s.Guid == guid).ExecuteCommandAsync();
                
                if (deleteResult > 0)
                {
                    _logger.LogInformation("成功删除国内供应商，GUID: {SupplierGUID}, Name: {SupplierName}", guid, supplier.SupplierName);
                    return ApiResponse<bool>.OK(true, "删除成功");
                }
                else
                {
                    return ApiResponse<bool>.Error("删除失败", "DELETE_FAILED");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除国内供应商失败，GUID: {SupplierGUID}", guid);
                return ApiResponse<bool>.Error("删除国内供应商失败", "DELETE_CHINA_SUPPLIER_ERROR");
            }
        }

        /// <summary>
        /// 启用/禁用国内供应商
        /// </summary>
        public async Task<ApiResponse<ChinaSupplierDto>> ToggleSupplierStatusAsync(string guid, int status)
        {
            try
            {
                var db = _context.Db;

                var supplier = await db.Queryable<ChinaSupplier>().Where(s => s.Guid == guid).FirstAsync();
                if (supplier == null)
                {
                    return ApiResponse<ChinaSupplierDto>.Error("国内供应商不存在", "CHINA_SUPPLIER_NOT_FOUND");
                }

                await db.Updateable<ChinaSupplier>()
                    .SetColumns(s => s.Status == status)
                    .SetColumns(s => s.FGC_LastModifier == "System") // 应该从当前用户上下文获取
                    .SetColumns(s => s.FGC_LastModifyDate == DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
                    .SetColumns(s => s.UpdatedAt == DateTime.Now)
                    .SetColumns(s => s.FGC_Rowversion == Guid.NewGuid().ToString())
                    .Where(s => s.Guid == guid)
                    .ExecuteCommandAsync();

                // 获取更新后的供应商信息
                var updatedSupplier = await db.Queryable<ChinaSupplier>()
                    .Where(s => s.Guid == guid)
                    .FirstAsync();

                var result = new ChinaSupplierDto
                {
                    Guid = updatedSupplier.Guid,
                    SupplierCode = updatedSupplier.SupplierCode,
                    SupplierName = updatedSupplier.SupplierName,
                    ShopNumber = updatedSupplier.ShopNumber,
                    ContactPerson = updatedSupplier.ContactPerson,
                    Phone = updatedSupplier.Phone,
                    Email = updatedSupplier.Email,
                    StorefrontPhoto = updatedSupplier.StorefrontPhoto,
                    Remarks = updatedSupplier.Remarks,
                    Status = updatedSupplier.Status,
                    FGC_Creator = updatedSupplier.FGC_Creator,
                    FGC_CreateDate = updatedSupplier.FGC_CreateDate,
                    FGC_LastModifier = updatedSupplier.FGC_LastModifier,
                    FGC_LastModifyDate = updatedSupplier.FGC_LastModifyDate
                };

                return ApiResponse<ChinaSupplierDto>.OK(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "切换国内供应商状态失败，GUID: {SupplierGUID}", guid);
                return ApiResponse<ChinaSupplierDto>.Error("切换国内供应商状态失败", "TOGGLE_SUPPLIER_STATUS_ERROR");
            }
        }

        /// <summary>
        /// 根据供应商代码检查是否存在
        /// </summary>
        public async Task<ApiResponse<bool>> CheckSupplierCodeExistsAsync(string supplierCode, string? excludeGuid = null)
        {
            try
            {
                var db = _context.Db;

                var query = db.Queryable<ChinaSupplier>().Where(s => s.SupplierCode == supplierCode);
                
                if (!string.IsNullOrEmpty(excludeGuid))
                {
                    query = query.Where(s => s.Guid != excludeGuid);
                }

                var exists = await query.AnyAsync();

                return ApiResponse<bool>.OK(exists);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检查供应商代码是否存在失败，SupplierCode: {SupplierCode}", supplierCode);
                return ApiResponse<bool>.Error("检查供应商代码失败", "CHECK_SUPPLIER_CODE_ERROR");
            }
        }

        /// <summary>
        /// 获取所有启用的国内供应商（下拉选择用）
        /// </summary>
        public async Task<ApiResponse<List<ChinaSupplierDto>>> GetActiveChinaSuppliersAsync()
        {
            try
            {
                var db = _context.Db;

                var suppliers = await db.Queryable<ChinaSupplier>()
                    .Where(s => s.Status == 1)
                    .OrderBy(s => s.SupplierCode)
                    .Select(s => new ChinaSupplierDto
                    {
                        Guid = s.Guid,
                        SupplierCode = s.SupplierCode,
                        SupplierName = s.SupplierName,
                        ShopNumber = s.ShopNumber,
                        ContactPerson = s.ContactPerson,
                        Phone = s.Phone,
                        Email = s.Email,
                        Status = s.Status
                    })
                    .ToListAsync();

                return ApiResponse<List<ChinaSupplierDto>>.OK(suppliers);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取启用的国内供应商列表失败");
                return ApiResponse<List<ChinaSupplierDto>>.Error("获取启用的国内供应商列表失败", "GET_ACTIVE_CHINA_SUPPLIERS_ERROR");
            }
        }

        /// <summary>
        /// 获取所有国内供应商（不分页）
        /// </summary>
        public async Task<ApiResponse<List<ChinaSupplierDto>>> GetAllChinaSuppliersAsync()
        {
            try
            {
                var db = _context.Db;

                var suppliers = await db.Queryable<ChinaSupplier>()
                    .Where(s => !s.IsDeleted)
                    .OrderBy(s => s.SupplierName)
                    .Select(s => new ChinaSupplierDto
                    {
                        Guid = s.Guid,
                        SupplierCode = s.SupplierCode,
                        SupplierName = s.SupplierName,
                        ShopNumber = s.ShopNumber,
                        ContactPerson = s.ContactPerson,
                        Phone = s.Phone,
                        Email = s.Email,
                        Status = s.Status
                    })
                    .ToListAsync();

                return ApiResponse<List<ChinaSupplierDto>>.OK(suppliers);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取所有国内供应商失败");
                return ApiResponse<List<ChinaSupplierDto>>.Error("获取所有国内供应商失败");
            }
        }

        /// <summary>
        /// 自动生成下一个供应商编码
        /// </summary>
        public async Task<ApiResponse<string>> GenerateNextSupplierCodeAsync()
        {
            try
            {
                // 获取所有HB开头的供应商编码
                var hbSuppliers = await _context.ChinaSupplierDb
                    .AsQueryable()
                    .Where(s => s.SupplierCode != null && s.SupplierCode.StartsWith("HB"))
                    .Select(s => s.SupplierCode)
                    .ToListAsync();

                var maxNumber = 0;
                foreach (var code in hbSuppliers)
                {
                    if (!string.IsNullOrEmpty(code) && code.Length >= 5 && code.StartsWith("HB"))
                    {
                        var numberPart = code.Substring(2);
                        if (int.TryParse(numberPart, out int number))
                        {
                            maxNumber = Math.Max(maxNumber, number);
                        }
                    }
                }

                var nextCode = $"HB{(maxNumber + 1):D3}";
                _logger.LogInformation("生成下一个供应商编码: {SupplierCode}", nextCode);
                
                return ApiResponse<string>.OK(nextCode);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "生成供应商编码失败");
                return ApiResponse<string>.Error("生成供应商编码失败", "GENERATE_SUPPLIER_CODE_FAILED");
            }
        }
    }
}