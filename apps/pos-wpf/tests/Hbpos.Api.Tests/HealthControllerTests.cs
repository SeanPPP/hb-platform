using Hbpos.Api.Controllers;
using Hbpos.Contracts.Common;
using Hbpos.Contracts.Health;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hbpos.Api.Tests;

public sealed class HealthControllerTests
{
    [Fact]
    public void HealthEndpoint_KeepsExpectedRouteAndAllowsAnonymous()
    {
        Assert.Equal("health", typeof(HealthController)
            .GetMethod(nameof(HealthController.Get))?
            .GetCustomAttributes(typeof(HttpGetAttribute), inherit: false)
            .Cast<HttpGetAttribute>()
            .Single()
            .Template);
        Assert.NotNull(typeof(HealthController)
            .GetMethod(nameof(HealthController.Get))?
            .GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: false)
            .SingleOrDefault());
    }

    [Fact]
    public void Get_ReturnsOnlineHealthResponse()
    {
        var controller = new HealthController();

        var result = controller.Get();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var apiResult = Assert.IsType<ApiResult<HealthCheckResponse>>(ok.Value);
        Assert.True(apiResult.Success);
        Assert.NotNull(apiResult.Data);
        Assert.True(apiResult.Data.IsOnline);
        Assert.Equal("ok", apiResult.Data.Status);
    }
}
