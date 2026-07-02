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
                    .Where(us => us.UserGUID == userGuid && !us.IsDeleted)
                    .OrderBy(us => us.IsPrimary ? 0 : 1)
                    .FirstAsync();

                if (assignment != null)
                {
                    var store = await _db.Queryable<Store>()
                        .Where(s =>
                            s.StoreGUID == assignment.StoreGUID && s.IsActive && !s.IsDeleted
                        )
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
                // 关键逻辑：收银条码管理沿用“可管理门店”语义，当前账号只有 primary 分店才作为管理范围。
                .Where(us => us.UserGUID == userGuid && !us.IsDeleted && us.IsPrimary)
                .Select(us => us.StoreGUID)
                .ToListAsync();
            if (!storeGuids.Any())
                return result;
            var codes = await _db.Queryable<Store>()
                .Where(s => storeGuids.Contains(s.StoreGUID) && s.IsActive && !s.IsDeleted)
                .Select(s => s.StoreCode)
                .ToListAsync();
            result.AddRange(NormalizeStoreCodes(codes));
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
            return HasRole(user, "Admin");
        }

        private static List<string> NormalizeStoreCodes(IEnumerable<string?> storeCodes)
        {
            return storeCodes
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Select(code => code!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private sealed class LinkedUserStoreRow
        {
            public string UserGUID { get; set; } = string.Empty;
            public string? StoreCode { get; set; }
            public string? StoreName { get; set; }
            public bool IsPrimary { get; set; }
        }

        private sealed record LinkedUserStoreDisplay(string StoreCode, string StoreName);

        private async Task<Dictionary<string, LinkedUserStoreDisplay>> GetLinkedUserStoreDisplaysAsync(
            IEnumerable<string?> userGuids)
        {
            var normalizedUserGuids = userGuids
                .Where(userGuid => !string.IsNullOrWhiteSpace(userGuid))
                .Select(userGuid => userGuid!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (!normalizedUserGuids.Any())
            {
                return new Dictionary<string, LinkedUserStoreDisplay>(StringComparer.OrdinalIgnoreCase);
            }

            var rows = await _db.Queryable<UserStore>()
                .InnerJoin<Store>((us, s) => us.StoreGUID == s.StoreGUID)
                .Where((us, s) =>
                    normalizedUserGuids.Contains(us.UserGUID) &&
                    !us.IsDeleted &&
                    s.IsActive &&
                    !s.IsDeleted)
                .Select((us, s) => new LinkedUserStoreRow
                {
                    UserGUID = us.UserGUID,
                    StoreCode = s.StoreCode,
                    StoreName = s.StoreName,
                    IsPrimary = us.IsPrimary,
                })
                .ToListAsync();

            // 关键逻辑：界面主门店展示按后台用户关联分店生成，旧 CashRegisterUsers.StoreCode 只保留为兼容字段。
            return rows
                .Where(row => !string.IsNullOrWhiteSpace(row.UserGUID))
                .GroupBy(row => row.UserGUID, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group =>
                    {
                        var orderedRows = group
                            .OrderByDescending(row => row.IsPrimary)
                            .ThenBy(row => row.StoreCode, StringComparer.OrdinalIgnoreCase)
                            .ToList();
                        var storeCodes = NormalizeStoreCodes(orderedRows.Select(row => row.StoreCode));
                        var storeNames = orderedRows
                            .Select(row => string.IsNullOrWhiteSpace(row.StoreName)
                                ? row.StoreCode
                                : row.StoreName)
                            .Where(name => !string.IsNullOrWhiteSpace(name))
                            .Select(name => name!.Trim())
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();

                        return new LinkedUserStoreDisplay(
                            string.Join(", ", storeCodes),
                            string.Join(", ", storeNames));
                    },
                    StringComparer.OrdinalIgnoreCase);
        }

        private async Task<List<string>> GetUserGuidsForStoreCodesAsync(
            IEnumerable<string?> storeCodes
        )
        {
            var normalizedStoreCodes = NormalizeStoreCodes(storeCodes);
            if (!normalizedStoreCodes.Any())
            {
                return new List<string>();
            }

            return (
                    await _db.Queryable<UserStore>()
                        .InnerJoin<Store>((us, s) => us.StoreGUID == s.StoreGUID)
                        .Where(
                            (us, s) =>
                                normalizedStoreCodes.Contains(s.StoreCode)
                                && !us.IsDeleted
                                && s.IsActive
                                && !s.IsDeleted
                        )
                        .Select((us, s) => us.UserGUID)
                        .ToListAsync()
                )
                .Where(userGuid => !string.IsNullOrWhiteSpace(userGuid))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private async Task<List<string>> GetUserGuidsForStoreCodeFilterAsync(
            string filterValue,
            string op
        )
        {
            var keyword = filterValue.Trim();
            if (keyword.Length == 0)
            {
                return new List<string>();
            }

            var query = _db.Queryable<UserStore>()
                .InnerJoin<Store>((us, s) => us.StoreGUID == s.StoreGUID)
                .Where((us, s) => !us.IsDeleted && s.IsActive && !s.IsDeleted);
            query = op == "equals"
                ? query.Where((us, s) => s.StoreCode == keyword)
                : query.Where((us, s) => s.StoreCode != null && s.StoreCode.Contains(keyword));

            return (await query.Select((us, s) => us.UserGUID).ToListAsync())
                .Where(userGuid => !string.IsNullOrWhiteSpace(userGuid))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public async Task<ApiResponse<List<CashRegisterUserUserOptionDto>>> GetUserOptionsAsync()
        {
            try
            {
                var managerStoreCodes = await GetCurrentUserStoreCodesAsync();
                var isAdmin = IsAdmin();

                var query = _db.Queryable<User>()
                    .Where(user => user.IsActive && !user.IsDeleted);

                if (!isAdmin)
                {
                    // 关键逻辑：收银条码页不能借用 Users.View，候选用户必须沿用本页的可管理门店范围。
                    var scopedUserGuids = await GetUserGuidsForStoreCodesAsync(managerStoreCodes);
                    if (!scopedUserGuids.Any())
                    {
                        return ApiResponse<List<CashRegisterUserUserOptionDto>>.OK(
                            new List<CashRegisterUserUserOptionDto>(),
                            "获取成功");
                    }

                    query = query.Where(user => scopedUserGuids.Contains(user.UserGUID));
                }

                var users = await query
                    .OrderBy(user => user.Username)
                    .Select(user => new CashRegisterUserUserOptionDto
                    {
                        UserGUID = user.UserGUID,
                        Username = user.Username,
                        UserFullName = user.FullName,
                    })
                    .ToListAsync();

                return ApiResponse<List<CashRegisterUserUserOptionDto>>.OK(users, "获取成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取收银条码可关联用户失败");
                return ApiResponse<List<CashRegisterUserUserOptionDto>>.Error("获取可关联用户失败");
            }
        }

        private static ISugarQueryable<CashRegisterUser> ApplyLinkedUserScope(
            ISugarQueryable<CashRegisterUser> query,
            IReadOnlyCollection<string> userGuids
        )
        {
            if (!userGuids.Any())
            {
                return query.Where(_ => false);
            }

            // 关键逻辑：收银条码旧 StoreCode 只做兼容显示，管理授权按条码关联后台用户的分店关系判断。
            return query.Where(u => u.UserGUID != null && userGuids.Contains(u.UserGUID));
        }

        private async Task<ISugarQueryable<CashRegisterUser>> ApplyManagerScopeAsync(
            ISugarQueryable<CashRegisterUser> query,
            bool isAdmin,
            IReadOnlyCollection<string> managerStoreCodes
        )
        {
            if (isAdmin)
            {
                return query;
            }

            return ApplyLinkedUserScope(query, await GetUserGuidsForStoreCodesAsync(managerStoreCodes));
        }

        private async Task<bool> IsUserInManagerScopeAsync(
            string userGuid,
            bool isAdmin,
            IReadOnlyCollection<string> managerStoreCodes
        )
        {
            if (isAdmin)
            {
                return true;
            }

            var scopedUserGuids = await GetUserGuidsForStoreCodesAsync(managerStoreCodes);
            return scopedUserGuids.Contains(userGuid, StringComparer.OrdinalIgnoreCase);
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

                baseQuery = await ApplyManagerScopeAsync(baseQuery, isAdmin, userStoreCodes);

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
                                    var storeFilteredUserGuids =
                                        await GetUserGuidsForStoreCodeFilterAsync(v, op);
                                    baseQuery = ApplyLinkedUserScope(baseQuery, storeFilteredUserGuids);
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
                        LegacyStoreCode = u.StoreCode,
                        UserGUID = u.UserGUID,
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

                var userGuids = resultList
                    .Where(r => !string.IsNullOrWhiteSpace(r.UserGUID))
                    .Select(r => r.UserGUID!)
                    .Distinct()
                    .ToList();
                if (userGuids.Any())
                {
                    var users = await _db.Queryable<User>()
                        .Where(u => userGuids.Contains(u.UserGUID))
                        .ToListAsync();
                    var userDict = users.ToDictionary(u => u.UserGUID, StringComparer.OrdinalIgnoreCase);
                    foreach (var item in resultList)
                    {
                        if (
                            !string.IsNullOrWhiteSpace(item.UserGUID)
                            && userDict.TryGetValue(item.UserGUID, out var user)
                        )
                        {
                            item.Username = user.Username;
                            item.UserFullName = user.FullName;
                        }
                    }

                    var storeDisplayDict = await GetLinkedUserStoreDisplaysAsync(userGuids);
                    foreach (var item in resultList)
                    {
                        if (
                            !string.IsNullOrWhiteSpace(item.UserGUID)
                            && storeDisplayDict.TryGetValue(item.UserGUID, out var storeDisplay)
                        )
                        {
                            item.StoreCode = storeDisplay.StoreCode;
                            item.StoreName = storeDisplay.StoreName;
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

                query = await ApplyManagerScopeAsync(query, isAdmin, userStoreCodes);

                var entity = await query.FirstAsync();

                if (entity == null)
                {
                    return ApiResponse<CashRegisterUserDetailDto>.Error("收银用户不存在");
                }

                User? linkedUser = null;
                if (!string.IsNullOrWhiteSpace(entity.UserGUID))
                {
                    linkedUser = await _db.Queryable<User>()
                        .Where(u => u.UserGUID == entity.UserGUID)
                        .FirstAsync();
                }

                var storeDisplayDict = await GetLinkedUserStoreDisplaysAsync([entity.UserGUID]);
                storeDisplayDict.TryGetValue(entity.UserGUID ?? string.Empty, out var storeDisplay);

                var result = new CashRegisterUserDetailDto
                {
                    Id = entity.Id,
                    HGUID = entity.HGUID ?? string.Empty,
                    StoreCode = storeDisplay?.StoreCode,
                    StoreName = storeDisplay?.StoreName,
                    LegacyStoreCode = entity.StoreCode,
                    UserGUID = entity.UserGUID,
                    Username = linkedUser?.Username,
                    UserFullName = linkedUser?.FullName,
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

        private async Task<User?> GetEnabledUserAsync(string? userGuid)
        {
            if (string.IsNullOrWhiteSpace(userGuid))
            {
                return null;
            }

            return await _db.Queryable<User>()
                .Where(u => u.UserGUID == userGuid && u.IsActive && !u.IsDeleted)
                .FirstAsync();
        }

        private async Task DeactivateOtherActiveCashiersAsync(
            string? userGuid,
            string currentHGuid,
            string updatedBy)
        {
            if (string.IsNullOrWhiteSpace(userGuid))
            {
                return;
            }

            // 关键逻辑：同一后台用户只保留一条有效收银条码；后续如有并发竞争再补数据库过滤索引。
            await _db.Updateable<CashRegisterUser>()
                .SetColumns(u => new CashRegisterUser
                {
                    Status = false,
                    LastModifier = updatedBy,
                    LastModifyDate = DateTime.UtcNow,
                })
                .Where(u => u.UserGUID == userGuid && u.HGUID != currentHGuid && u.Status)
                .ExecuteCommandAsync();
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

                if (!isAdmin)
                {
                    dto.StoreCode = userStoreCode;
                }

                if (string.IsNullOrEmpty(dto.StoreCode))
                {
                    return ApiResponse<CashRegisterUserDetailDto>.Error("分店代码不能为空");
                }

                var normalizedUserBarcode = dto.UserBarcode?.Trim();
                if (string.IsNullOrEmpty(normalizedUserBarcode))
                {
                    return ApiResponse<CashRegisterUserDetailDto>.Error("用户条码不能为空");
                }

                var linkedUser = await GetEnabledUserAsync(dto.UserGUID);
                if (linkedUser == null)
                {
                    return ApiResponse<CashRegisterUserDetailDto>.Error("请选择启用的后台用户");
                }

                if (!await IsUserInManagerScopeAsync(linkedUser.UserGUID, isAdmin, userStoreCodes))
                {
                    return ApiResponse<CashRegisterUserDetailDto>.Error("只能管理自己分店关联用户的收银条码");
                }

                var now = DateTime.UtcNow;
                var entity = new CashRegisterUser
                {
                    HGUID = Guid.NewGuid().ToString("N"),
                    StoreCode = dto.StoreCode ?? string.Empty,
                    UserGUID = linkedUser.UserGUID,
                    OperatorUser = dto.OperatorUser ?? string.Empty,
                    UserBarcode = normalizedUserBarcode,
                    LoginRole = dto.LoginRole ?? string.Empty,
                    Remark = dto.Remark ?? string.Empty,
                    PrintCount = 0,
                    Status = dto.Status,
                    Creator = createdBy,
                    CreateDate = now,
                    LastModifier = createdBy,
                    LastModifyDate = now,
                };

                await _db.Ado.BeginTranAsync();
                try
                {
                    var existing = await _db.Queryable<CashRegisterUser>()
                        .Where(u => u.UserBarcode == entity.UserBarcode && u.Status)
                        .FirstAsync();
                    if (existing != null)
                    {
                        await _db.Ado.RollbackTranAsync();
                        return ApiResponse<CashRegisterUserDetailDto>.Error("用户条码已存在");
                    }

                    if (entity.Status)
                    {
                        await DeactivateOtherActiveCashiersAsync(entity.UserGUID, entity.HGUID, createdBy);
                    }

                    await _db.Insertable(entity).ExecuteCommandAsync();
                    await _db.Ado.CommitTranAsync();
                }
                catch
                {
                    await _db.Ado.RollbackTranAsync();
                    throw;
                }

                var storeDisplayDict = await GetLinkedUserStoreDisplaysAsync([entity.UserGUID]);
                storeDisplayDict.TryGetValue(entity.UserGUID ?? string.Empty, out var storeDisplay);

                var result = new CashRegisterUserDetailDto
                {
                    Id = entity.Id,
                    HGUID = entity.HGUID ?? string.Empty,
                    StoreCode = storeDisplay?.StoreCode,
                    StoreName = storeDisplay?.StoreName,
                    LegacyStoreCode = entity.StoreCode,
                    UserGUID = entity.UserGUID,
                    Username = linkedUser?.Username,
                    UserFullName = linkedUser?.FullName,
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

                query = await ApplyManagerScopeAsync(query, isAdmin, userStoreCodes);

                var entity = await query.FirstAsync();

                if (entity == null)
                {
                    return ApiResponse<CashRegisterUserDetailDto>.Error("收银用户不存在");
                }

                var normalizedUserBarcode = dto.UserBarcode?.Trim();
                if (dto.UserBarcode is not null && string.IsNullOrEmpty(normalizedUserBarcode))
                {
                    return ApiResponse<CashRegisterUserDetailDto>.Error("用户条码不能为空");
                }

                if (!isAdmin)
                {
                    entity.StoreCode = await GetCurrentUserStoreCodeAsync() ?? entity.StoreCode;
                }
                else if (!string.IsNullOrEmpty(dto.StoreCode))
                {
                    entity.StoreCode = dto.StoreCode;
                }

                var linkedUser = await GetEnabledUserAsync(dto.UserGUID);
                if (linkedUser == null)
                {
                    return ApiResponse<CashRegisterUserDetailDto>.Error("请选择启用的后台用户");
                }

                if (!await IsUserInManagerScopeAsync(linkedUser.UserGUID, isAdmin, userStoreCodes))
                {
                    return ApiResponse<CashRegisterUserDetailDto>.Error("只能管理自己分店关联用户的收银条码");
                }

                entity.UserGUID = linkedUser.UserGUID;
                entity.OperatorUser = dto.OperatorUser ?? string.Empty;
                entity.LoginRole = dto.LoginRole ?? string.Empty;
                entity.Remark = dto.Remark ?? string.Empty;
                entity.Status = dto.Status;
                entity.LastModifier = updatedBy;
                entity.LastModifyDate = DateTime.UtcNow;

                await _db.Ado.BeginTranAsync();
                try
                {
                    if (!string.IsNullOrEmpty(normalizedUserBarcode) && normalizedUserBarcode != entity.UserBarcode)
                    {
                        var existing = await _db.Queryable<CashRegisterUser>()
                            .Where(u => u.UserBarcode == normalizedUserBarcode && u.HGUID != hGuid && u.Status)
                            .FirstAsync();
                        if (existing != null)
                        {
                            await _db.Ado.RollbackTranAsync();
                            return ApiResponse<CashRegisterUserDetailDto>.Error("用户条码已存在");
                        }

                        entity.UserBarcode = normalizedUserBarcode;
                    }

                    if (entity.Status)
                    {
                        await DeactivateOtherActiveCashiersAsync(entity.UserGUID, entity.HGUID, updatedBy);
                    }

                    await _db.Updateable(entity).ExecuteCommandAsync();
                    await _db.Ado.CommitTranAsync();
                }
                catch
                {
                    await _db.Ado.RollbackTranAsync();
                    throw;
                }

                var storeDisplayDict = await GetLinkedUserStoreDisplaysAsync([entity.UserGUID]);
                storeDisplayDict.TryGetValue(entity.UserGUID ?? string.Empty, out var storeDisplay);

                var result = new CashRegisterUserDetailDto
                {
                    Id = entity.Id,
                    HGUID = entity.HGUID ?? string.Empty,
                    StoreCode = storeDisplay?.StoreCode,
                    StoreName = storeDisplay?.StoreName,
                    LegacyStoreCode = entity.StoreCode,
                    UserGUID = entity.UserGUID,
                    Username = linkedUser.Username,
                    UserFullName = linkedUser.FullName,
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

                query = await ApplyManagerScopeAsync(query, isAdmin, userStoreCodes);

                var entity = await query.FirstAsync();

                if (entity == null)
                {
                    return ApiResponse<bool>.Error("收银用户不存在");
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

                query = await ApplyManagerScopeAsync(query, isAdmin, userStoreCodes);

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
