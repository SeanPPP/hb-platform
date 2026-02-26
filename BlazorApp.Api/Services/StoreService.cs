using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace BlazorApp.Api.Services
{
    /// <summary>
    /// 分店服务实现
    /// 提供分店管理的增删改查、状态控制、用户关联管理等核心业务功能
    /// 支持多分店体系、用户分配、批量用户操作等功能
    /// </summary>
    public class StoreService : IStoreService
    {
        // 数据库上下文，用于数据库操作
        private readonly SqlSugarContext _context;

        // 日志记录器，用于记录操作日志和错误信息
        private readonly ILogger<StoreService> _logger;

        /// <summary>
        /// 构造函数：初始化分店服务
        /// </summary>
        /// <param name="context">数据库上下文</param>
        /// <param name="logger">日志记录器</param>
        public StoreService(SqlSugarContext context, ILogger<StoreService> logger)
        {
            _context = context;
            _logger = logger;
        }

        /// <summary>
        /// 获取所有未删除的分店列表（按名称排序）
        /// </summary>
        /// <returns>分店列表</returns>
        public async Task<ApiResponse<List<StoreDto>>> GetAllStoresByNameAsync()
        {
            try
            {
                var db = _context.Db;

                var stores = await db.Queryable<Store>()
                    .Where(s => s.IsDeleted == false)
                    .OrderBy(s => s.StoreName)
                    .Select(s => new StoreDto
                    {
                        StoreGUID = s.StoreGUID,
                        StoreName = s.StoreName,
                        StoreCode = s.StoreCode,
                        ABN = s.ABN,
                        BrandName = s.BrandName,
                        Address = s.Address,
                        ContactPhone = s.Phone,
                        IsActive = s.IsActive,
                        CreatedAt = s.CreatedAt,
                        UpdatedAt = s.UpdatedAt ?? s.CreatedAt,
                    })
                    .ToListAsync();

                return ApiResponse<List<StoreDto>>.OK(stores, "获取所有分店列表成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取所有分店列表失败");
                return ApiResponse<List<StoreDto>>.Error(
                    "获取所有分店列表失败",
                    "GET_ALL_STORES_ERROR"
                );
            }
        }

        /// <summary>
        /// 获取所有激活的分店列表（用于数据同步分店选择）
        /// </summary>
        /// <returns>激活的分店列表</returns>
        public async Task<ApiResponse<List<StoreDto>>> GetActiveStoresAsync()
        {
            try
            {
                var db = _context.Db;

                // 查询所有激活状态的分店
                var activeStores = await db.Queryable<Store>()
                    .Where(s => s.IsActive == true && s.IsDeleted == false)
                    .Select(s => new StoreDto
                    {
                        StoreGUID = s.StoreGUID,
                        StoreName = s.StoreName,
                        StoreCode = s.StoreCode,
                        ABN = s.ABN,
                        BrandName = s.BrandName,
                        Address = s.Address,
                        ContactPhone = s.Phone,
                        IsActive = s.IsActive,
                    })
                    .OrderBy(s => s.StoreCode)
                    .ToListAsync();

                _logger.LogInformation($"成功获取 {activeStores.Count} 个激活分店");

                return ApiResponse<List<StoreDto>>.OK(activeStores, "获取激活分店列表成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取激活分店列表失败");
                return ApiResponse<List<StoreDto>>.Error(
                    "获取激活分店列表失败",
                    "STORE_QUERY_ERROR"
                );
            }
        }

        /// <summary>
        /// 获取分店列表
        /// 支持分店搜索、状态筛选、用户关联筛选，包含用户统计信息
        /// 采用分步查询优化性能，避免复杂的连表统计查询
        /// </summary>
        /// <param name="query">分店查询条件</param>
        /// <returns>分页的分店列表，包含用户统计信息</returns>
        public async Task<ApiResponse<PagedResult<StoreDto>>> GetStoresAsync(StoreQueryDto query)
        {
            try
            {
                var db = _context.Db;

                // 1. 构建基础分店查询
                var storeQuery = db.Queryable<Store>();

                // 2. 关键字搜索条件 - 支持分店名称和分店代码模糊匹配
                if (!string.IsNullOrEmpty(query.Search))
                {
                    storeQuery = storeQuery.Where(s =>
                        s.StoreName.Contains(query.Search) || s.StoreCode.Contains(query.Search)
                    );
                }

                // 3. 分店状态筛选条件
                if (query.IsActive.HasValue)
                {
                    storeQuery = storeQuery.Where(s => s.IsActive == query.IsActive.Value);
                }

                // 4. 用户关联筛选条件 - 使用子查询优化性能
                if (!string.IsNullOrEmpty(query.UserGUID))
                {
                    var userStoreGuids = await db.Queryable<UserStore>()
                        .Where(us => us.UserGUID == query.UserGUID)
                        .Select(us => us.StoreGUID)
                        .ToListAsync();
                    storeQuery = storeQuery.Where(s => userStoreGuids.Contains(s.StoreGUID));
                }

                // 5. 获取符合条件的总记录数
                var total = await storeQuery.CountAsync();

                // 6. 应用排序规则
                storeQuery = ApplySorting(storeQuery, query.SortField, query.SortOrder);

                // 7. 获取当前页分店GUID列表，用于后续的用户统计查询
                var storeGuids = await storeQuery
                    .Skip((query.Page - 1) * query.PageSize) // 分页跳过
                    .Take(query.PageSize) // 分页取数量
                    .Select(s => s.StoreGUID) // 只选择GUID
                    .ToListAsync();

                // 7. 批量获取用户统计信息 - 分步查询优化性能
                // 7.1 获取每个分店的用户总数
                var totalUserStats = await db.Queryable<UserStore>()
                    .Where(us => storeGuids.Contains(us.StoreGUID))
                    .GroupBy(us => us.StoreGUID)
                    .Select(group => new
                    {
                        StoreGUID = group.StoreGUID,
                        TotalUsers = SqlFunc.AggregateCount(group.StoreGUID), // 用户总数
                    })
                    .ToListAsync();

                // 7.2 获取每个分店的活跃用户数
                var activeUserStats = await db.Queryable<UserStore>()
                    .InnerJoin<User>((us, u) => us.UserGUID == u.UserGUID)
                    .Where((us, u) => storeGuids.Contains(us.StoreGUID) && u.IsActive)
                    .GroupBy(us => us.StoreGUID)
                    .Select(us => new
                    {
                        StoreGUID = us.StoreGUID,
                        ActiveUsers = SqlFunc.AggregateCount(us.StoreGUID), // 活跃用户数
                    })
                    .ToListAsync();

                var userStatsDict = totalUserStats.ToDictionary(
                    x => x.StoreGUID,
                    x => new { x.TotalUsers, ActiveUsers = 0 }
                );
                foreach (var activeStat in activeUserStats)
                {
                    if (userStatsDict.ContainsKey(activeStat.StoreGUID))
                    {
                        userStatsDict[activeStat.StoreGUID] = new
                        {
                            userStatsDict[activeStat.StoreGUID].TotalUsers,
                            activeStat.ActiveUsers,
                        };
                    }
                }

                // 获取分店详情，保持与查询相同的排序顺序
                var storeDetailQuery = db.Queryable<Store>()
                    .Where(s => storeGuids.Contains(s.StoreGUID));

                // 应用相同的排序规则
                storeDetailQuery = ApplySorting(storeDetailQuery, query.SortField, query.SortOrder);

                var stores = await storeDetailQuery
                    .Select(s => new StoreDto
                    {
                        StoreGUID = s.StoreGUID,
                        StoreName = s.StoreName,
                        StoreCode = s.StoreCode,
                        ABN = s.ABN,
                        BrandName = s.BrandName,
                        Description = null,
                        Address = s.Address,
                        ContactPhone = s.Phone,
                        ContactEmail = null,
                        IsActive = s.IsActive,
                        CreatedAt = s.CreatedAt,
                        UpdatedAt = s.UpdatedAt ?? s.CreatedAt,
                        TotalUsers = 0, // 将在内存中填充
                        ActiveUsers = 0, // 将在内存中填充
                    })
                    .ToListAsync();

                // 在内存中填充用户统计信息
                foreach (var store in stores)
                {
                    if (userStatsDict.ContainsKey(store.StoreGUID))
                    {
                        store.TotalUsers = userStatsDict[store.StoreGUID].TotalUsers;
                        store.ActiveUsers = userStatsDict[store.StoreGUID].ActiveUsers;
                    }
                }

                var result = new PagedResult<StoreDto>
                {
                    Items = stores,
                    Total = total,
                    Page = query.Page,
                    PageSize = query.PageSize,
                };

                return ApiResponse<PagedResult<StoreDto>>.OK(result, "获取分店列表成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取分店列表失败");
                return ApiResponse<PagedResult<StoreDto>>.Error(
                    "获取分店列表失败",
                    "GET_STORES_ERROR"
                );
            }
        }

        /// <summary>
        /// 根据GUID获取分店详情
        /// </summary>
        public async Task<ApiResponse<StoreDetailDto>> GetStoreByGuidAsync(string guid)
        {
            try
            {
                var db = _context.Db;
                var store = await db.Queryable<Store>()
                    .Where(s => s.StoreGUID == guid)
                    .FirstAsync();

                if (store == null)
                {
                    return ApiResponse<StoreDetailDto>.Error("分店不存在", "STORE_NOT_FOUND");
                }

                return await GetStoreDetailAsync(store);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取分店详情失败，GUID: {StoreGUID}", guid);
                return ApiResponse<StoreDetailDto>.Error(
                    "获取分店详情失败",
                    "GET_STORE_DETAIL_ERROR"
                );
            }
        }

        /// <summary>
        /// 根据分店代码获取分店信息
        /// </summary>
        public async Task<ApiResponse<StoreDto>> GetStoreByCodeAsync(string storeCode)
        {
            try
            {
                var db = _context.Db;
                var store = await db.Queryable<Store>()
                    .Where(s => s.StoreCode == storeCode)
                    .FirstAsync();

                if (store == null)
                {
                    _logger.LogWarning("根据分店代码未找到分店: {StoreCode}", storeCode);
                    return ApiResponse<StoreDto>.Error("分店不存在或已停用", "STORE_NOT_FOUND");
                }

                var result = new StoreDto
                {
                    StoreGUID = store.StoreGUID,
                    StoreName = store.StoreName,
                    StoreCode = store.StoreCode,
                    ABN = store.ABN,
                    BrandName = store.BrandName,
                    Address = store.Address,
                    ContactPhone = store.Phone,
                    IsActive = store.IsActive,
                    CreatedAt = store.CreatedAt,
                    UpdatedAt = store.UpdatedAt ?? DateTime.UtcNow,
                };

                _logger.LogInformation(
                    "成功根据分店代码获取分店信息: {StoreCode} -> {StoreName}",
                    storeCode,
                    store.StoreName
                );
                return ApiResponse<StoreDto>.OK(result, "获取分店信息成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "根据分店代码获取分店信息失败: {StoreCode}", storeCode);
                return ApiResponse<StoreDto>.Error("获取分店信息失败", "GET_STORE_BY_CODE_ERROR");
            }
        }

        /// <summary>
        /// 根据GUID更新分店
        /// </summary>
        public async Task<ApiResponse<StoreDto>> UpdateStoreByGuidAsync(
            string guid,
            UpdateStoreDto dto
        )
        {
            try
            {
                var db = _context.Db;

                var store = await db.Queryable<Store>()
                    .Where(s => s.StoreGUID == guid)
                    .FirstAsync();

                if (store == null)
                {
                    return ApiResponse<StoreDto>.Error("分店不存在", "STORE_NOT_FOUND");
                }

                // 检查分店代码是否重复（排除当前分店）
                var existingStore = await db.Queryable<Store>()
                    .Where(s => s.StoreCode == dto.StoreCode && s.StoreGUID != guid)
                    .FirstAsync();

                if (existingStore != null)
                {
                    return ApiResponse<StoreDto>.Error(
                        "分店代码已存在",
                        "DUPLICATE_STORE_CODE",
                        new { storeCode = dto.StoreCode }
                    );
                }

                // 更新分店信息
                store.StoreName = dto.StoreName;
                store.StoreCode = dto.StoreCode;
                store.ABN = dto.ABN;
                store.BrandName = dto.BrandName;
                store.Address = dto.Address;
                store.Phone = dto.ContactPhone;
                store.IsActive = dto.IsActive;
                store.UpdatedAt = DateTime.UtcNow;

                await db.Updateable(store).ExecuteCommandAsync();

                var result = new StoreDto
                {
                    StoreGUID = store.StoreGUID,
                    StoreName = store.StoreName,
                    StoreCode = store.StoreCode,
                    ABN = store.ABN,
                    BrandName = store.BrandName,
                    Description = null,
                    Address = store.Address,
                    ContactPhone = store.Phone,
                    ContactEmail = null,
                    IsActive = store.IsActive,
                    CreatedAt = store.CreatedAt,
                    UpdatedAt = store.UpdatedAt ?? store.CreatedAt,
                };

                return ApiResponse<StoreDto>.OK(result, "更新分店成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新分店失败，GUID: {StoreGUID}", guid);
                return ApiResponse<StoreDto>.Error("更新分店失败", "UPDATE_STORE_ERROR");
            }
        }

        /// <summary>
        /// 根据GUID删除分店
        /// </summary>
        public async Task<ApiResponse<bool>> DeleteStoreByGuidAsync(string guid)
        {
            try
            {
                var db = _context.Db;

                // 检查分店是否存在
                var store = await db.Queryable<Store>()
                    .Where(s => s.StoreGUID == guid)
                    .FirstAsync();

                if (store == null)
                {
                    return ApiResponse<bool>.Error("分店不存在", "STORE_NOT_FOUND");
                }

                // 检查是否有关联用户
                var userCount = await db.Queryable<UserStore>()
                    .Where(us => us.StoreGUID == guid)
                    .CountAsync();

                if (userCount > 0)
                {
                    return ApiResponse<bool>.Error(
                        "无法删除有关联用户的分店",
                        "STORE_HAS_USERS",
                        new { userCount }
                    );
                }

                await db.Deleteable<Store>().Where(s => s.StoreGUID == guid).ExecuteCommandAsync();

                return ApiResponse<bool>.OK(true, "删除分店成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除分店失败，GUID: {StoreGUID}", guid);
                return ApiResponse<bool>.Error("删除分店失败", "DELETE_STORE_ERROR");
            }
        }

        /// <summary>
        /// 根据GUID更新分店状态
        /// </summary>
        public async Task<ApiResponse<bool>> UpdateStoreStatusByGuidAsync(
            string guid,
            bool isActive
        )
        {
            try
            {
                var db = _context.Db;

                var store = await db.Queryable<Store>()
                    .Where(s => s.StoreGUID == guid)
                    .FirstAsync();

                if (store == null)
                {
                    return ApiResponse<bool>.Error("分店不存在", "STORE_NOT_FOUND");
                }

                store.IsActive = isActive;
                store.UpdatedAt = DateTime.UtcNow;

                await db.Updateable(store).ExecuteCommandAsync();

                return ApiResponse<bool>.OK(true, $"{(isActive ? "激活" : "停用")}分店成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新分店状态失败，GUID: {StoreGUID}", guid);
                return ApiResponse<bool>.Error("更新分店状态失败", "UPDATE_STORE_STATUS_ERROR");
            }
        }

        /// <summary>
        /// 获取分店用户列表
        /// </summary>
        public async Task<ApiResponse<PagedResult<StoreUserDto>>> GetStoreUsersAsync(
            string storeGuid,
            UserQueryDto query
        )
        {
            try
            {
                var db = _context.Db;

                // 检查分店是否存在
                var store = await db.Queryable<Store>()
                    .Where(s => s.StoreGUID == storeGuid)
                    .FirstAsync();

                if (store == null)
                {
                    return ApiResponse<PagedResult<StoreUserDto>>.Error(
                        "分店不存在",
                        "STORE_NOT_FOUND"
                    );
                }

                var userQuery = db.Queryable<UserStore>()
                    .InnerJoin<User>((us, u) => us.UserGUID == u.UserGUID)
                    .Where((us, u) => us.StoreGUID == storeGuid);

                // 搜索条件
                if (!string.IsNullOrEmpty(query.Search))
                {
                    userQuery = userQuery.Where(
                        (us, u) =>
                            u.Username.Contains(query.Search) || u.Email.Contains(query.Search)
                    );
                }

                // 获取总数
                var total = await userQuery.CountAsync();

                // 分页查询
                var users = await userQuery
                    .OrderByDescending((us, u) => us.AssignedAt)
                    .Skip((query.Page - 1) * query.PageSize)
                    .Take(query.PageSize)
                    .Select(us => new StoreUserDto
                    {
                        UserGUID = us.User.UserGUID,
                        Username = us.User.Username,
                        Email = us.User.Email,
                        Phone = string.Empty, // User模型中没有Phone字段
                        IsPrimary = false, // UserStore模型中没有IsPrimary字段
                        AssignedAt = us.AssignedAt,
                    })
                    .ToListAsync();

                var result = new PagedResult<StoreUserDto>
                {
                    Items = users,
                    Total = total,
                    Page = query.Page,
                    PageSize = query.PageSize,
                };

                return ApiResponse<PagedResult<StoreUserDto>>.OK(result, "获取分店用户列表成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取分店用户列表失败，StoreGUID: {StoreGUID}", storeGuid);
                return ApiResponse<PagedResult<StoreUserDto>>.Error(
                    "获取分店用户列表失败",
                    "GET_STORE_USERS_ERROR"
                );
            }
        }

        /// <summary>
        /// 为分店添加用户
        /// 建立用户与分店的关联关系，支持用户分配到指定分店
        /// 包含重复关联检查，防止重复分配
        /// </summary>
        /// <param name="storeGuid">分店GUID</param>
        /// <param name="dto">添加用户DTO，包含用户GUID</param>
        /// <returns>操作结果</returns>
        public async Task<ApiResponse<bool>> AddUserToStoreAsync(
            string storeGuid,
            AddUserToStoreDto dto
        )
        {
            try
            {
                var db = _context.Db;

                // 1. 验证分店是否存在
                var store = await db.Queryable<Store>()
                    .Where(s => s.StoreGUID == storeGuid)
                    .FirstAsync();

                if (store == null)
                {
                    return ApiResponse<bool>.Error("分店不存在", "STORE_NOT_FOUND");
                }

                // 2. 验证用户是否存在
                var user = await db.Queryable<User>()
                    .Where(u => u.UserGUID == dto.UserGUID)
                    .FirstAsync();

                if (user == null)
                {
                    return ApiResponse<bool>.Error("用户不存在", "USER_NOT_FOUND");
                }

                // 3. 检查用户是否已经关联到该分店，防止重复分配
                var existingUserStore = await db.Queryable<UserStore>()
                    .Where(us => us.StoreGUID == storeGuid && us.UserGUID == dto.UserGUID)
                    .FirstAsync();

                if (existingUserStore != null)
                {
                    return ApiResponse<bool>.Error("用户已关联到该分店", "USER_ALREADY_ASSIGNED");
                }

                // 4. 创建用户分店关联记录
                var userStore = new UserStore
                {
                    UserGUID = dto.UserGUID, // 用户GUID
                    StoreGUID = storeGuid, // 分店GUID
                    UserStoreGUID = Guid.NewGuid().ToString(), // 关联记录GUID
                    AssignedAt = DateTime.UtcNow, // 分配时间
                };

                // 5. 插入关联记录到数据库
                await db.Insertable(userStore).ExecuteCommandAsync();

                return ApiResponse<bool>.OK(true, "添加用户到分店成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "添加用户到分店失败，StoreGUID: {StoreGUID}, UserGUID: {UserGUID}",
                    storeGuid,
                    dto.UserGUID
                );
                return ApiResponse<bool>.Error("添加用户到分店失败", "ADD_USER_TO_STORE_ERROR");
            }
        }

        /// <summary>
        /// 从分店移除用户
        /// </summary>
        public async Task<ApiResponse<bool>> RemoveUserFromStoreAsync(
            string storeGuid,
            string userGuid
        )
        {
            try
            {
                var db = _context.Db;

                // 检查分店是否存在
                var store = await db.Queryable<Store>()
                    .Where(s => s.StoreGUID == storeGuid)
                    .FirstAsync();

                if (store == null)
                {
                    return ApiResponse<bool>.Error("分店不存在", "STORE_NOT_FOUND");
                }

                // 检查用户关联是否存在
                var userStore = await db.Queryable<UserStore>()
                    .Where(us => us.StoreGUID == storeGuid && us.UserGUID == userGuid)
                    .FirstAsync();

                if (userStore == null)
                {
                    return ApiResponse<bool>.Error("用户未关联到该分店", "USER_NOT_ASSIGNED");
                }

                // 检查是否为最后一个用户
                var userCount = await db.Queryable<UserStore>()
                    .Where(us => us.StoreGUID == storeGuid)
                    .CountAsync();

                if (userCount <= 1)
                {
                    return ApiResponse<bool>.Error(
                        "无法移除分店的最后一个用户",
                        "CANNOT_REMOVE_LAST_USER"
                    );
                }

                await db.Deleteable<UserStore>()
                    .Where(us => us.StoreGUID == storeGuid && us.UserGUID == userGuid)
                    .ExecuteCommandAsync();

                return ApiResponse<bool>.OK(true, "从分店移除用户成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "从分店移除用户失败，StoreGUID: {StoreGUID}, UserGUID: {UserGUID}",
                    storeGuid,
                    userGuid
                );
                return ApiResponse<bool>.Error(
                    "从分店移除用户失败",
                    "REMOVE_USER_FROM_STORE_ERROR"
                );
            }
        }

        /// <summary>
        /// 设置主要用户
        /// </summary>
        public async Task<ApiResponse<bool>> SetPrimaryUserAsync(
            string storeGuid,
            string userGuid,
            bool isPrimary
        )
        {
            try
            {
                var db = _context.Db;

                // 检查分店是否存在
                var store = await db.Queryable<Store>()
                    .Where(s => s.StoreGUID == storeGuid)
                    .FirstAsync();

                if (store == null)
                {
                    return ApiResponse<bool>.Error("分店不存在", "STORE_NOT_FOUND");
                }

                // 检查用户关联是否存在
                var userStore = await db.Queryable<UserStore>()
                    .Where(us => us.StoreGUID == storeGuid && us.UserGUID == userGuid)
                    .FirstAsync();

                if (userStore == null)
                {
                    return ApiResponse<bool>.Error("用户未关联到该分店", "USER_NOT_ASSIGNED");
                }

                // 注意：UserStore模型中没有IsPrimary字段，这里只是示例
                // 实际实现需要根据具体的业务需求来设计

                return ApiResponse<bool>.OK(true, $"{(isPrimary ? "设置" : "取消")}主要用户成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "设置主要用户失败，StoreGUID: {StoreGUID}, UserGUID: {UserGUID}",
                    storeGuid,
                    userGuid
                );
                return ApiResponse<bool>.Error("设置主要用户失败", "SET_PRIMARY_USER_ERROR");
            }
        }

        /// <summary>
        /// 批量管理用户
        /// 支持批量添加或移除用户与分店的关联关系
        /// 提高批量用户操作的效率
        /// </summary>
        /// <param name="storeGuid">分店GUID</param>
        /// <param name="dto">批量操作DTO，包含操作类型和用户GUID列表</param>
        /// <returns>操作结果</returns>
        public async Task<ApiResponse<bool>> BatchManageUsersAsync(
            string storeGuid,
            BatchUserOperationDto dto
        )
        {
            try
            {
                var db = _context.Db;

                // 检查分店是否存在
                var store = await db.Queryable<Store>()
                    .Where(s => s.StoreGUID == storeGuid)
                    .FirstAsync();

                if (store == null)
                {
                    return ApiResponse<bool>.Error("分店不存在", "STORE_NOT_FOUND");
                }

                // 根据操作类型执行相应的批量操作
                if (dto.Action.ToLower() == "add")
                {
                    // 1. 批量添加用户到分店
                    foreach (var userGuid in dto.UserGUIDs)
                    {
                        // 验证用户是否存在
                        var user = await db.Queryable<User>()
                            .Where(u => u.UserGUID == userGuid)
                            .FirstAsync();

                        if (user == null)
                            continue; // 跳过不存在的用户

                        // 检查关联是否已存在，避免重复添加
                        var existingUserStore = await db.Queryable<UserStore>()
                            .Where(us => us.StoreGUID == storeGuid && us.UserGUID == userGuid)
                            .FirstAsync();

                        if (existingUserStore == null)
                        {
                            // 创建新的用户分店关联
                            var userStore = new UserStore
                            {
                                UserGUID = userGuid,
                                StoreGUID = storeGuid,
                                UserStoreGUID = Guid.NewGuid().ToString(),
                                AssignedAt = DateTime.UtcNow,
                            };

                            await db.Insertable(userStore).ExecuteCommandAsync();
                        }
                    }
                }
                else if (dto.Action.ToLower() == "remove")
                {
                    // 2. 批量移除用户与分店的关联
                    foreach (var userGuid in dto.UserGUIDs)
                    {
                        // 检查关联是否存在
                        var userStore = await db.Queryable<UserStore>()
                            .Where(us => us.StoreGUID == storeGuid && us.UserGUID == userGuid)
                            .FirstAsync();

                        if (userStore != null)
                        {
                            // 删除用户分店关联
                            await db.Deleteable<UserStore>()
                                .Where(us => us.StoreGUID == storeGuid && us.UserGUID == userGuid)
                                .ExecuteCommandAsync();
                        }
                    }
                }

                return ApiResponse<bool>.OK(true, $"批量{dto.Action}用户成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "批量管理用户失败，StoreGUID: {StoreGUID}, Action: {Action}",
                    storeGuid,
                    dto.Action
                );
                return ApiResponse<bool>.Error("批量管理用户失败", "BATCH_MANAGE_USERS_ERROR");
            }
        }

        /// <summary>
        /// 获取分店详情的私有方法
        /// </summary>
        private async Task<ApiResponse<StoreDetailDto>> GetStoreDetailAsync(Store store)
        {
            try
            {
                var db = _context.Db;
                var storeDto = new StoreDto
                {
                    StoreGUID = store.StoreGUID,
                    StoreName = store.StoreName,
                    StoreCode = store.StoreCode,
                    ABN = store.ABN,
                    BrandName = store.BrandName,
                    Address = store.Address,
                    IsActive = store.IsActive,
                    CreatedAt = store.CreatedAt,
                    UpdatedAt = store.UpdatedAt ?? DateTime.UtcNow,
                };

                // 获取关联用户
                var users = await db.Queryable<UserStore>()
                    .InnerJoin<User>((us, u) => us.UserGUID == u.UserGUID)
                    .Where((us, u) => us.StoreGUID == store.StoreGUID)
                    .Select(
                        (us, u) =>
                            new StoreUserDto
                            {
                                UserGUID = u.UserGUID,
                                Username = u.Username,
                                Email = u.Email,
                                Phone = string.Empty,
                                IsPrimary = false,
                                AssignedAt = us.AssignedAt,
                            }
                    )
                    .ToListAsync();

                var result = new StoreDetailDto
                {
                    StoreGUID = store.StoreGUID,
                    StoreName = store.StoreName,
                    StoreCode = store.StoreCode,
                    ABN = store.ABN,
                    BrandName = store.BrandName,
                    Description = string.Empty, // Store实体没有Description属性，使用空字符串
                    Address = store.Address,
                    ContactPhone = store.Phone,
                    Users = users,
                    TotalUsers = users.Count,
                    ActiveUsers = users.Count(u => true), // 这里可以根据用户状态进一步筛选
                };

                return ApiResponse<StoreDetailDto>.OK(result, "获取分店详情成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取分店详情失败");
                return ApiResponse<StoreDetailDto>.Error(
                    "获取分店详情失败",
                    "GET_STORE_DETAIL_ERROR"
                );
            }
        }

        /// <summary>
        /// 创建分店
        /// </summary>
        public async Task<ApiResponse<StoreDto>> CreateStoreAsync(CreateStoreDto dto)
        {
            try
            {
                var db = _context.Db;

                // 检查分店代码是否重复
                var existingStore = await db.Queryable<Store>()
                    .Where(s => s.StoreCode == dto.StoreCode)
                    .FirstAsync();

                if (existingStore != null)
                {
                    return ApiResponse<StoreDto>.Error(
                        "分店代码已存在",
                        "DUPLICATE_STORE_CODE",
                        new { storeCode = dto.StoreCode }
                    );
                }

                var store = new Store
                {
                    StoreName = dto.StoreName,
                    StoreCode = dto.StoreCode,
                    Address = dto.Address,
                    ABN = dto.ABN,
                    BrandName = dto.BrandName,
                    StoreGUID = Guid.NewGuid().ToString(),
                    IsActive = true,
                    Phone = dto.ContactPhone,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                };

                var storeId = await db.Insertable(store).ExecuteReturnIdentityAsync();

                var result = new StoreDto
                {
                    StoreGUID = store.StoreGUID,
                    StoreName = store.StoreName,
                    StoreCode = store.StoreCode,
                    ABN = store.ABN,
                    BrandName = store.BrandName,
                    Description = string.Empty,
                    ContactPhone = string.Empty,
                    Address = store.Address,
                    IsActive = store.IsActive,
                    CreatedAt = store.CreatedAt,
                    UpdatedAt = store.UpdatedAt ?? DateTime.UtcNow,
                };

                return ApiResponse<StoreDto>.OK(result, "创建分店成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建分店失败");
                return ApiResponse<StoreDto>.Error("创建分店失败", "CREATE_STORE_ERROR");
            }
        }

        /// <summary>
        /// 更新分店
        /// </summary>
        public async Task<ApiResponse<StoreDto>> UpdateStoreAsync(string guid, UpdateStoreDto dto)
        {
            try
            {
                var db = _context.Db;

                var store = await db.Queryable<Store>()
                    .Where(s => s.StoreGUID == guid)
                    .FirstAsync();

                if (store == null)
                {
                    return ApiResponse<StoreDto>.Error("分店不存在", "STORE_NOT_FOUND");
                }

                // 检查分店代码是否重复（排除当前分店）
                var existingStore = await db.Queryable<Store>()
                    .Where(s => s.StoreCode == dto.StoreCode && s.StoreGUID != guid)
                    .FirstAsync();

                if (existingStore != null)
                {
                    return ApiResponse<StoreDto>.Error(
                        "分店代码已存在",
                        "DUPLICATE_STORE_CODE",
                        new { storeCode = dto.StoreCode }
                    );
                }

                // 更新分店信息
                store.StoreName = dto.StoreName;
                store.StoreCode = dto.StoreCode;
                store.ABN = dto.ABN;
                store.BrandName = dto.BrandName;
                store.Address = dto.Address;
                store.Phone = dto.ContactPhone;
                store.IsActive = dto.IsActive;
                store.UpdatedAt = DateTime.UtcNow;

                await db.Updateable(store).ExecuteCommandAsync();

                var result = new StoreDto
                {
                    StoreGUID = store.StoreGUID,
                    StoreName = store.StoreName,
                    StoreCode = store.StoreCode,
                    ABN = store.ABN,
                    BrandName = store.BrandName,
                    Description = string.Empty,
                    Address = store.Address,
                    ContactPhone = store.Phone,
                    IsActive = store.IsActive,
                    CreatedAt = store.CreatedAt,
                    UpdatedAt = store.UpdatedAt ?? DateTime.UtcNow,
                };

                return ApiResponse<StoreDto>.OK(result, "更新分店成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新分店失败，ID: {StoreGUID}", guid);
                return ApiResponse<StoreDto>.Error("更新分店失败", "UPDATE_STORE_ERROR");
            }
        }

        /// <summary>
        /// 删除分店
        /// </summary>
        public async Task<ApiResponse<bool>> DeleteStoreAsync(string guid)
        {
            try
            {
                var db = _context.Db;

                // 检查分店是否存在
                var store = await db.Queryable<Store>()
                    .Where(s => s.StoreGUID == guid)
                    .FirstAsync();

                if (store == null)
                {
                    return ApiResponse<bool>.Error("分店不存在", "STORE_NOT_FOUND");
                }

                // 检查是否有关联用户
                var userCount = await db.Queryable<UserStore>()
                    .Where(us => us.StoreGUID == guid)
                    .CountAsync();

                if (userCount > 0)
                {
                    return ApiResponse<bool>.Error(
                        "无法删除有关联用户的分店",
                        "STORE_HAS_USERS",
                        new { userCount }
                    );
                }

                await db.Deleteable<Store>().Where(s => s.StoreGUID == guid).ExecuteCommandAsync();

                return ApiResponse<bool>.OK(true, "删除分店成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除分店失败，ID: {StoreGUID}", guid);
                return ApiResponse<bool>.Error("删除分店失败", "DELETE_STORE_ERROR");
            }
        }

        /// <summary>
        /// 更新分店状态
        /// </summary>
        public async Task<ApiResponse<bool>> UpdateStoreStatusAsync(string guid, bool isActive)
        {
            try
            {
                var db = _context.Db;

                var store = await db.Queryable<Store>()
                    .Where(s => s.StoreGUID == guid)
                    .FirstAsync();

                if (store == null)
                {
                    return ApiResponse<bool>.Error("分店不存在", "STORE_NOT_FOUND");
                }

                store.IsActive = isActive;
                store.UpdatedAt = DateTime.UtcNow;

                await db.Updateable(store).ExecuteCommandAsync();

                return ApiResponse<bool>.OK(true, $"{(isActive ? "激活" : "停用")}分店成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新分店状态失败，ID: {StoreGUID}", guid);
                return ApiResponse<bool>.Error("更新分店状态失败", "UPDATE_STORE_STATUS_ERROR");
            }
        }

        /// <summary>
        /// 应用排序规则到查询
        /// </summary>
        /// <param name="query">分店查询</param>
        /// <param name="sortField">排序字段</param>
        /// <param name="sortOrder">排序方向</param>
        /// <returns>应用排序后的查询</returns>
        private ISugarQueryable<Store> ApplySorting(
            ISugarQueryable<Store> query,
            string? sortField,
            string? sortOrder
        )
        {
            var isDescending = !string.IsNullOrEmpty(sortOrder) && sortOrder.ToLower() == "desc";

            return sortField?.ToLower() switch
            {
                "storename" => isDescending
                    ? query.OrderByDescending(s => s.StoreName)
                    : query.OrderBy(s => s.StoreName),
                "storecode" => isDescending
                    ? query.OrderByDescending(s => s.StoreCode)
                    : query.OrderBy(s => s.StoreCode),
                "createdat" => isDescending
                    ? query.OrderByDescending(s => s.CreatedAt)
                    : query.OrderBy(s => s.CreatedAt),
                "isactive" => isDescending
                    ? query.OrderByDescending(s => s.IsActive)
                    : query.OrderBy(s => s.IsActive),
                "address" => isDescending
                    ? query.OrderByDescending(s => s.Address)
                    : query.OrderBy(s => s.Address),
                _ => query.OrderBy(s => s.StoreName), // 默认按名称升序
            };
        }
    }
}
