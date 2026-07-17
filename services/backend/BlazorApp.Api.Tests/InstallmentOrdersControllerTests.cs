using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using BlazorApp.Api.Authorization;
using BlazorApp.Api.Controllers.React;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.POSM;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class InstallmentOrdersControllerTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnection _connection;
    private readonly SqlSugarClient _db;

    public InstallmentOrdersControllerTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        _connection = new SqliteConnection($"Data Source={_dbPath}");
        _connection.Open();
        _db = new SqlSugarClient(
            new ConnectionConfig
            {
                ConnectionString = _connection.ConnectionString,
                DbType = DbType.Sqlite,
                IsAutoCloseConnection = false,
                InitKeyType = InitKeyType.Attribute,
            }
        );
        _db.CodeFirst.InitTables(
            typeof(UserStore),
            typeof(Store),
            typeof(InstallmentOrder),
            typeof(InstallmentOrderLine),
            typeof(InstallmentPayment)
        );
    }

    [Fact]
    public void Controller_声明专用路由和分期查看权限()
    {
        var controllerType = typeof(InstallmentOrdersController);
        var route = Assert.Single(controllerType.GetCustomAttributes<RouteAttribute>());
        Assert.Equal("api/react/v1/installment-orders", route.Template);

        AssertEndpointContract(
            controllerType,
            nameof(InstallmentOrdersController.List),
            typeof(HttpPostAttribute),
            "list"
        );
        AssertEndpointContract(
            controllerType,
            nameof(InstallmentOrdersController.Detail),
            typeof(HttpGetAttribute),
            "detail/{installmentGuid}"
        );
    }

    [Fact]
    public async Task List_普通用户未分配分店时返回空列表且不查询服务()
    {
        var service = new Mock<IInstallmentOrderReactService>(MockBehavior.Strict);
        var controller = CreateController(service.Object, "USER-1", "User");

        var result = await controller.List(
            new InstallmentOrderQueryParams { PageNumber = 2, PageSize = 30 }
        );

        var ok = Assert.IsType<OkObjectResult>(result);
        var data = GetAnonymousProperty(ok.Value, "data");
        var page = Assert.IsType<PagedListReactDto<InstallmentOrderSummaryDto>>(data);
        Assert.Empty(page.Items);
        Assert.Equal(0, page.Total);
        Assert.Equal(2, page.PageNumber);
        Assert.Equal(30, page.PageSize);
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task List_普通用户查询范围被收敛到已分配分店()
    {
        await SeedUserStoreAsync("USER-1", "STORE-A");
        InstallmentOrderQueryParams? captured = null;
        var service = new Mock<IInstallmentOrderReactService>();
        service
            .Setup(x => x.GetOrderListAsync(It.IsAny<InstallmentOrderQueryParams>()))
            .Callback<InstallmentOrderQueryParams>(query => captured = query)
            .ReturnsAsync(new PagedListReactDto<InstallmentOrderSummaryDto>());
        var controller = CreateController(service.Object, "USER-1", "User");

        await controller.List(
            new InstallmentOrderQueryParams { StoreCodes = ["STORE-A", "STORE-B"] }
        );

        Assert.NotNull(captured);
        Assert.Equal(["STORE-A"], captured.StoreCodes);
    }

    [Fact]
    public async Task List_兼容userGuid声明解析当前用户门店范围()
    {
        await SeedUserStoreAsync("USER-CLAIM", "STORE-A");
        InstallmentOrderQueryParams? captured = null;
        var service = new Mock<IInstallmentOrderReactService>();
        service
            .Setup(x => x.GetOrderListAsync(It.IsAny<InstallmentOrderQueryParams>()))
            .Callback<InstallmentOrderQueryParams>(query => captured = query)
            .ReturnsAsync(new PagedListReactDto<InstallmentOrderSummaryDto>());
        var controller = CreateController(service.Object, "USER-CLAIM", "User", "userGuid");

        await controller.List(new InstallmentOrderQueryParams());

        Assert.NotNull(captured);
        Assert.Equal(["STORE-A"], captured.StoreCodes);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("管理员")]
    [InlineData("SuperAdmin")]
    [InlineData("超级管理员")]
    [InlineData("WarehouseManager")]
    [InlineData("仓库经理")]
    public async Task List_全店角色不受UserStore限制(string role)
    {
        var service = new Mock<IInstallmentOrderReactService>();
        service
            .Setup(x => x.GetOrderListAsync(It.IsAny<InstallmentOrderQueryParams>()))
            .ReturnsAsync(new PagedListReactDto<InstallmentOrderSummaryDto>());
        var controller = CreateController(service.Object, "USER-1", role);

        var result = await controller.List(new InstallmentOrderQueryParams());

        Assert.IsType<OkObjectResult>(result);
        service.Verify(
            x => x.GetOrderListAsync(It.IsAny<InstallmentOrderQueryParams>()),
            Times.Once
        );
    }

    [Fact]
    public void WarehouseManager_默认权限模板包含分期查看权限()
    {
        var template = Assert.Single(
            PermissionSeedData.RolePermissionTemplates,
            item => item.RoleName == "WarehouseManager"
        );

        Assert.Contains(Permissions.InstallmentOrders.View, template.PermissionCodes);
    }

    [Fact]
    public async Task Detail_普通用户未分配分店时统一返回不存在()
    {
        var detail = CreateDetail("STORE-A");
        var service = new Mock<IInstallmentOrderReactService>();
        service
            .Setup(x => x.GetOrderDetailAsync(
                detail.Order.InstallmentGuid,
                It.Is<IReadOnlyCollection<string>>(codes => codes.Count == 0)
            ))
            .ReturnsAsync(ApiResponse<InstallmentOrderDetailResponse>.Error("分期订单不存在"));
        var controller = CreateController(service.Object, "USER-1", "User");

        var result = await controller.Detail(detail.Order.InstallmentGuid);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task Detail_普通用户不能访问其他分店订单()
    {
        await SeedUserStoreAsync("USER-1", "STORE-A");
        var detail = CreateDetail("STORE-B");
        var service = new Mock<IInstallmentOrderReactService>();
        service
            .Setup(x => x.GetOrderDetailAsync(
                detail.Order.InstallmentGuid,
                It.Is<IReadOnlyCollection<string>>(codes => codes.SequenceEqual(new[] { "STORE-A" }))
            ))
            .ReturnsAsync(ApiResponse<InstallmentOrderDetailResponse>.Error("分期订单不存在"));
        var controller = CreateController(service.Object, "USER-1", "User");

        var result = await controller.Detail(detail.Order.InstallmentGuid);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task 授权管道_无分期查看权限时返回403且不进入端点()
    {
        var roleService = CreateRoleService(permissionGranted: false, superAdminGranted: false);
        var nextInvoked = false;

        var statusCode = await RunAuthorizationPipelineAsync(
            "USER-NO-PERMISSION",
            roleService.Object,
            nameof(InstallmentOrdersController.List),
            _ =>
            {
                nextInvoked = true;
                return Task.CompletedTask;
            }
        );

        Assert.Equal(StatusCodes.Status403Forbidden, statusCode);
        Assert.False(nextInvoked);
    }

    [Fact]
    public async Task 授权管道_WarehouseManager默认模板授权可进入端点()
    {
        var template = Assert.Single(
            PermissionSeedData.RolePermissionTemplates,
            item => item.RoleName == "WarehouseManager"
        );
        var roleService = CreateRoleService(
            permissionGranted: template.PermissionCodes.Contains(Permissions.InstallmentOrders.View),
            superAdminGranted: false
        );
        var nextInvoked = false;

        var statusCode = await RunAuthorizationPipelineAsync(
            "USER-WAREHOUSE",
            roleService.Object,
            nameof(InstallmentOrdersController.List),
            _ =>
            {
                nextInvoked = true;
                return Task.CompletedTask;
            }
        );

        Assert.Equal(StatusCodes.Status200OK, statusCode);
        Assert.True(nextInvoked);
    }

    [Fact]
    public async Task 授权管道_SuperAdmin无需显式权限也可进入端点()
    {
        var roleService = CreateRoleService(permissionGranted: false, superAdminGranted: true);
        var nextInvoked = false;

        var statusCode = await RunAuthorizationPipelineAsync(
            "USER-SUPERADMIN",
            roleService.Object,
            nameof(InstallmentOrdersController.Detail),
            _ =>
            {
                nextInvoked = true;
                return Task.CompletedTask;
            }
        );

        Assert.Equal(StatusCodes.Status200OK, statusCode);
        Assert.True(nextInvoked);
    }

    [Fact]
    public async Task 授权管道_普通用户跨店返回404且不会读取商品和付款()
    {
        const string userGuid = "USER-SCOPED";
        const string installmentGuid = "11111111-1111-1111-1111-111111111111";
        await SeedUserStoreAsync(userGuid, "STORE-A");
        await _db.Insertable(
                new InstallmentOrder
                {
                    InstallmentGuid = installmentGuid,
                    InstallmentNumber = "INST-OTHER-STORE",
                    StoreCode = "STORE-B",
                    DeviceCode = "POS-1",
                    CashierId = "CASHIER-1",
                    CashierName = "Cashier",
                    CustomerName = "Customer",
                    CustomerPhone = "0400000000",
                    TotalAmount = 100m,
                    Status = (int)InstallmentOrderStatus.Active,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                }
            )
            .ExecuteCommandAsync();
        // 关键断言：删掉子表后仍应返回 404，证明越权主单不会触发明细查询。
        _db.Ado.ExecuteCommand("DROP TABLE InstallmentOrderLine; DROP TABLE InstallmentPayment;");

        var service = new InstallmentOrderReactService(
            CreateContext<POSMSqlSugarContext>(),
            CreateContext<SqlSugarContext>(),
            NullLogger<InstallmentOrderReactService>.Instance
        );
        var roleService = CreateRoleService(permissionGranted: true, superAdminGranted: false);
        IActionResult? endpointResult = null;

        var statusCode = await RunAuthorizationPipelineAsync(
            userGuid,
            roleService.Object,
            nameof(InstallmentOrdersController.Detail),
            async context =>
            {
                var controller = new InstallmentOrdersController(service, CreateContext<SqlSugarContext>())
                {
                    ControllerContext = new ControllerContext { HttpContext = context },
                };
                endpointResult = await controller.Detail(installmentGuid);
            },
            role: "User",
            userClaimType: "userGuid"
        );

        Assert.Equal(StatusCodes.Status200OK, statusCode);
        Assert.IsType<NotFoundObjectResult>(endpointResult);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
        SqliteTempFileCleanup.DeleteIfExists(_dbPath);
    }

    private InstallmentOrdersController CreateController(
        IInstallmentOrderReactService service,
        string userGuid,
        string role,
        string userClaimType = "userId"
    ) =>
        new(service, CreateContext<SqlSugarContext>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(
                        new ClaimsIdentity(
                            [
                                new Claim(userClaimType, userGuid),
                                new Claim(ClaimTypes.Role, role),
                            ],
                            "test"
                        )
                    ),
                },
            },
        };

    private TContext CreateContext<TContext>()
    {
        var context = (TContext)RuntimeHelpers.GetUninitializedObject(typeof(TContext));
        typeof(TContext)
            .GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(context, _db);
        return context;
    }

    private async Task SeedUserStoreAsync(string userGuid, string storeCode)
    {
        var storeGuid = Guid.NewGuid().ToString();
        await _db.Insertable(
                new Store
                {
                    StoreGUID = storeGuid,
                    StoreCode = storeCode,
                    StoreName = storeCode,
                }
            )
            .ExecuteCommandAsync();
        await _db.Insertable(
                new UserStore
                {
                    UserStoreGUID = Guid.NewGuid().ToString(),
                    UserGUID = userGuid,
                    StoreGUID = storeGuid,
                }
            )
            .ExecuteCommandAsync();
    }

    private static InstallmentOrderDetailResponse CreateDetail(string storeCode) =>
        new()
        {
            Order = new InstallmentOrderDetailDto
            {
                InstallmentGuid = "11111111-1111-1111-1111-111111111111",
                InstallmentNumber = "INST-0001",
                StoreCode = storeCode,
            },
        };

    private static object? GetAnonymousProperty(object? value, string propertyName) =>
        value?.GetType().GetProperty(propertyName)?.GetValue(value);

    private static void AssertEndpointContract(
        Type controllerType,
        string methodName,
        Type httpAttributeType,
        string template
    )
    {
        var method = controllerType.GetMethod(methodName);
        Assert.NotNull(method);
        var httpAttribute = Assert.Single(
            method.GetCustomAttributes(inherit: false),
            httpAttributeType.IsInstanceOfType
        );
        Assert.Equal(template, ((HttpMethodAttribute)httpAttribute).Template);
        var authorize = Assert.Single(method.GetCustomAttributes<AuthorizeAttribute>());
        Assert.Equal(Permissions.InstallmentOrders.View, authorize.Policy);
        Assert.True(string.IsNullOrWhiteSpace(authorize.Roles));
    }

    private static Mock<IRoleService> CreateRoleService(
        bool permissionGranted,
        bool superAdminGranted
    )
    {
        var roleService = new Mock<IRoleService>();
        roleService
            .Setup(service => service.UserHasRoleAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((string _, string role) =>
                ApiResponse<bool>.OK(
                    superAdminGranted
                    && role.Equals("SuperAdmin", StringComparison.OrdinalIgnoreCase)
                )
            );
        roleService
            .Setup(service => service.UserHasPermissionAsync(
                It.IsAny<string>(),
                It.IsAny<string>()
            ))
            .ReturnsAsync((string _, string permission) =>
                ApiResponse<bool>.OK(
                    permissionGranted
                    && permission.Equals(
                        Permissions.InstallmentOrders.View,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
            );
        return roleService;
    }

    private static async Task<int> RunAuthorizationPipelineAsync(
        string userGuid,
        IRoleService roleService,
        string methodName,
        RequestDelegate terminal,
        string role = "User",
        string userClaimType = ClaimTypes.NameIdentifier
    )
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        services.AddAuthentication("TestAuth");
        services.AddAuthorization();
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddScoped(_ => roleService);
        services.Replace(
            ServiceDescriptor.Singleton<IAuthenticationService, PipelineAuthenticationService>()
        );
        await using var provider = services.BuildServiceProvider();

        var appBuilder = new ApplicationBuilder(provider);
        appBuilder.UseAuthorization();
        appBuilder.Run(terminal);

        var context = new DefaultHttpContext { RequestServices = provider };
        context.User = new ClaimsPrincipal(
            new ClaimsIdentity(
                [
                    new Claim(userClaimType, userGuid),
                    new Claim(ClaimTypes.Role, role),
                ],
                "TestAuth"
            )
        );
        context.SetEndpoint(
            new Endpoint(
                _ => Task.CompletedTask,
                new EndpointMetadataCollection(GetAuthorizationMetadata(methodName)),
                methodName
            )
        );

        await appBuilder.Build()(context);
        return context.Response.StatusCode;
    }

    private static object[] GetAuthorizationMetadata(string methodName)
    {
        var controllerAttributes = typeof(InstallmentOrdersController)
            .GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .Cast<object>();
        var methodAttributes = typeof(InstallmentOrdersController)
            .GetMethod(methodName)!
            .GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .Cast<object>();
        return controllerAttributes.Concat(methodAttributes).ToArray();
    }

    private sealed class PipelineAuthenticationService : IAuthenticationService
    {
        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) =>
            Task.FromResult(
                AuthenticateResult.Success(
                    new AuthenticationTicket(context.User, scheme ?? "TestAuth")
                )
            );

        public Task ChallengeAsync(
            HttpContext context,
            string? scheme,
            AuthenticationProperties? properties
        )
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }

        public Task ForbidAsync(
            HttpContext context,
            string? scheme,
            AuthenticationProperties? properties
        )
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }

        public Task SignInAsync(
            HttpContext context,
            string? scheme,
            ClaimsPrincipal principal,
            AuthenticationProperties? properties
        ) => Task.CompletedTask;

        public Task SignOutAsync(HttpContext context, string? scheme) => Task.CompletedTask;

        public Task SignOutAsync(
            HttpContext context,
            string? scheme,
            AuthenticationProperties? properties
        ) => Task.CompletedTask;
    }
}
