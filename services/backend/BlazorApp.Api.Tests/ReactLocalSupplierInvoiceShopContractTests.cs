using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using BlazorApp.Api.Controllers.React;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Moq;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class ReactLocalSupplierInvoiceShopContractTests
{
    private static readonly string[] HeaderPropertyNames =
    {
        nameof(ShopLocalSupplierInvoiceHeaderDto.InvoiceGUID),
        nameof(ShopLocalSupplierInvoiceHeaderDto.StoreCode),
        nameof(ShopLocalSupplierInvoiceHeaderDto.StoreName),
        nameof(ShopLocalSupplierInvoiceHeaderDto.SupplierCode),
        nameof(ShopLocalSupplierInvoiceHeaderDto.SupplierName),
        nameof(ShopLocalSupplierInvoiceHeaderDto.InvoiceNo),
        nameof(ShopLocalSupplierInvoiceHeaderDto.OrderDate),
        nameof(ShopLocalSupplierInvoiceHeaderDto.InboundDate),
        nameof(ShopLocalSupplierInvoiceHeaderDto.TotalAmount),
        nameof(ShopLocalSupplierInvoiceHeaderDto.ReceivedTotalAmount),
        nameof(ShopLocalSupplierInvoiceHeaderDto.FlowStatus),
        nameof(ShopLocalSupplierInvoiceHeaderDto.InboundStatus),
        nameof(ShopLocalSupplierInvoiceHeaderDto.Remarks),
    };

    private static readonly string[] ItemPropertyNames =
    {
        nameof(ShopLocalSupplierInvoiceItemDto.DetailGUID),
        nameof(ShopLocalSupplierInvoiceItemDto.StoreProductCode),
        nameof(ShopLocalSupplierInvoiceItemDto.ProductCode),
        nameof(ShopLocalSupplierInvoiceItemDto.ItemNumber),
        nameof(ShopLocalSupplierInvoiceItemDto.Barcode),
        nameof(ShopLocalSupplierInvoiceItemDto.ProductName),
        nameof(ShopLocalSupplierInvoiceItemDto.ProductImage),
        nameof(ShopLocalSupplierInvoiceItemDto.Specification),
        nameof(ShopLocalSupplierInvoiceItemDto.Unit),
        nameof(ShopLocalSupplierInvoiceItemDto.Quantity),
        nameof(ShopLocalSupplierInvoiceItemDto.LastPurchasePrice),
        nameof(ShopLocalSupplierInvoiceItemDto.PurchasePrice),
        nameof(ShopLocalSupplierInvoiceItemDto.RetailPrice),
        nameof(ShopLocalSupplierInvoiceItemDto.Amount),
        nameof(ShopLocalSupplierInvoiceItemDto.NewAutoRetailPrice),
    };

    [Fact]
    public async Task ShopGrid_MapsOnlyTheMinimalHeaderContract()
    {
        var request = new GridRequestDto();
        var source = CreateSensitiveHeaderSource();
        var invoices = new Mock<ILocalSupplierInvoicesReactService>(MockBehavior.Strict);
        invoices
            .Setup(service => service.GetGridDataAsync(request, null))
            .ReturnsAsync(
                GridResponseDto<LocalSupplierInvoiceListDto>.OK(
                    new List<LocalSupplierInvoiceListDto> { source },
                    1
                )
            );
        var controller = CreateController(invoices.Object);

        var result = await controller.ShopGrid(request);

        var ok = Assert.IsType<OkObjectResult>(result);
        var data = ReadAnonymousProperty<object>(ok.Value!, "data");
        var items = ReadAnonymousProperty<List<ShopLocalSupplierInvoiceHeaderDto>>(data, "Items");
        var item = Assert.Single(items);
        Assert.Equal(source.InvoiceGUID, item.InvoiceGUID);
        AssertExactProperties(typeof(ShopLocalSupplierInvoiceHeaderDto), HeaderPropertyNames);
        invoices.VerifyAll();
    }

    [Fact]
    public async Task GetShopInvoice_MapsOnlyTheMinimalHeaderContract()
    {
        var source = new LocalSupplierInvoiceDetailDto
        {
            InvoiceGUID = "invoice-1",
            StoreCode = "S01",
            SupplierCode = "SUP01",
            InvoiceNo = "INV-1",
            AppGUID = "internal-app",
            PcGUID = "internal-pc",
            ImportTemplate = "internal-template",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        var invoices = new Mock<ILocalSupplierInvoicesReactService>(MockBehavior.Strict);
        invoices
            .Setup(service => service.GetInvoiceAsync("invoice-1"))
            .ReturnsAsync(ApiResponse<LocalSupplierInvoiceDetailDto>.OK(source));
        var controller = CreateController(invoices.Object);

        var result = await controller.GetShopInvoice("invoice-1");

        var ok = Assert.IsType<OkObjectResult>(result);
        var item = Assert.IsType<ShopLocalSupplierInvoiceHeaderDto>(
            ReadAnonymousProperty<object>(ok.Value!, "data")
        );
        Assert.Equal(source.InvoiceGUID, item.InvoiceGUID);
        AssertExactProperties(typeof(ShopLocalSupplierInvoiceHeaderDto), HeaderPropertyNames);
        invoices.VerifyAll();
    }

    [Fact]
    public async Task GetShopDetailsGrid_MapsOnlyTheMinimalProductContract()
    {
        var request = new GridRequestDto();
        var source = new LocalSupplierInvoiceItemDto
        {
            DetailGUID = "detail-1",
            ProductCode = "P01",
            ItemNumber = "ITEM-01",
            ProductName = "Product One",
            ExistingProductCount = 99,
            BarcodeStatus = 2,
            BarcodeMatchCount = 3,
            ActivityType = 4,
        };
        var invoices = new Mock<ILocalSupplierInvoicesReactService>(MockBehavior.Strict);
        invoices
            .Setup(service => service.GetDetailsGridAsync("invoice-1", request))
            .ReturnsAsync(
                GridResponseDto<LocalSupplierInvoiceItemDto>.OK(
                    new List<LocalSupplierInvoiceItemDto> { source },
                    1
                )
            );
        var controller = CreateController(invoices.Object);

        var result = await controller.GetShopDetailsGrid("invoice-1", request);

        var ok = Assert.IsType<OkObjectResult>(result);
        var data = ReadAnonymousProperty<object>(ok.Value!, "data");
        var items = ReadAnonymousProperty<List<ShopLocalSupplierInvoiceItemDto>>(data, "Items");
        var item = Assert.Single(items);
        Assert.Equal(source.DetailGUID, item.DetailGUID);
        AssertExactProperties(typeof(ShopLocalSupplierInvoiceItemDto), ItemPropertyNames);
        invoices.VerifyAll();
    }

    [Theory]
    [InlineData(nameof(ReactLocalSupplierInvoicesController.ShopGrid), "shop/grid")]
    [InlineData(nameof(ReactLocalSupplierInvoicesController.GetShopInvoice), "shop/{invoiceGuid}")]
    [InlineData(
        nameof(ReactLocalSupplierInvoicesController.GetShopDetailsGrid),
        "shop/{invoiceGuid}/details/grid"
    )]
    [InlineData(nameof(ReactLocalSupplierInvoicesController.GetFilterOptions), "shop/filter-options")]
    public void ShopEndpoints_UseDedicatedRoutes(string methodName, string expectedTemplate)
    {
        var method = typeof(ReactLocalSupplierInvoicesController).GetMethod(methodName)!;
        var routeTemplate = method.GetCustomAttribute<HttpMethodAttribute>()?.Template;

        Assert.Equal(expectedTemplate, routeTemplate);
    }

    private static LocalSupplierInvoiceListDto CreateSensitiveHeaderSource()
    {
        return new LocalSupplierInvoiceListDto
        {
            InvoiceGUID = "invoice-1",
            StoreCode = "S01",
            SupplierCode = "SUP01",
            InvoiceNo = "INV-1",
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "internal-creator",
            UpdatedAt = DateTime.UtcNow,
            UpdatedBy = "internal-updater",
        };
    }

    private static ReactLocalSupplierInvoicesController CreateController(
        ILocalSupplierInvoicesReactService invoices
    )
    {
        var authorization = new Mock<IAuthorizationService>(MockBehavior.Strict);
        authorization
            .Setup(service => service.AuthorizeAsync(
                It.IsAny<ClaimsPrincipal>(),
                It.IsAny<object?>(),
                It.IsAny<string>()))
            .ReturnsAsync((ClaimsPrincipal _, object? _, string policy) =>
                policy == Permissions.OrderFront.View
                    ? AuthorizationResult.Success()
                    : AuthorizationResult.Failed());
        var controller = new ReactLocalSupplierInvoicesController(
            invoices,
            CreateSqlSugarContext(),
            Mock.Of<ILocalSupplierInvoiceHqSyncService>(),
            Mock.Of<ILocalSupplierInvoiceImportService>(),
            authorizationService: authorization.Object
        );
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(
                    new ClaimsIdentity(
                        new[]
                        {
                            new Claim("userId", "user-1"),
                            new Claim(ClaimTypes.Role, "Admin"),
                        },
                        "TestAuth"
                    )
                ),
            },
        };
        return controller;
    }

    private static void AssertExactProperties(Type dtoType, IEnumerable<string> expectedNames)
    {
        Assert.Equal(
            expectedNames.OrderBy(name => name),
            dtoType.GetProperties().Select(property => property.Name).OrderBy(name => name)
        );
    }

    private static T ReadAnonymousProperty<T>(object value, string propertyName)
    {
        var property = value.GetType().GetProperty(propertyName)
            ?? throw new InvalidOperationException($"未找到属性 {propertyName}");
        return (T)property.GetValue(value)!;
    }

    private static SqlSugarContext CreateSqlSugarContext()
    {
        var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(
            typeof(SqlSugarContext)
        );
        var dbField = typeof(SqlSugarContext).GetField(
            "_db",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        dbField.SetValue(context, Mock.Of<ISqlSugarClient>());
        return context;
    }
}
