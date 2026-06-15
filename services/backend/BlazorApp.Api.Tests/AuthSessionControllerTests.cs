using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using BlazorApp.Api.Controllers;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Services;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Moq;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class AuthSessionControllerTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnection _sqliteConnection;
    private readonly SqlSugarClient _db;

    public AuthSessionControllerTests()
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

        _db.CodeFirst.InitTables<User, Role, UserRole, SysRolePermission>();
        _db.CodeFirst.InitTables<SysUserPermission>();
    }

    [Fact]
    public async Task SessionLogin_ReturnsCookieOnlySessionPayload()
    {
        await _db.Insertable(
            new User
            {
                UserGUID = "user-1",
                Username = "alice",
                Email = "alice@example.com",
                PasswordHash = "hashed",
                FullName = "Alice",
                IsActive = true,
                IsDeleted = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            }
        ).ExecuteCommandAsync();

        var authService = new Mock<IAuthService>();
        authService
            .Setup(service => service.LoginAsync(It.IsAny<LoginRequest>()))
            .ReturnsAsync(
                new LoginResponse
                {
                    Success = true,
                    User = new LoginUserDto
                    {
                        UserGUID = "user-1",
                        Username = "alice",
                        Email = "alice@example.com",
                    },
                }
            );
        authService
            .Setup(service =>
                service.GenerateTokensAsync(
                    It.Is<User>(user => user.UserGUID == "user-1"),
                    It.IsAny<string>(),
                    It.IsAny<string>()
                )
            )
            .ReturnsAsync(
                new TokenResponse
                {
                    AccessToken = "access-token",
                    RefreshToken = "refresh-token",
                    AccessTokenExpiry = DateTime.UtcNow.AddMinutes(15),
                    RefreshTokenExpiry = DateTime.UtcNow.AddDays(7),
                    Success = true,
                }
            );

        var controller = CreateController(authService.Object);

        var result = await InvokeAsync(controller, "SessionLogin", new LoginRequest
        {
            Username = "alice",
            Password = "Secret123",
        });

        Assert.NotNull(result);
        Assert.True(GetBoolean(result!, "Success"));
        Assert.False(HasNestedProperty(result!, "Data", "AccessToken"));
        Assert.False(HasNestedProperty(result!, "Data", "RefreshToken"));

        var setCookieHeaders = controller.Response.Headers.SetCookie.ToArray();
        Assert.Contains(
            setCookieHeaders,
            header =>
                header != null
                && header.Contains("access_token=", StringComparison.OrdinalIgnoreCase)
                && header.Contains("httponly", StringComparison.OrdinalIgnoreCase)
        );
        Assert.Contains(
            setCookieHeaders,
            header =>
                header != null
                && header.Contains("refresh_token=", StringComparison.OrdinalIgnoreCase)
                && header.Contains("httponly", StringComparison.OrdinalIgnoreCase)
        );
    }

    [Fact]
    public async Task SessionLogin_RecordsResolvedLoginIpOnUser()
    {
        await _db.Insertable(
            new User
            {
                UserGUID = "ip-user-1",
                Username = "ipuser",
                Email = "ipuser@example.com",
                PasswordHash = "hashed",
                FullName = "IP User",
                IsActive = true,
                IsDeleted = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            }
        ).ExecuteCommandAsync();

        var authService = new Mock<IAuthService>();
        authService
            .Setup(service => service.LoginAsync(It.IsAny<LoginRequest>()))
            .ReturnsAsync(
                new LoginResponse
                {
                    Success = true,
                    User = new LoginUserDto
                    {
                        UserGUID = "ip-user-1",
                        Username = "ipuser",
                        Email = "ipuser@example.com",
                    },
                }
            );
        authService
            .Setup(service =>
                service.GenerateTokensAsync(
                    It.Is<User>(user => user.UserGUID == "ip-user-1"),
                    "203.0.113.9",
                    It.IsAny<string>()
                )
            )
            .ReturnsAsync(
                new TokenResponse
                {
                    AccessToken = "access-token",
                    RefreshToken = "refresh-token",
                    AccessTokenExpiry = DateTime.UtcNow.AddMinutes(15),
                    RefreshTokenExpiry = DateTime.UtcNow.AddDays(7),
                    Success = true,
                }
            );

        var controller = CreateController(authService.Object);
        controller.ControllerContext.HttpContext.Request.Headers["X-Forwarded-For"] =
            "203.0.113.9, 10.0.0.1";

        var result = await InvokeAsync(controller, "SessionLogin", new LoginRequest
        {
            Username = "ipuser",
            Password = "Secret123",
        });

        Assert.NotNull(result);
        Assert.True(GetBoolean(result!, "Success"));

        var user = await _db.Queryable<User>().FirstAsync(item => item.UserGUID == "ip-user-1");
        Assert.Equal("203.0.113.9", user!.LastLoginIp);
        Assert.NotNull(user.LastLoginAt);
        authService.Verify(
            service => service.GenerateTokensAsync(
                It.Is<User>(item => item.UserGUID == "ip-user-1"),
                "203.0.113.9",
                It.IsAny<string>()
            ),
            Times.Once
        );
    }


    [Fact]
    public async Task SessionRefresh_ReturnsCookieOnlySessionPayload()
    {
        var authService = new Mock<IAuthService>();
        authService
            .Setup(service =>
                service.RefreshTokensAsync(
                    It.IsAny<HttpContext>(),
                    It.IsAny<string>(),
                    It.IsAny<string>()
                )
            )
            .ReturnsAsync(
                new TokenResponse
                {
                    AccessToken = "new-access",
                    RefreshToken = "new-refresh",
                    AccessTokenExpiry = DateTime.UtcNow.AddMinutes(15),
                    RefreshTokenExpiry = DateTime.UtcNow.AddDays(7),
                    Success = true,
                }
            );

        var controller = CreateController(authService.Object);
        controller.ControllerContext.HttpContext.Request.Headers.UserAgent = "xunit";
        controller.ControllerContext.HttpContext.Request.Headers.Cookie = "refresh_token=cookie-token";

        var result = await InvokeAsync(controller, "SessionRefresh");

        Assert.NotNull(result);
        Assert.True(GetBoolean(result!, "Success"));
        Assert.False(HasNestedProperty(result!, "Data", "AccessToken"));
        Assert.False(HasNestedProperty(result!, "Data", "RefreshToken"));
    }

    [Fact]
    public async Task SessionLogout_RevokesRefreshTokenFromCookie()
    {
        var authService = new Mock<IAuthService>();
        authService
            .Setup(service => service.RevokeRefreshTokenAsync("refresh-cookie-token"))
            .ReturnsAsync(true);

        var controller = CreateController(authService.Object);
        controller.ControllerContext.HttpContext.Request.Headers.Cookie =
            "refresh_token=refresh-cookie-token";

        var result = await InvokeAsync(controller, "SessionLogout");

        Assert.NotNull(result);
        Assert.True(GetBoolean(result!, "Success"));
        authService.Verify(
            service => service.RevokeRefreshTokenAsync("refresh-cookie-token"),
            Times.Once
        );

        var setCookieHeaders = controller.Response.Headers.SetCookie.ToArray();
        Assert.Contains(
            setCookieHeaders,
            header => header != null && header.Contains("access_token=", StringComparison.OrdinalIgnoreCase)
        );
        Assert.Contains(
            setCookieHeaders,
            header => header != null && header.Contains("refresh_token=", StringComparison.OrdinalIgnoreCase)
        );
    }

    [Fact]
    public async Task GetCurrentUser_WhenLinkedStoreInactive_ReturnsStoreAndAllowsCurrentUserLoad()
    {
        await _db.Insertable(
            new User
            {
                UserGUID = "inactive-store-user",
                Username = "inactive-store-user",
                Email = "inactive-store-user@example.com",
                PasswordHash = "hashed",
                FullName = "Inactive Store User",
                IsActive = true,
                IsDeleted = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            }
        ).ExecuteCommandAsync();

        var userService = new Mock<IUserService>();
        userService
            .Setup(service => service.GetUserStoresAsync("inactive-store-user"))
            .ReturnsAsync(
                ApiResponse<List<UserStoreDto>>.OK(
                    new List<UserStoreDto>
                    {
                        new()
                        {
                            StoreGUID = "inactive-store",
                            StoreName = "Inactive Store",
                            StoreCode = "INACTIVE",
                            IsActive = false,
                            AssignedAt = DateTime.UtcNow,
                        },
                    },
                    "获取用户分店成功"
                )
            );

        var controller = CreateController(Mock.Of<IAuthService>(), userService: userService.Object);
        controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(
            new ClaimsIdentity(
                new[]
                {
                    new Claim("userId", "inactive-store-user"),
                    new Claim(ClaimTypes.NameIdentifier, "inactive-store-user"),
                },
                "TestAuthType"
            )
        );

        var result = await controller.GetCurrentUser();

        Assert.True(result.Success);
        var store = Assert.Single(result.Data!.Stores!);
        Assert.Equal("Inactive Store", store.StoreName);
        Assert.False(store.IsActive);
    }

    [Fact]
    public async Task GetCurrentUser_WhenUserHasDirectDashboardPermission_ReturnsDirectPermission()
    {
        await _db.Insertable(
            new User
            {
                UserGUID = "direct-permission-user",
                Username = "whs2",
                Email = "whs2@example.com",
                PasswordHash = "hashed",
                FullName = "WHS2",
                IsActive = true,
                IsDeleted = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            }
        ).ExecuteCommandAsync();
        await _db.Insertable(
            new SysUserPermission
            {
                Id = "direct-permission-user-dashboard",
                UserGuid = "direct-permission-user",
                PermissionCode = Permissions.Dashboard.View,
                IsDeleted = false,
            }
        ).ExecuteCommandAsync();

        var userService = new Mock<IUserService>();
        userService
            .Setup(service => service.GetUserStoresAsync("direct-permission-user"))
            .ReturnsAsync(ApiResponse<List<UserStoreDto>>.OK(new List<UserStoreDto>(), "获取用户分店成功"));

        var controller = CreateController(Mock.Of<IAuthService>(), userService: userService.Object);
        controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(
            new ClaimsIdentity(
                new[]
                {
                    new Claim("userId", "direct-permission-user"),
                    new Claim(ClaimTypes.NameIdentifier, "direct-permission-user"),
                },
                "TestAuthType"
            )
        );

        var result = await controller.GetCurrentUser();

        Assert.True(result.Success);
        Assert.Contains(Permissions.Dashboard.View, result.Data!.Permissions);
    }

    public void Dispose()
    {
        _db.Dispose();
        _sqliteConnection.Dispose();

        if (File.Exists(_dbPath))
        {
            SqliteTempFileCleanup.DeleteIfExists(_dbPath);
        }
    }

    private AuthController CreateController(
        IAuthService authService,
        IRoleService? roleService = null,
        IUserService? userService = null
    )
    {
        var controller = new AuthController(
            authService,
            CreateSqlSugarContext(_db),
            roleService ?? Mock.Of<IRoleService>(),
            userService ?? Mock.Of<IUserService>(),
            Mock.Of<ILogger<AuthController>>()
        );

        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Loopback;
        httpContext.Response.Body = new MemoryStream();
        controller.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
        {
            HttpContext = httpContext,
        };

        return controller;
    }

    private static async Task<object?> InvokeAsync(
        AuthController controller,
        string methodName,
        params object?[] args
    )
    {
        var method = typeof(AuthController).GetMethod(methodName);
        Assert.NotNull(method);

        var task = Assert.IsAssignableFrom<Task>(method!.Invoke(controller, args));
        await task;

        return task.GetType().GetProperty("Result")?.GetValue(task);
    }

    private static bool GetBoolean(object instance, string propertyName)
    {
        var property = instance.GetType().GetProperty(propertyName);
        Assert.NotNull(property);
        return Assert.IsType<bool>(property!.GetValue(instance));
    }

    private static bool HasNestedProperty(object instance, string outerProperty, string nestedProperty)
    {
        var outer = instance.GetType().GetProperty(outerProperty);
        Assert.NotNull(outer);

        var outerValue = outer!.GetValue(instance);
        Assert.NotNull(outerValue);

        return outerValue!.GetType().GetProperty(nestedProperty) != null;
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
}
