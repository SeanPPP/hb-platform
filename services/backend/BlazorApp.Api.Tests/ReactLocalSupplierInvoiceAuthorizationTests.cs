using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using BlazorApp.Api.Controllers.React;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Moq;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class ReactLocalSupplierInvoiceAuthorizationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly SqlSugarClient _db;

    public ReactLocalSupplierInvoiceAuthorizationTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
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
        _db.CodeFirst.InitTables<Store, UserStore, StoreLocalSupplierInvoice>();
    }

    [Fact]
    public async Task GetInvoice_MobileViewUserCanReadAnAssignedStoreInvoice()
    {
        await SeedScopedInvoiceAsync();
        var invoices = new Mock<ILocalSupplierInvoicesReactService>(MockBehavior.Strict);
        invoices
            .Setup(service => service.GetInvoiceAsync("invoice-1"))
            .ReturnsAsync(ApiResponse<LocalSupplierInvoiceDetailDto>.OK(new LocalSupplierInvoiceDetailDto()));
        var authorization = CreateAuthorizationService("LocalPurchase.MobileView");
        var controller = CreateController(invoices.Object, authorization.Object);

        var result = await controller.GetInvoice("invoice-1");

        Assert.IsType<OkObjectResult>(result);
        invoices.Verify(service => service.GetInvoiceAsync("invoice-1"), Times.Once);
        authorization.Verify(
            service => service.AuthorizeAsync(
                It.IsAny<ClaimsPrincipal>(),
                null,
                "LocalPurchase.MobileView"
            ),
            Times.Once
        );
    }

    [Fact]
    public async Task GetInvoice_ForbidsWhenNeitherMobileNorExistingReadPermissionIsGranted()
    {
        await SeedScopedInvoiceAsync();
        var invoices = new Mock<ILocalSupplierInvoicesReactService>(MockBehavior.Strict);
        var controller = CreateController(invoices.Object, CreateAuthorizationService().Object);

        var result = await controller.GetInvoice("invoice-1");

        Assert.IsType<ForbidResult>(result);
        invoices.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetInvoice_MobileViewUserCannotReadAnUnassignedStoreInvoice()
    {
        await SeedScopedInvoiceAsync();
        await _db.Insertable(
            new StoreLocalSupplierInvoice
            {
                InvoiceGUID = "invoice-other-store",
                StoreCode = "S02",
                IsDeleted = false,
            }
        ).ExecuteCommandAsync();
        var invoices = new Mock<ILocalSupplierInvoicesReactService>(MockBehavior.Strict);
        var controller = CreateController(
            invoices.Object,
            CreateAuthorizationService(Permissions.LocalPurchase.MobileView).Object
        );

        var result = await controller.GetInvoice("invoice-other-store");

        Assert.IsType<ForbidResult>(result);
        invoices.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetInvoice_WithoutAuthorizationServiceFailsClosed()
    {
        await SeedScopedInvoiceAsync();
        var invoices = new Mock<ILocalSupplierInvoicesReactService>(MockBehavior.Strict);
        var controller = CreateController(invoices.Object, authorizationService: null);

        var result = await controller.GetInvoice("invoice-1");

        Assert.IsType<ForbidResult>(result);
        invoices.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(nameof(ReactLocalSupplierInvoicesController.Grid))]
    [InlineData(nameof(ReactLocalSupplierInvoicesController.GetInvoice))]
    [InlineData(nameof(ReactLocalSupplierInvoicesController.GetDetails))]
    [InlineData(nameof(ReactLocalSupplierInvoicesController.GetDetailsGrid))]
    [InlineData(nameof(ReactLocalSupplierInvoicesController.GetBarcodeAbnormalDetails))]
    [InlineData(nameof(ReactLocalSupplierInvoicesController.GetProductsByBarcode))]
    [InlineData(nameof(ReactLocalSupplierInvoicesController.GetProductsByProductCode))]
    public async Task ReadEndpoints_WithoutEitherReadPermissionForbidBeforeBypassingStoreScope(
        string methodName
    )
    {
        var invoices = new Mock<ILocalSupplierInvoicesReactService>(MockBehavior.Strict);
        var controller = CreateController(
            invoices.Object,
            CreateAuthorizationService().Object,
            isAdmin: true
        );

        var result = await InvokeReadEndpointAsync(controller, methodName);

        Assert.IsType<ForbidResult>(result);
        invoices.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(nameof(ReactLocalSupplierInvoicesController.Grid))]
    [InlineData(nameof(ReactLocalSupplierInvoicesController.GetInvoice))]
    [InlineData(nameof(ReactLocalSupplierInvoicesController.GetDetails))]
    [InlineData(nameof(ReactLocalSupplierInvoicesController.GetDetailsGrid))]
    [InlineData(nameof(ReactLocalSupplierInvoicesController.GetBarcodeAbnormalDetails))]
    [InlineData(nameof(ReactLocalSupplierInvoicesController.GetProductsByBarcode))]
    [InlineData(nameof(ReactLocalSupplierInvoicesController.GetProductsByProductCode))]
    public void ReadEndpoints_DoNotUseTheSingleWebViewPolicy(string methodName)
    {
        var method = typeof(ReactLocalSupplierInvoicesController).GetMethod(methodName)!;

        Assert.DoesNotContain(
            method.GetCustomAttributes<AuthorizeAttribute>(inherit: false),
            attribute => !string.IsNullOrWhiteSpace(attribute.Policy)
        );
    }

    [Fact]
    public void MobileView_DoesNotBecomeAnAliasForWebWriteHqOrAnalysisPermissions()
    {
        const string mobileView = "LocalPurchase.MobileView";

        Assert.DoesNotContain(mobileView, Permissions.GetEquivalentPermissionCodes(Permissions.LocalPurchase.View));
        Assert.DoesNotContain(mobileView, Permissions.GetEquivalentPermissionCodes(Permissions.LocalPurchase.Edit));
        Assert.DoesNotContain(mobileView, Permissions.GetEquivalentPermissionCodes(Permissions.LocalPurchase.PushToHq));
        var analysisAuthorize = Assert.Single(
            typeof(ReactLocalSupplierInvoiceSalesAnalysisController)
                .GetCustomAttributes<AuthorizeAttribute>(inherit: false)
        );
        Assert.Equal(Permissions.LocalPurchase.View, analysisAuthorize.Policy);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private async Task SeedScopedInvoiceAsync()
    {
        await _db.Insertable(
            new Store
            {
                StoreGUID = "store-1",
                StoreCode = "S01",
                StoreName = "Sydney",
                IsActive = true,
                IsDeleted = false,
            }
        ).ExecuteCommandAsync();
        await _db.Insertable(
            new UserStore
            {
                UserStoreGUID = "user-store-1",
                UserGUID = "user-1",
                StoreGUID = "store-1",
            }
        ).ExecuteCommandAsync();
        await _db.Insertable(
            new StoreLocalSupplierInvoice
            {
                InvoiceGUID = "invoice-1",
                StoreCode = "S01",
                IsDeleted = false,
            }
        ).ExecuteCommandAsync();
    }

    private ReactLocalSupplierInvoicesController CreateController(
        ILocalSupplierInvoicesReactService invoices,
        IAuthorizationService? authorizationService,
        bool isAdmin = false
    )
    {
        var controller = new ReactLocalSupplierInvoicesController(
            invoices,
            CreateSqlSugarContext(_db),
            Mock.Of<ILocalSupplierInvoiceHqSyncService>(),
            Mock.Of<ILocalSupplierInvoiceImportService>(),
            authorizationService: authorizationService
        );
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(
                    new ClaimsIdentity(
                        isAdmin
                            ? new[]
                            {
                                new Claim("userId", "user-1"),
                                new Claim(ClaimTypes.Role, "Admin"),
                            }
                            : new[] { new Claim("userId", "user-1") },
                        "TestAuth"
                    )
                ),
            },
        };
        return controller;
    }

    private static Task<IActionResult> InvokeReadEndpointAsync(
        ReactLocalSupplierInvoicesController controller,
        string methodName
    )
    {
        return methodName switch
        {
            nameof(ReactLocalSupplierInvoicesController.Grid) => controller.Grid(new GridRequestDto()),
            nameof(ReactLocalSupplierInvoicesController.GetInvoice) => controller.GetInvoice("invoice-1"),
            nameof(ReactLocalSupplierInvoicesController.GetDetails) => controller.GetDetails("invoice-1"),
            nameof(ReactLocalSupplierInvoicesController.GetDetailsGrid) => controller.GetDetailsGrid(
                "invoice-1",
                new GridRequestDto()
            ),
            nameof(ReactLocalSupplierInvoicesController.GetBarcodeAbnormalDetails) =>
                controller.GetBarcodeAbnormalDetails("invoice-1"),
            nameof(ReactLocalSupplierInvoicesController.GetProductsByBarcode) =>
                controller.GetProductsByBarcode("invoice-1", "barcode"),
            nameof(ReactLocalSupplierInvoicesController.GetProductsByProductCode) =>
                controller.GetProductsByProductCode("invoice-1", "product-code"),
            _ => throw new ArgumentOutOfRangeException(nameof(methodName), methodName, null),
        };
    }

    private static Mock<IAuthorizationService> CreateAuthorizationService(
        params string[] allowedPolicies
    )
    {
        var allowed = new HashSet<string>(allowedPolicies, StringComparer.OrdinalIgnoreCase);
        var authorization = new Mock<IAuthorizationService>(MockBehavior.Strict);
        authorization
            .Setup(service => service.AuthorizeAsync(
                It.IsAny<ClaimsPrincipal>(),
                It.IsAny<object?>(),
                It.IsAny<string>()))
            .ReturnsAsync((ClaimsPrincipal _, object? _, string policy) =>
                allowed.Contains(policy) ? AuthorizationResult.Success() : AuthorizationResult.Failed());
        return authorization;
    }

    private static SqlSugarContext CreateSqlSugarContext(ISqlSugarClient db)
    {
        var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(SqlSugarContext));
        var dbField = typeof(SqlSugarContext).GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!;
        dbField.SetValue(context, db);
        return context;
    }
}
