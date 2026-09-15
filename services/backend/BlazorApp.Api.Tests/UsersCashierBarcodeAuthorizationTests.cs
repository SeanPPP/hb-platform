using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using BlazorApp.Api.Authentication;
using BlazorApp.Api.Controllers;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Services;
using BlazorApp.Api.Services.MobileDeviceActivation;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class UsersCashierBarcodeAuthorizationTests : IDisposable
{
    private const string ActorGuid = "admin-user";
    private const string ManagerGuid = "manager-user";
    private const string TargetGuid = "target-user";
    private const string GeneratedBarcode = "2912345678906";

    private readonly string _dbPath;
    private readonly SqliteConnection _sqliteConnection;
    private readonly SqlSugarClient _db;

    public UsersCashierBarcodeAuthorizationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        _sqliteConnection = new SqliteConnection($"Data Source={_dbPath}");
        _sqliteConnection.Open();
        _db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = _sqliteConnection.ConnectionString,
            DbType = DbType.Sqlite,
            IsAutoCloseConnection = false,
            InitKeyType = InitKeyType.Attribute,
        });
        _db.CodeFirst.InitTables(
            typeof(User),
            typeof(Role),
            typeof(UserRole),
            typeof(Store),
            typeof(UserStore),
            typeof(CashRegisterUser),
            typeof(CashierBarcodeReservation),
            typeof(EmployeeCashierBarcode)
        );
    }

    [Fact]
    public void Routes_RequirePosTerminalManagementPermission()
    {
        var get = typeof(UsersController).GetMethod(nameof(UsersController.GetUserCashierBarcode));
        var post = typeof(UsersController).GetMethod(nameof(UsersController.RefreshUserCashierBarcode));

        Assert.Equal(
            Permissions.Users.ManagePosTerminalPermissions,
            get?.GetCustomAttribute<AuthorizeAttribute>()?.Policy
        );
        Assert.Equal(
            Permissions.Users.ManagePosTerminalPermissions,
            post?.GetCustomAttribute<AuthorizeAttribute>()?.Policy
        );
    }

    [Fact]
    public async Task AdminWithPermission_GetAndPostReturnOkWithoutLegacyFallbackAndDisableLegacy()
    {
        await SeedActorAsync(ActorGuid, "Admin");
        await SeedTargetAsync(isActive: true);
        await _db.Insertable(new CashRegisterUser
        {
            UserGUID = TargetGuid,
            UserBarcode = "2988888888888",
            Status = true,
            OperatorUser = "legacy",
            LoginRole = "cashier",
            Remark = string.Empty,
            Creator = "seed",
            CreateDate = DateTime.UtcNow,
            LastModifier = "seed",
            LastModifyDate = DateTime.UtcNow,
        }).ExecuteCommandAsync();
        var (controller, _) = CreateController(ActorGuid, permissionGranted: true);

        var getResult = Assert.IsType<OkObjectResult>(
            await controller.GetUserCashierBarcode(TargetGuid)
        );
        var getBody = Assert.IsType<ApiResponse<EmployeeCashierBarcodeDto>>(getResult.Value);
        Assert.True(getBody.Success);
        Assert.False(getBody.Data!.Exists);
        Assert.Null(getBody.Data.Barcode);
        Assert.Equal("no-store", controller.Response.Headers.CacheControl.ToString());

        var postResult = Assert.IsType<OkObjectResult>(
            await controller.RefreshUserCashierBarcode(
                TargetGuid,
                new AdminCashierBarcodeRefreshRequest { ExpectedBarcode = null }
            )
        );
        var postBody = Assert.IsType<ApiResponse<EmployeeCashierBarcodeDto>>(postResult.Value);
        Assert.True(postBody.Success);
        Assert.Equal(GeneratedBarcode, postBody.Data!.Barcode);
        Assert.Equal("no-store", controller.Response.Headers.CacheControl.ToString());
        var legacy = await _db.Queryable<CashRegisterUser>()
            .FirstAsync(item => item.UserGUID == TargetGuid);
        Assert.False(legacy!.Status);
        Assert.Equal(ActorGuid, legacy.LastModifier);
        Assert.Equal(ActorGuid, (await _db.Queryable<EmployeeCashierBarcode>()
            .FirstAsync(item => item.UserGUID == TargetGuid))!.UpdatedBy);
    }

    [Fact]
    public async Task StoreManagerWithPermission_IsForbiddenBeforeMissingTargetLookup()
    {
        await SeedActorAsync(ManagerGuid, "StoreManager");
        var (controller, roleService) = CreateController(ManagerGuid, permissionGranted: true);

        Assert.IsType<ForbidResult>(await controller.GetUserCashierBarcode(TargetGuid));
        Assert.IsType<ForbidResult>(await controller.RefreshUserCashierBarcode(
            TargetGuid,
            new AdminCashierBarcodeRefreshRequest { ExpectedBarcode = null }
        ));
        roleService.Verify(
            service => service.UserHasPermissionAsync(It.IsAny<string>(), It.IsAny<string>()),
            Times.Never
        );
    }

    [Fact]
    public async Task AdminWithoutPermission_IsForbiddenBeforeMissingTargetLookup()
    {
        await SeedActorAsync(ActorGuid, "Admin");
        var (controller, roleService) = CreateController(ActorGuid, permissionGranted: false);

        Assert.IsType<ForbidResult>(await controller.GetUserCashierBarcode(TargetGuid));
        Assert.IsType<ForbidResult>(await controller.RefreshUserCashierBarcode(
            TargetGuid,
            new AdminCashierBarcodeRefreshRequest { ExpectedBarcode = null }
        ));
        roleService.Verify(
            service => service.UserHasPermissionAsync(
                ActorGuid,
                Permissions.Users.ManagePosTerminalPermissions
            ),
            Times.Exactly(2)
        );
    }

    [Theory]
    [InlineData(ServiceApiTokenAuthenticationDefaults.TokenTypeClaim, "true")]
    [InlineData("token_use", MobileDeviceAccountTokenIssuer.TokenUse)]
    [InlineData("token_use", "browser_extension")]
    public async Task RestrictedTokenTypes_AreForbidden(string claimType, string claimValue)
    {
        await SeedActorAsync(ActorGuid, "Admin");
        await SeedTargetAsync(isActive: true);
        var (controller, roleService) = CreateController(
            ActorGuid,
            permissionGranted: true,
            new Claim(claimType, claimValue)
        );

        Assert.IsType<ForbidResult>(await controller.GetUserCashierBarcode(TargetGuid));
        Assert.IsType<ForbidResult>(await controller.RefreshUserCashierBarcode(
            TargetGuid,
            new AdminCashierBarcodeRefreshRequest { ExpectedBarcode = null }
        ));
        roleService.Verify(
            service => service.UserHasPermissionAsync(It.IsAny<string>(), It.IsAny<string>()),
            Times.Never
        );
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public async Task MissingInactiveOrDeletedTarget_GetAndPostReturnNotFound(
        bool seedTarget,
        bool targetIsActive,
        bool targetIsDeleted
    )
    {
        await SeedActorAsync(ActorGuid, "Admin");
        if (seedTarget) await SeedTargetAsync(targetIsActive, targetIsDeleted);
        var (getController, _) = CreateController(ActorGuid, permissionGranted: true);
        var (postController, _) = CreateController(ActorGuid, permissionGranted: true);

        var getResult = Assert.IsType<NotFoundObjectResult>(
            await getController.GetUserCashierBarcode(TargetGuid)
        );
        var getBody = Assert.IsType<ApiResponse<object>>(getResult.Value);
        Assert.Equal("USER_NOT_FOUND", getBody.ErrorCode);
        Assert.Equal("no-store", getController.Response.Headers.CacheControl.ToString());

        var postResult = Assert.IsType<NotFoundObjectResult>(
            await postController.RefreshUserCashierBarcode(
                TargetGuid,
                new AdminCashierBarcodeRefreshRequest { ExpectedBarcode = null }
            )
        );
        var postBody = Assert.IsType<ApiResponse<object>>(postResult.Value);
        Assert.Equal("USER_NOT_FOUND", postBody.ErrorCode);
        Assert.Equal("no-store", postController.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task Post_MissingExpectedBarcodeReturnsBadRequestButExplicitNullSucceeds()
    {
        await SeedActorAsync(ActorGuid, "Admin");
        await SeedTargetAsync(isActive: true);
        var (controller, _) = CreateController(ActorGuid, permissionGranted: true);

        var missingBody = Assert.IsType<BadRequestObjectResult>(
            await controller.RefreshUserCashierBarcode(TargetGuid, null)
        );
        Assert.Equal(
            "EXPECTED_BARCODE_REQUIRED",
            Assert.IsType<ApiResponse<object>>(missingBody.Value).ErrorCode
        );
        var missingProperty = Assert.IsType<BadRequestObjectResult>(
            await controller.RefreshUserCashierBarcode(
                TargetGuid,
                new AdminCashierBarcodeRefreshRequest()
            )
        );
        Assert.Equal(
            "EXPECTED_BARCODE_REQUIRED",
            Assert.IsType<ApiResponse<object>>(missingProperty.Value).ErrorCode
        );

        var explicitNull = Assert.IsType<OkObjectResult>(
            await controller.RefreshUserCashierBarcode(
                TargetGuid,
                new AdminCashierBarcodeRefreshRequest { ExpectedBarcode = null }
            )
        );
        Assert.Equal(
            GeneratedBarcode,
            Assert.IsType<ApiResponse<EmployeeCashierBarcodeDto>>(explicitNull.Value).Data!.Barcode
        );
    }

    [Fact]
    public async Task Post_StaleExpectedBarcodeReturnsConflictWithoutCurrentBarcode()
    {
        await SeedActorAsync(ActorGuid, "Admin");
        await SeedTargetAsync(isActive: true);
        var (controller, _) = CreateController(ActorGuid, permissionGranted: true);
        await controller.RefreshUserCashierBarcode(
            TargetGuid,
            new AdminCashierBarcodeRefreshRequest { ExpectedBarcode = null }
        );

        var result = Assert.IsType<ConflictObjectResult>(
            await controller.RefreshUserCashierBarcode(
                TargetGuid,
                new AdminCashierBarcodeRefreshRequest { ExpectedBarcode = null }
            )
        );
        var body = Assert.IsType<ApiResponse<EmployeeCashierBarcodeDto>>(result.Value);
        Assert.False(body.Success);
        Assert.Equal("CASHIER_BARCODE_CHANGED", body.ErrorCode);
        Assert.Null(body.Data);
        Assert.Equal("no-store", controller.Response.Headers.CacheControl.ToString());
        Assert.Equal(1, await _db.Queryable<EmployeeCashierBarcode>()
            .CountAsync(item => item.UserGUID == TargetGuid && item.Status));
    }

    private async Task SeedActorAsync(string userGuid, string roleName)
    {
        await _db.Insertable(CreateUser(userGuid, isActive: true)).ExecuteCommandAsync();
        var roleGuid = $"role-{userGuid}";
        await _db.Insertable(new Role
        {
            RoleGUID = roleGuid,
            RoleName = roleName,
            IsActive = true,
        }).ExecuteCommandAsync();
        await _db.Insertable(new UserRole
        {
            UserRoleGUID = $"user-role-{userGuid}",
            UserGUID = userGuid,
            RoleGUID = roleGuid,
        }).ExecuteCommandAsync();
    }

    private Task<int> SeedTargetAsync(bool isActive, bool isDeleted = false) =>
        _db.Insertable(CreateUser(TargetGuid, isActive, isDeleted)).ExecuteCommandAsync();

    private static User CreateUser(string userGuid, bool isActive, bool isDeleted = false) => new()
    {
        UserGUID = userGuid,
        Username = userGuid,
        Email = $"{userGuid}@example.test",
        PasswordHash = "hashed",
        IsActive = isActive,
        IsDeleted = isDeleted,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
    };

    private (UsersController Controller, Mock<IRoleService> RoleService) CreateController(
        string actorGuid,
        bool permissionGranted,
        params Claim[] extraClaims
    )
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, actorGuid),
            new(ClaimTypes.Name, actorGuid),
        };
        claims.AddRange(extraClaims);
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth")),
        };
        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        var context = CreateSqlSugarContext(_db);
        var barcodeService = new EmployeeCashierBarcodeService(
            context,
            new CurrentUserService(accessor),
            () => GeneratedBarcode
        );
        var roleService = new Mock<IRoleService>();
        roleService
            .Setup(service => service.UserHasPermissionAsync(
                actorGuid,
                Permissions.Users.ManagePosTerminalPermissions
            ))
            .ReturnsAsync(ApiResponse<bool>.OK(permissionGranted));
        var controller = new UsersController(
            Mock.Of<IUserService>(),
            roleService.Object,
            NullLogger<UsersController>.Instance,
            context,
            barcodeService
        )
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
        };
        return (controller, roleService);
    }

    private static SqlSugarContext CreateSqlSugarContext(ISqlSugarClient db)
    {
        var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(
            typeof(SqlSugarContext)
        );
        var dbField = typeof(SqlSugarContext).GetField(
            "_db",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        dbField!.SetValue(context, db);
        return context;
    }

    public void Dispose()
    {
        _db.Dispose();
        _sqliteConnection.Dispose();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }
}
