using System.Security.Claims;
using BlazorApp.Api.Controllers.React;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Constants;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class BatchProductSalesAnalysisControllerTests
{
    [Fact]
    public async Task Query_受限用户指定未授权门店返回403()
    {
        var service = new Mock<IBatchProductSalesAnalysisService>();
        service.Setup(x => x.QueryAsync(It.IsAny<BatchProductSalesQueryRequestDto>(), It.IsAny<IReadOnlyList<string>?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BatchProductSalesAnalysisForbiddenException());
        var controller = CreateController(service.Object, "User", ["S1"]);

        var result = await controller.Query(new BatchProductSalesQueryRequestDto { ItemNumbers = ["001"], StartDate = DateTime.Today, EndDate = DateTime.Today, StoreCodes = ["S2"] });

        Assert.IsType<ForbidResult>(result);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("WarehouseManager")]
    public async Task Options_实时全店角色传递全店范围(string role)
    {
        IReadOnlyList<string>? captured = ["unexpected"];
        var service = new Mock<IBatchProductSalesAnalysisService>();
        service.Setup(x => x.GetOptionsAsync(It.IsAny<IReadOnlyList<string>?>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<string>?, CancellationToken>((scope, _) => captured = scope)
            .ReturnsAsync(ApiResponse<BatchProductSalesOptionsDto>.OK(new()));

        var result = await CreateController(service.Object, role, ["S1"]).GetOptions();

        Assert.IsType<OkObjectResult>(result);
        Assert.Null(captured);
    }

    [Fact]
    public async Task Options_普通用户无门店传递空范围()
    {
        IReadOnlyList<string>? captured = null;
        var service = new Mock<IBatchProductSalesAnalysisService>();
        service.Setup(x => x.GetOptionsAsync(It.IsAny<IReadOnlyList<string>?>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<string>?, CancellationToken>((scope, _) => captured = scope)
            .ReturnsAsync(ApiResponse<BatchProductSalesOptionsDto>.OK(new()));

        await CreateController(service.Object, "User", []).GetOptions();

        Assert.NotNull(captured);
        Assert.Empty(captured!);
    }

    [Theory]
    [InlineData("options", "User")]
    [InlineData("query", "User")]
    [InlineData("detail", "User")]
    [InlineData("query", "WarehouseManager")]
    public async Task 权限撤销后旧JWT和展开权限不能继续查询(string endpoint, string role)
    {
        var service = new Mock<IBatchProductSalesAnalysisService>(MockBehavior.Strict);
        var controller = CreateController(service.Object, role, ["S1"], hasPermission: false);
        var result = endpoint switch
        {
            "options" => await controller.GetOptions(),
            "detail" => await controller.Detail(new()),
            _ => await controller.Query(new()),
        };
        Assert.IsType<ForbidResult>(result);
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task 实时权限快照失败时拒绝查询且不调用数据服务()
    {
        var service = new Mock<IBatchProductSalesAnalysisService>(MockBehavior.Strict);
        var controller = CreateController(service.Object, "User", ["S1"], snapshotSuccess: false);
        Assert.IsType<ForbidResult>(await controller.Query(new()));
        service.VerifyNoOtherCalls();
    }

    private static BatchProductSalesAnalysisController CreateController(IBatchProductSalesAnalysisService service, string role, List<string> stores, bool hasPermission = true, bool snapshotSuccess = true)
    {
        var roles = new Mock<IRoleService>();
        roles.Setup(x => x.GetUserPermissionSnapshotAsync("user-1"))
            .ReturnsAsync(snapshotSuccess ? ApiResponse<UserPermissionSnapshotDto>.OK(new UserPermissionSnapshotDto
            { UserGuid = "user-1", RoleNames = [role], IsSuperAdmin = role == "Admin",
              ExactPermissionCodes = hasPermission ? [Permissions.SalesDashboard.BatchProductSalesView] : [],
              PermissionCodes = [Permissions.SalesDashboard.BatchProductSalesView] })
            : ApiResponse<UserPermissionSnapshotDto>.Error("快照读取失败"));
        var users = new Mock<IUserService>();
        users.Setup(x => x.GetUserByGuidAsync("user-1")).ReturnsAsync(ApiResponse<UserDetailDto>.OK(new UserDetailDto
        {
            UserGUID = "user-1", Stores = stores.Select(code => new UserStoreDto { StoreCode = code }).ToList(),
        }));
        return new BatchProductSalesAnalysisController(service, users.Object, roles.Object, NullLogger<BatchProductSalesAnalysisController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user-1")], "test")) } },
        };
    }
}
