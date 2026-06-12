using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace BlazorApp.Api.Services.React
{
    public class CashRegisterUserReactService : ICashRegisterUserReactService
    {
        private readonly ISqlSugarClient _db;
        private readonly ILogger<CashRegisterUserReactService> _logger;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public CashRegisterUserReactService(
            SqlSugarContext context,
            ILogger<CashRegisterUserReactService> logger,
            IHttpContextAccessor httpContextAccessor
        )
        {
            _db = context.Db;
            _logger = logger;
            _httpContextAccessor = httpContextAccessor;
        }

        private string? GetCurrentUserGuid()
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user == null)
                return null;
            return user.FindFirst("userGuid")?.Value
                ?? user.FindFirst("userId")?.Value
                ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        }

        private async Task<string?> GetCurrentUserStoreCodeAsync()
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user == null)
                return null;

            var userGuid = GetCurrentUserGuid();
            if (!string.IsNullOrEmpty(userGuid))
            {
                var assignment = await _db.Queryable<UserStore>()
                    .Where(us => us.UserGUID == userGuid)
                    .OrderBy(us => us.IsPrimary ? 0 : 1)
                    .FirstAsync();

                if (assignment != null)
                {
                    var store = await _db.Queryable<Store>()
                        .Where(s => s.StoreGUID == assignment.StoreGUID)
                        .FirstAsync();
                    if (store != null && !string.IsNullOrEmpty(store.StoreCode))
                    {
                        return store.StoreCode;
                    }
                }
            }

            var storeCodeClaim = user.FindFirst("StoreCode")?.Value;
            return storeCodeClaim;
        }

        private async Task<List<string>> GetCurrentUserStoreCodesAsync()
        {
            var result = new List<string>();
            var userGuid = GetCurrentUserGuid();
            if (string.IsNullOrEmpty(userGuid))
                return result;
            var storeGuids = await _db.Queryable<UserStore>()
                .Where(us => us.UserGUID == userGuid)
                .Select(us => us.StoreGUID)
                .ToListAsync();
            if (!storeGuids.Any())
                return result;
            var codes = await _db.Queryable<Store>()
                .Where(s => storeGuids.Contains(s.StoreGUID))
                .Select(s => s.StoreCode)
                .ToListAsync();
            result.AddRange(codes.Where(c => !string.IsNullOrEmpty(c)));
            return result;
        }

        private bool HasRole(ClaimsPrincipal user, string role)
        {
            if (user == null)
                return false;
            return user.Claims.Any(c =>
                c.Type == ClaimTypes.Role
                && c.Value.Equals(role, StringComparison.OrdinalIgnoreCase)
            );
        }

        private bool IsAdmin()
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user == null)
                return false;
            return HasRole(user, "Admin") || HasRole(user, "WarehouseManager");
        }

        public async Task<GridResponseDto<CashRegisterUserListDto>> GetGridDataAsync(
            GridRequestDto request
        )
        {
            try
            {
                var pageIndex = (request.StartRow / request.PageSize) + 1;
                var pageSize = request.PageSize;

                var baseQuery = _db.Queryable<CashRegisterUser>();

                var userStoreCodes = await GetCurrentUserStoreCodesAsync();
                var isAdmin = IsAdmin();

                if (!isAdmin && userStoreCodes.Any())
                {
                    baseQuery = baseQuery.Where(u => userStoreCodes.Contains(u.StoreCode));
                }

                if (request.FilterModel != null && request.FilterModel.Any())
                {
                    foreach (var kv in request.FilterModel)
                    {
                        var col = kv.Key;
                        var f = kv.Value;
                        if (f == null || f.FilterType == null)
                            continue;
                        var type = f.FilterType.ToLower();
                        if (type == "text" && f.Filter != null)
                        {
                            var v = f.Filter?.ToString()?.Trim();
                            if (string.IsNullOrEmpty(v))
                                continue;
                            var op = (f.Type ?? "contains").ToLower();
                            switch (col)
                            {
                                case "storeCode":
                                    if (op == "equals")
                                        baseQuery = baseQuery.Where(u => u.StoreCode == v);
                                    else if (op == "contains")
                                        baseQuery = baseQuery.Where(u =>
                                            u.StoreCode != null && u.StoreCode.Contains(v)
                                        );
                                    break;
                                case "operatorUser":
                                    if (op == "equals")
                                        baseQuery = baseQuery.Where(u => u.OperatorUser == v);
                                    else if (op == "contains")
                                        baseQuery = baseQuery.Where(u =>
                                            u.OperatorUser != null && u.OperatorUser.Contains(v)
                                        );
                                    break;
                                case "userBarcode":
                                    if (op == "equals")
                                        baseQuery = baseQuery.Where(u => u.UserBarcode == v);
                                    else if (op == "contains")
                                        baseQuery = baseQuery.Where(u =>
                                            u.UserBarcode != null && u.UserBarcode.Contains(v)
                                        );
                                    break;
                                case "loginRole":
                                    if (op == "equals")
                                        baseQuery = baseQuery.Where(u => u.LoginRole == v);
                                    else if (op == "contains")
                                        baseQuery = baseQuery.Where(u =>
                                            u.LoginRole != null && u.LoginRole.Contains(v)
                                        );
                                    break;
                                case "status":
                                    if (bool.TryParse(v, out var statusValue))
                                        baseQuery = baseQuery.Where(u => u.Status == statusValue);
                                    break;
                            }
                        }
                    }
                }

                if (!string.IsNullOrWhiteSpace(request.GlobalSearch))
                {
                    var keyword = request.GlobalSearch.Trim();
                    baseQuery = baseQuery.Where(u =>
                        (u.OperatorUser != null && u.OperatorUser.Contains(keyword))
                        || (u.UserBarcode != null && u.UserBarcode.Contains(keyword))
                        || (u.LoginRole != null && u.LoginRole.Contains(keyword))
                        || (u.Remark != null && u.Remark.Contains(keyword))
                    );
                }

                var totalCount = await baseQuery.CountAsync();

                if (request.SortModel != null && request.SortModel.Any())
                {
                    var sort = request.SortModel.First();
                    var sortField = sort.ColId;
                    var sortDirection = sort.Sort?.ToLower() == "desc";
                    switch (sortField)
                    {
                        case "storeName":
                            baseQuery = sortDirection
                                ? baseQuery.OrderByDescending(u => u.StoreCode)
                                : baseQuery.OrderBy(u => u.StoreCode);
                            break;
                        case "operatorUser":
                            baseQuery = sortDirection
                                ? baseQuery.OrderByDescending(u => u.OperatorUser)
                                : baseQuery.OrderBy(u => u.OperatorUser);
                            break;
                        case "userBarcode":
                            baseQuery = sortDirection
                                ? baseQuery.OrderByDescending(u => u.UserBarcode)
                                : baseQuery.OrderBy(u => u.UserBarcode);
                            break;
                        case "loginRole":
                            baseQuery = sortDirection
                                ? baseQuery.OrderByDescending(u => u.LoginRole)
                                : baseQuery.OrderBy(u => u.LoginRole);
                            break;
                        case "printCount":
                            baseQuery = sortDirection
                                ? baseQuery.OrderByDescending(u => u.PrintCount)
                                : baseQuery.OrderBy(u => u.PrintCount);
                            break;
                        case "status":
                            baseQuery = sortDirection
                                ? baseQuery.OrderByDescending(u => u.Status)
                                : baseQuery.OrderBy(u => u.Status);
                            break;
                        case "remark":
                            baseQuery = sortDirection
                                ? baseQuery.OrderByDescending(u => u.Remark)
                                : baseQuery.OrderBy(u => u.Remark);
                            break;
                        case "createDate":
                            baseQuery = sortDirection
                                ? baseQuery.OrderByDescending(u => u.CreateDate)
                                : baseQuery.OrderBy(u => u.CreateDate);
                            break;
                        case "lastModifyDate":
                            baseQuery = sortDirection
                                ? baseQuery.OrderByDescending(u => u.LastModifyDate)
                                : baseQuery.OrderBy(u => u.LastModifyDate);
                            break;
                        default:
                            baseQuery = baseQuery.OrderByDescending(u => u.CreateDate);
                            break;
                    }
                }
                else
                {
                    baseQuery = baseQuery.OrderByDescending(u => u.CreateDate);
                }

                var items = await baseQuery
                    .Skip((pageIndex - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                var resultList = items
                    .Select(u => new CashRegisterUserListDto
                    {
                        Id = u.Id,
                        HGUID = u.HGUID ?? string.Empty,
                        StoreCode = u.StoreCode,
                        OperatorUser = u.OperatorUser,
                        UserBarcode = u.UserBarcode,
                        LoginRole = u.LoginRole,
                        Remark = u.Remark,
                        PrintCount = u.PrintCount,
                        Status = u.Status,
                        CreateDate = u.CreateDate,
                        LastModifyDate = u.LastModifyDate,
                        LastModifier = u.LastModifier,
                    })
                    .ToList();

                var storeCodes = resultList
                    .Where(r => !string.IsNullOrEmpty(r.StoreCode))
                    .Select(r => r.StoreCode)
                    .Distinct()
                    .ToList();
                if (storeCodes.Any())
                {
                    var stores = await _db.Queryable<Store>()
                        .Where(s => storeCodes.Contains(s.StoreCode))
                        .ToListAsync();
                    var storeDict = stores.ToDictionary(s => s.StoreCode, s => s.StoreName);
                    foreach (var item in resultList)
                    {
                        if (
                            !string.IsNullOrEmpty(item.StoreCode)
                            && storeDict.TryGetValue(item.StoreCode, out var storeName)
                        )
                        {
                            item.StoreName = storeName;
                        }
                    }
                }

                return GridResponseDto<CashRegisterUserListDto>.OK(resultList, totalCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取收银用户网格数据失败");
                return GridResponseDto<CashRegisterUserListDto>.Error(ex.Message);
            }
        }

        public async Task<ApiResponse<CashRegisterUserDetailDto>> GetByHGuidAsync(string hGuid)
        {
            try
            {
                var userStoreCodes = await GetCurrentUserStoreCodesAsync();
                var isAdmin = IsAdmin();

                var query = _db.Queryable<CashRegisterUser>().Where(u => u.HGUID == hGuid);

                if (!isAdmin && userStoreCodes.Any())
                {
                    query = query.Where(u => userStoreCodes.Contains(u.StoreCode));
                }

                var entity = await query.FirstAsync();

                if (entity == null)
                {
                    return ApiResponse<CashRegisterUserDetailDto>.Error("收银用户不存在");
                }

                string? storeName = null;
                if (!string.IsNullOrEmpty(entity.StoreCode))
                {
                    var store = await _db.Queryable<Store>()
                        .Where(s => s.StoreCode == entity.StoreCode)
                        .FirstAsync();
                    storeName = store?.StoreName;
                }

                var result = new CashRegisterUserDetailDto
                {
                    Id = entity.Id,
                    HGUID = entity.HGUID ?? string.Empty,
                    StoreCode = entity.StoreCode,
                    StoreName = storeName,
                    OperatorUser = entity.OperatorUser,
                    UserBarcode = entity.UserBarcode,
                    LoginRole = entity.LoginRole,
                    Remark = entity.Remark,
                    PrintCount = entity.PrintCount,
                    Status = entity.Status,
                    Creator = entity.Creator,
                    CreateDate = entity.CreateDate,
                    LastModifier = entity.LastModifier,
                    LastModifyDate = entity.LastModifyDate,
                };

                return ApiResponse<CashRegisterUserDetailDto>.OK(result, "获取成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取收银用户详情失败，HGUID: {HGUID}", hGuid);
                return ApiResponse<CashRegisterUserDetailDto>.Error("获取详情失败");
            }
        }

        public async Task<ApiResponse<CashRegisterUserDetailDto>> CreateAsync(
            CreateCashRegisterUserDto dto,
            string createdBy
        )
        {
            try
            {
                var userStoreCodes = await GetCurrentUserStoreCodesAsync();
                var userStoreCode = await GetCurrentUserStoreCodeAsync();
                var isAdmin = IsAdmin();

                if (!isAdmin && userStoreCodes.Any())
                {
                    if (
                        !string.IsNullOrEmpty(dto.StoreCode)
                        && !userStoreCodes.Contains(dto.StoreCode)
                    )
                    {
                        return ApiResponse<CashRegisterUserDetailDto>.Error(
                            "只能创建自己分店的收银用户"
                        );
                    }
                    dto.StoreCode = userStoreCode;
                }

                if (string.IsNullOrEmpty(dto.StoreCode))
                {
                    return ApiResponse<CashRegisterUserDetailDto>.Error("分店代码不能为空");
                }

                if (string.IsNullOrEmpty(dto.UserBarcode))
                {
                    return ApiResponse<CashRegisterUserDetailDto>.Error("用户条码不能为空");
                }

                var existing = await _db.Queryable<CashRegisterUser>()
                    .Where(u => u.UserBarcode == dto.UserBarcode)
                    .FirstAsync();

                if (existing != null)
                {
                    return ApiResponse<CashRegisterUserDetailDto>.Error("用户条码已存在");
                }

                var now = DateTime.UtcNow;
                var entity = new CashRegisterUser
                {
                    HGUID = Guid.NewGuid().ToString("N"),
                    StoreCode = dto.StoreCode ?? string.Empty,
                    OperatorUser = dto.OperatorUser ?? string.Empty,
                    UserBarcode = dto.UserBarcode ?? string.Empty,
                    LoginRole = dto.LoginRole ?? string.Empty,
                    Remark = dto.Remark ?? string.Empty,
                    PrintCount = 0,
                    Status = dto.Status,
                    Creator = createdBy,
                    CreateDate = now,
                    LastModifier = createdBy,
                    LastModifyDate = now,
                };

                await _db.Insertable(entity).ExecuteCommandAsync();

                string? storeName = null;
                if (!string.IsNullOrEmpty(entity.StoreCode))
                {
                    var store = await _db.Queryable<Store>()
                        .Where(s => s.StoreCode == entity.StoreCode)
                        .FirstAsync();
                    storeName = store?.StoreName;
                }

                var result = new CashRegisterUserDetailDto
                {
                    Id = entity.Id,
                    HGUID = entity.HGUID ?? string.Empty,
                    StoreCode = entity.StoreCode,
                    StoreName = storeName,
                    OperatorUser = entity.OperatorUser,
                    UserBarcode = entity.UserBarcode,
                    LoginRole = entity.LoginRole,
                    Remark = entity.Remark,
                    PrintCount = entity.PrintCount,
                    Status = entity.Status,
                    Creator = entity.Creator,
                    CreateDate = entity.CreateDate,
                    LastModifier = entity.LastModifier,
                    LastModifyDate = entity.LastModifyDate,
                };

                return ApiResponse<CashRegisterUserDetailDto>.OK(result, "创建成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建收银用户失败");
                return ApiResponse<CashRegisterUserDetailDto>.Error("创建失败");
            }
        }

        public async Task<ApiResponse<CashRegisterUserDetailDto>> UpdateAsync(
            string hGuid,
            UpdateCashRegisterUserDto dto,
            string updatedBy
        )
        {
            try
            {
                var userStoreCodes = await GetCurrentUserStoreCodesAsync();
                var isAdmin = IsAdmin();

                var query = _db.Queryable<CashRegisterUser>().Where(u => u.HGUID == hGuid);

                if (!isAdmin && userStoreCodes.Any())
                {
                    query = query.Where(u => userStoreCodes.Contains(u.StoreCode));
                }

                var entity = await query.FirstAsync();

                if (entity == null)
                {
                    return ApiResponse<CashRegisterUserDetailDto>.Error("收银用户不存在");
                }

                if (!isAdmin && userStoreCodes.Any() && !userStoreCodes.Contains(entity.StoreCode))
                {
                    return ApiResponse<CashRegisterUserDetailDto>.Error("没有权限修改此收银用户");
                }

                if (!string.IsNullOrEmpty(dto.StoreCode))
                {
                    if (!isAdmin && userStoreCodes.Any() && !userStoreCodes.Contains(dto.StoreCode))
                    {
                        return ApiResponse<CashRegisterUserDetailDto>.Error(
                            "只能修改自己分店的收银用户"
                        );
                    }
                    entity.StoreCode = dto.StoreCode;
                }

                if (!string.IsNullOrEmpty(dto.UserBarcode) && dto.UserBarcode != entity.UserBarcode)
                {
                    var existing = await _db.Queryable<CashRegisterUser>()
                        .Where(u => u.UserBarcode == dto.UserBarcode && u.HGUID != hGuid)
                        .FirstAsync();

                    if (existing != null)
                    {
                        return ApiResponse<CashRegisterUserDetailDto>.Error("用户条码已存在");
                    }
                    entity.UserBarcode = dto.UserBarcode;
                }

                entity.OperatorUser = dto.OperatorUser ?? string.Empty;
                entity.LoginRole = dto.LoginRole ?? string.Empty;
                entity.Remark = dto.Remark ?? string.Empty;
                entity.Status = dto.Status;
                entity.LastModifier = updatedBy;
                entity.LastModifyDate = DateTime.UtcNow;

                await _db.Updateable(entity).ExecuteCommandAsync();

                string? storeName = null;
                if (!string.IsNullOrEmpty(entity.StoreCode))
                {
                    var store = await _db.Queryable<Store>()
                        .Where(s => s.StoreCode == entity.StoreCode)
                        .FirstAsync();
                    storeName = store?.StoreName;
                }

                var result = new CashRegisterUserDetailDto
                {
                    Id = entity.Id,
                    HGUID = entity.HGUID ?? string.Empty,
                    StoreCode = entity.StoreCode,
                    StoreName = storeName,
                    OperatorUser = entity.OperatorUser,
                    UserBarcode = entity.UserBarcode,
                    LoginRole = entity.LoginRole,
                    Remark = entity.Remark,
                    PrintCount = entity.PrintCount,
                    Status = entity.Status,
                    Creator = entity.Creator,
                    CreateDate = entity.CreateDate,
                    LastModifier = entity.LastModifier,
                    LastModifyDate = entity.LastModifyDate,
                };

                return ApiResponse<CashRegisterUserDetailDto>.OK(result, "更新成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新收银用户失败，HGUID: {HGUID}", hGuid);
                return ApiResponse<CashRegisterUserDetailDto>.Error("更新失败");
            }
        }

        public async Task<ApiResponse<bool>> DeleteAsync(string hGuid, string updatedBy)
        {
            try
            {
                var userStoreCodes = await GetCurrentUserStoreCodesAsync();
                var isAdmin = IsAdmin();

                var query = _db.Queryable<CashRegisterUser>().Where(u => u.HGUID == hGuid);

                if (!isAdmin && userStoreCodes.Any())
                {
                    query = query.Where(u => userStoreCodes.Contains(u.StoreCode));
                }

                var entity = await query.FirstAsync();

                if (entity == null)
                {
                    return ApiResponse<bool>.Error("收银用户不存在");
                }

                if (!isAdmin && userStoreCodes.Any() && !userStoreCodes.Contains(entity.StoreCode))
                {
                    return ApiResponse<bool>.Error("没有权限删除此收银用户");
                }

                await _db.Deleteable(entity).ExecuteCommandAsync();

                return ApiResponse<bool>.OK(true, "删除成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除收银用户失败，HGUID: {HGUID}", hGuid);
                return ApiResponse<bool>.Error("删除失败");
            }
        }

        public async Task<ApiResponse<bool>> BatchDeleteAsync(List<string> hGuids, string updatedBy)
        {
            try
            {
                if (hGuids == null || !hGuids.Any())
                {
                    return ApiResponse<bool>.Error("请选择要删除的记录");
                }

                var userStoreCodes = await GetCurrentUserStoreCodesAsync();
                var isAdmin = IsAdmin();

                var query = _db.Queryable<CashRegisterUser>().Where(u => hGuids.Contains(u.HGUID));

                if (!isAdmin && userStoreCodes.Any())
                {
                    query = query.Where(u => userStoreCodes.Contains(u.StoreCode));
                }

                var entities = await query.ToListAsync();

                if (!entities.Any())
                {
                    return ApiResponse<bool>.Error("没有找到要删除的记录");
                }

                await _db.Deleteable(entities).ExecuteCommandAsync();

                return ApiResponse<bool>.OK(true, $"删除成功，共删除 {entities.Count} 条记录");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量删除收银用户失败");
                return ApiResponse<bool>.Error("批量删除失败");
            }
        }
    }
}
