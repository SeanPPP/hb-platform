using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using SqlSugar;
using BlazorApp.Shared.Models;
using BlazorApp.Api.Data;
using BlazorApp.Api.Filters;
using BlazorApp.Api.Services;
using BlazorApp.Api.Interfaces;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize] // 🔐 启用授权，需要认证才能访问调试接口
    [DevelopmentOnly]
    [Authorize(Roles = "Admin")]
    [Obsolete("Diagnostic controller. Keep disabled outside Development and remove after confirming no runtime usage.")]
    public class DebugController : ControllerBase
    {
        private readonly SqlSugarContext _dbContext;
        private readonly IDataInitializationService _dataInitService;
        private readonly IWarehouseProductService _warehouseProductService;

        public DebugController(SqlSugarContext dbContext, IDataInitializationService dataInitService, IWarehouseProductService warehouseProductService)
        {
            _dbContext = dbContext;
            _dataInitService = dataInitService;
            _warehouseProductService = warehouseProductService;
        }

        [HttpGet("check-navigation")]
        public async Task<IActionResult> CheckNavigation()
        {
            try
            {
                var result = new
                {
                    // 检查角色表
                    roles = await _dbContext.Db.Queryable<Role>().ToListAsync(),

                    // 检查用户角色关联表
                    userRoles = await _dbContext.Db.Queryable<UserRole>().ToListAsync(),

                    // 检查用户表
                    users = await _dbContext.Db.Queryable<User>().ToListAsync(),

                    // 测试导航查询
                    navigationTest = await TestNavigationQuery("admin")
                };

                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message, stackTrace = ex.StackTrace });
            }
        }

        [HttpGet("test-navigation/{username}")]
        public async Task<IActionResult> TestNavigationQuery(string username)
        {
            try
            {
                // 测试1：基本用户查询
                var user = await _dbContext.Db.Queryable<User>()
                    .FirstAsync(u => u.Username == username);

                if (user == null)
                {
                    return NotFound(new { message = $"用户 {username} 不存在" });
                }

                // 测试2：包含角色的导航查询
                var userWithRoles = await _dbContext.Db.Queryable<User>()
                    .Includes(u => u.Roles)
                    .FirstAsync(u => u.Username == username);

                // 测试3：手动查询用户角色关联
                var userRoles = await _dbContext.Db.Queryable<UserRole>()
                    .Where(ur => ur.UserGUID == user.UserGUID)
                    .ToListAsync();

                // 测试4：手动查询角色信息
                var roles = new List<Role>();
                if (userRoles.Any())
                {
                    var roleGuids = userRoles.Select(ur => ur.RoleGUID).ToList();
                    roles = await _dbContext.Db.Queryable<Role>()
                        .Where(r => roleGuids.Contains(r.RoleGUID))
                        .ToListAsync();
                }

                return Ok(new
                {
                    user = new
                    {
                        user.UserGUID,
                        user.Username,
                        user.IsActive
                    },
                    navigationQuery = new
                    {
                        hasRoles = userWithRoles?.Roles != null,
                        rolesCount = userWithRoles?.Roles?.Count ?? 0,
                        roles = userWithRoles?.Roles?.Select(r => new { r.RoleGUID, r.RoleName })
                    },
                    manualQuery = new
                    {
                        userRolesCount = userRoles.Count,
                        userRoles = userRoles.Select(ur => new { ur.UserGUID, ur.RoleGUID }),
                        rolesCount = roles.Count,
                        roles = roles.Select(r => new { r.RoleGUID, r.RoleName })
                    }
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message, stackTrace = ex.StackTrace });
            }
        }

        [HttpGet("init-data")]
        public async Task<IActionResult> InitializeData()
        {
            try
            {
                await _dataInitService.CheckAndInitializeDataAsync();
                return Ok(new { message = "数据初始化完成" });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message, stackTrace = ex.StackTrace });
            }
        }

        [HttpGet("sql-test")]
        public async Task<IActionResult> SqlTest()
        {
            try
            {
                // 测试原始SQL查询
                var sql = @"
                    SELECT 
                        u.UserGUID,
                        u.Username,
                        r.RoleGUID,
                        r.RoleName
                    FROM [User] u
                    LEFT JOIN UserRole ur ON u.UserGUID = ur.UserGUID
                    LEFT JOIN Role r ON ur.RoleGUID = r.RoleGUID
                    WHERE u.Username = 'admin'
                ";

                var result = await _dbContext.Db.Ado.SqlQueryAsync<dynamic>(sql);

                return Ok(new
                {
                    sqlQuery = sql,
                    result = result
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message, stackTrace = ex.StackTrace });
            }
        }

        [HttpGet("test-navigation-config")]
        public async Task<IActionResult> TestNavigationConfig()
        {
            try
            {
                // 测试1：检查User -> UserRole导航
                var userWithUserRoles = await _dbContext.Db.Queryable<User>()
                    .Includes(u => u.Roles)
                    .FirstAsync(u => u.Username == "admin");

                // 测试2：检查User -> Role导航（修复后的配置）
                var userWithRoles = await _dbContext.Db.Queryable<User>()
                    .Includes(u => u.Roles)
                    .FirstAsync(u => u.Username == "admin");

                // 测试3：手动验证关联逻辑
                var user = await _dbContext.Db.Queryable<User>()
                    .FirstAsync(u => u.Username == "admin");

                var userRoles = await _dbContext.Db.Queryable<UserRole>()
                    .Where(ur => ur.UserGUID == user.UserGUID)
                    .ToListAsync();

                var roleGuids = userRoles.Select(ur => ur.RoleGUID).ToList();
                var roles = await _dbContext.Db.Queryable<Role>()
                    .Where(r => roleGuids.Contains(r.RoleGUID))
                    .ToListAsync();

                return Ok(new
                {
                    navigationConfig = new
                    {
                        userToUserRoles = userWithUserRoles?.Roles?.Count ?? 0,
                        userToRoles = userWithRoles?.Roles?.Count ?? 0,
                        manualQuery = roles.Count
                    },
                    data = new
                    {
                        user = user != null ? new { user.UserGUID, user.Username } : null,
                        userRoles = userRoles.Select(ur => new { ur.UserGUID, ur.RoleGUID }),
                        roles = roles.Select(r => new { r.RoleGUID, r.RoleName })
                    }
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message, stackTrace = ex.StackTrace });
            }
        }

        [HttpGet("test-all-navigation")]
        public async Task<IActionResult> TestAllNavigation()
        {
            try
            {
                var results = new Dictionary<string, object>();

                // 测试1：User -> Roles
                var userWithRoles = await _dbContext.Db.Queryable<User>()
                    .Includes(u => u.Roles)
                    .FirstAsync(u => u.Username == "admin");
                results["User_Roles"] = new
                {
                    success = userWithRoles?.Roles != null,
                    count = userWithRoles?.Roles?.Count ?? 0
                };

                // 测试2：User -> Stores
                var userWithStores = await _dbContext.Db.Queryable<User>()
                    .Includes(u => u.Stores)
                    .FirstAsync(u => u.Username == "admin");
                results["User_Stores"] = new
                {
                    success = userWithStores?.Stores != null,
                    count = userWithStores?.Stores?.Count ?? 0
                };

                // 测试3：User -> RefreshTokens
                var userWithRefreshTokens = await _dbContext.Db.Queryable<User>()
                    .Includes(u => u.RefreshTokens)
                    .FirstAsync(u => u.Username == "admin");
                results["User_RefreshTokens"] = new
                {
                    success = userWithRefreshTokens?.RefreshTokens != null,
                    count = userWithRefreshTokens?.RefreshTokens?.Count ?? 0
                };

                // 测试4：Store -> Users
                var store = await _dbContext.Db.Queryable<Store>().FirstAsync();
                if (store != null)
                {
                    var storeWithUsers = await _dbContext.Db.Queryable<Store>()
                        .Includes(s => s.Users)
                        .FirstAsync(s => s.StoreGUID == store.StoreGUID);
                    results["Store_Users"] = new
                    {
                        success = storeWithUsers?.Users != null,
                        count = storeWithUsers?.Users?.Count ?? 0
                    };
                }



                // 测试10：UserStore -> User
                var userStore = await _dbContext.Db.Queryable<UserStore>().FirstAsync();
                if (userStore != null)
                {
                    var userStoreWithUser = await _dbContext.Db.Queryable<UserStore>()
                        .Includes(us => us.User)
                        .FirstAsync(us => us.UserStoreGUID == userStore.UserStoreGUID);
                    results["UserStore_User"] = new
                    {
                        success = userStoreWithUser?.User != null,
                        hasUser = userStoreWithUser?.User != null
                    };

                    // 测试11：UserStore -> Store
                    var userStoreWithStore = await _dbContext.Db.Queryable<UserStore>()
                        .Includes(us => us.Store)
                        .FirstAsync(us => us.UserStoreGUID == userStore.UserStoreGUID);
                    results["UserStore_Store"] = new
                    {
                        success = userStoreWithStore?.Store != null,
                        hasStore = userStoreWithStore?.Store != null
                    };
                }

                // 测试12：RefreshToken -> User
                var refreshToken = await _dbContext.Db.Queryable<RefreshToken>().FirstAsync();
                if (refreshToken != null)
                {
                    var refreshTokenWithUser = await _dbContext.Db.Queryable<RefreshToken>()
                        .Includes(rt => rt.User)
                        .FirstAsync(rt => rt.RefreshTokenGUID == refreshToken.RefreshTokenGUID);
                    results["RefreshToken_User"] = new
                    {
                        success = refreshTokenWithUser?.User != null,
                        hasUser = refreshTokenWithUser?.User != null
                    };
                }

                return Ok(new
                {
                    message = "所有导航配置测试完成",
                    results = results,
                    summary = new
                    {
                        totalTests = results.Count,
                        successfulTests = results.Values.Count(v => (v?.GetType().GetProperty("success")?.GetValue(v) as bool?) == true),
                        failedTests = results.Values.Count(v => (v?.GetType().GetProperty("success")?.GetValue(v) as bool?) == false)
                    }
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message, stackTrace = ex.StackTrace });
            }
        }

        [HttpGet("test-simple-navigation")]
        public async Task<IActionResult> TestSimpleNavigation()
        {
            try
            {
                var results = new Dictionary<string, object>();

                // 测试1：基本用户查询
                var user = await _dbContext.Db.Queryable<User>()
                    .FirstAsync(u => u.Username == "admin");

                if (user == null)
                {
                    return NotFound(new { message = "admin用户不存在" });
                }

                // 测试2：用户角色导航查询
                var userWithRoles = await _dbContext.Db.Queryable<User>()
                    .Includes(u => u.Roles)
                    .FirstAsync(u => u.Username == "admin");

                results["User_Roles"] = new
                {
                    success = userWithRoles?.Roles != null,
                    count = userWithRoles?.Roles?.Count ?? 0,
                    roles = userWithRoles?.Roles?.Select(r => new { r.RoleGUID, r.RoleName })
                };

                // 测试3：用户店铺导航查询
                var userWithStores = await _dbContext.Db.Queryable<User>()
                    .Includes(u => u.Stores)
                    .FirstAsync(u => u.Username == "admin");

                results["User_Stores"] = new
                {
                    success = userWithStores?.Stores != null,
                    count = userWithStores?.Stores?.Count ?? 0,
                    stores = userWithStores?.Stores?.Select(s => new { s.StoreGUID, s.StoreName })
                };

                return Ok(new
                {
                    message = "简化导航测试完成",
                    user = new { user.UserGUID, user.Username },
                    results = results
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message, stackTrace = ex.StackTrace });
            }
        }

        /// <summary>
        /// 测试二级导航属性查询 - 无需认证
        /// </summary>
        [HttpGet("test-navigation")]
        [AllowAnonymous]
        public async Task<IActionResult> TestNavigationQuery()
        {
            try
            {
                // 1. 检查 Product 表中的 WarehouseCategoryGUID 数据
                var productsWithCategory = await _dbContext.Db.Queryable<Product>()
                    .Where(p => p.WarehouseCategoryGUID != null)
                    .Take(5)
                    .ToListAsync();

                // 2. 检查 WarehouseCategory 表中的数据
                var categories = await _dbContext.Db.Queryable<WarehouseCategory>()
                    .Take(5)
                    .ToListAsync();

                // 3. 测试 WarehouseProduct 的 Product 导航属性
                var warehouseProductsWithProduct = await _dbContext.Db.Queryable<WarehouseProduct>()
                    .Includes(wp => wp.Product)
                    .Take(3)
                    .ToListAsync();

                // 4. 测试二级导航查询 - 方法1
                var method1Result = await _dbContext.Db.Queryable<WarehouseProduct>()
                    .Includes(wp => wp.Product)
                    .Includes(wp => wp.Product!.WarehouseCategory)
                    .Take(3)
                    .ToListAsync();

                // 5. 测试二级导航查询 - 方法2
                var method2Result = await _dbContext.Db.Queryable<WarehouseProduct>()
                    .Includes(wp => wp.Product)
                    .Take(3)
                    .ToListAsync();

                // 手动加载 WarehouseCategory
                foreach (var item in method2Result)
                {
                    if (item.Product != null && !string.IsNullOrEmpty(item.Product.WarehouseCategoryGUID))
                    {
                        item.Product.WarehouseCategory = await _dbContext.Db.Queryable<WarehouseCategory>()
                            .FirstAsync(c => c.CategoryGUID == item.Product.WarehouseCategoryGUID);
                    }
                }

                return Ok(new
                {
                    message = "二级导航属性测试结果",
                    data = new
                    {
                        productsWithCategoryCount = productsWithCategory.Count,
                        productsWithCategoryData = productsWithCategory.Select(p => new
                        {
                            p.ProductCode,
                            p.ProductName,
                            p.WarehouseCategoryGUID
                        }).ToList(),
                        categoriesCount = categories.Count,
                        categoriesData = categories.Select(c => new
                        {
                            c.CategoryGUID,
                            c.CategoryName,
                            c.ChineseName
                        }).ToList(),
                        warehouseProductsWithProductCount = warehouseProductsWithProduct.Count,
                        warehouseProductsData = warehouseProductsWithProduct.Select(wp => new
                        {
                            wp.ProductCode,
                            ProductName = wp.Product?.ProductName,  // 从 Product 表获取名称
                            ProductInfo = wp.Product != null ? new
                            {
                                wp.Product.ProductCode,
                                wp.Product.ProductName,
                                wp.Product.WarehouseCategoryGUID
                            } : null
                        }).ToList(),
                        method1Results = method1Result.Select(wp => new
                        {
                            wp.ProductCode,
                            ProductName = wp.Product?.ProductName,  // 从 Product 表获取名称
                            ProductInfo = wp.Product != null ? new
                            {
                                wp.Product.ProductCode,
                                wp.Product.ProductName,
                                wp.Product.WarehouseCategoryGUID,
                                CategoryInfo = wp.Product.WarehouseCategory != null ? new
                                {
                                    wp.Product.WarehouseCategory.CategoryGUID,
                                    wp.Product.WarehouseCategory.CategoryName,
                                    wp.Product.WarehouseCategory.ChineseName
                                } : null
                            } : null
                        }).ToList(),
                        method2Results = method2Result.Select(wp => new
                        {
                            wp.ProductCode,
                            ProductName = wp.Product?.ProductName,  // 从 Product 表获取名称
                            ProductInfo = wp.Product != null ? new
                            {
                                wp.Product.ProductCode,
                                wp.Product.ProductName,
                                wp.Product.WarehouseCategoryGUID,
                                CategoryInfo = wp.Product.WarehouseCategory != null ? new
                                {
                                    wp.Product.WarehouseCategory.CategoryGUID,
                                    wp.Product.WarehouseCategory.CategoryName,
                                    wp.Product.WarehouseCategory.ChineseName
                                } : null
                            } : null
                        }).ToList()
                    }
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message, stackTrace = ex.StackTrace });
            }
        }

        /// <summary>
        /// 测试IsActive查询问题 - 无需认证
        /// </summary>
        [HttpGet("test-isactive")]
        [AllowAnonymous]
        public async Task<IActionResult> TestIsActiveQuery()
        {
            try
            {
                // 测试IsActive=true的查询
                var queryTrue = new WarehouseProductQueryDto
                {
                    PageNumber = 1,
                    PageSize = 5,
                    IsActive = true
                };

                var resultTrue = await _warehouseProductService.GetPagedProductsAsync(queryTrue);

                // 测试IsActive=false的查询
                var queryFalse = new WarehouseProductQueryDto
                {
                    PageNumber = 1,
                    PageSize = 5,
                    IsActive = false
                };

                var resultFalse = await _warehouseProductService.GetPagedProductsAsync(queryFalse);

                // 测试无IsActive条件的查询
                var queryAll = new WarehouseProductQueryDto
                {
                    PageNumber = 1,
                    PageSize = 5
                };

                var resultAll = await _warehouseProductService.GetPagedProductsAsync(queryAll);

                // 直接SQL查询验证
                var sqlActiveCount = await _dbContext.Db.Ado.GetIntAsync("SELECT COUNT(*) FROM WarehouseProduct WHERE IsActive = 1");
                var sqlInactiveCount = await _dbContext.Db.Ado.GetIntAsync("SELECT COUNT(*) FROM WarehouseProduct WHERE IsActive = 0");
                var sqlTotalCount = await _dbContext.Db.Ado.GetIntAsync("SELECT COUNT(*) FROM WarehouseProduct");

                return Ok(new
                {
                    message = "IsActive查询测试结果",
                    results = new
                    {
                        activeQuery = new
                        {
                            totalCount = resultTrue.Total,
                            itemsReturned = resultTrue.Items.Count,
                            statsActiveCount = resultTrue.Stats?.ActiveProductCount ?? 0
                        },
                        inactiveQuery = new
                        {
                            totalCount = resultFalse.Total,
                            itemsReturned = resultFalse.Items.Count,
                            statsActiveCount = resultFalse.Stats?.ActiveProductCount ?? 0
                        },
                        allQuery = new
                        {
                            totalCount = resultAll.Total,
                            itemsReturned = resultAll.Items.Count,
                            statsActiveCount = resultAll.Stats?.ActiveProductCount ?? 0
                        }
                    },
                    directSqlCounts = new
                    {
                        activeProducts = sqlActiveCount,
                        inactiveProducts = sqlInactiveCount,
                        totalProducts = sqlTotalCount
                    }
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message, stackTrace = ex.StackTrace });
            }
        }
    }
}
