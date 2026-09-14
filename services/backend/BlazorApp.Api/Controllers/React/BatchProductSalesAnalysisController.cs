using System.Security.Claims;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers.React;

[ApiController]
[Route("api/react/v1/dashboard/batch-product-sales-analysis")]
[Authorize(Policy = Permissions.SalesDashboard.BatchProductSalesView)]
public sealed class BatchProductSalesAnalysisController : ControllerBase
{
    private readonly IBatchProductSalesAnalysisService _service;
    private readonly IUserService _userService;
    private readonly IRoleService _roleService;
    private readonly ILogger<BatchProductSalesAnalysisController> _logger;

    public BatchProductSalesAnalysisController(IBatchProductSalesAnalysisService service, IUserService userService,
        IRoleService roleService, ILogger<BatchProductSalesAnalysisController> logger)
    {
        _service = service; _userService = userService; _roleService = roleService; _logger = logger;
    }

    [HttpGet("options")]
    public async Task<IActionResult> GetOptions()
    {
        try { return Ok(await _service.GetOptionsAsync(await ResolveStoreScopeAsync(), HttpContext.RequestAborted)); }
        catch (BatchProductSalesAnalysisForbiddenException) { return Forbid(); }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested) { return new EmptyResult(); }
        catch (Exception ex) { return InternalError<BatchProductSalesOptionsDto>(ex, "批量货号销量选项加载失败"); }
    }

    [HttpPost("query")]
    public async Task<IActionResult> Query([FromBody] BatchProductSalesQueryRequestDto request)
    {
        try { return Ok(await _service.QueryAsync(request, await ResolveStoreScopeAsync(), HttpContext.RequestAborted)); }
        catch (BatchProductSalesAnalysisForbiddenException) { return Forbid(); }
        catch (BatchProductSalesAnalysisValidationException ex) { return BadRequest(ApiResponse<BatchProductSalesQueryResultDto>.Error(ex.Message)); }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested) { return new EmptyResult(); }
        catch (Exception ex) { return InternalError<BatchProductSalesQueryResultDto>(ex, "批量货号销量查询失败"); }
    }

    [HttpPost("detail")]
    public async Task<IActionResult> Detail([FromBody] BatchProductSalesDetailRequestDto request)
    {
        try { return Ok(await _service.GetDetailAsync(request, await ResolveStoreScopeAsync(), HttpContext.RequestAborted)); }
        catch (BatchProductSalesAnalysisForbiddenException) { return Forbid(); }
        catch (BatchProductSalesAnalysisValidationException ex) { return BadRequest(ApiResponse<BatchProductSalesDetailDto>.Error(ex.Message)); }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested) { return new EmptyResult(); }
        catch (Exception ex) { return InternalError<BatchProductSalesDetailDto>(ex, "批量货号销量明细查询失败"); }
    }

    /// <summary>权限快照失败时拒绝访问，不能信任可能过期的 JWT 角色或请求门店。</summary>
    private async Task<List<string>?> ResolveStoreScopeAsync()
    {
        var userGuid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(userGuid)) throw new BatchProductSalesAnalysisForbiddenException();
        var snapshot = await _roleService.GetUserPermissionSnapshotAsync(userGuid);
        if (snapshot?.Success != true || snapshot.Data == null) throw new BatchProductSalesAnalysisForbiddenException();
        // JWT 可能仍包含已撤销权限；每次读取都以实时精确授权为准。
        if (!snapshot.Data.IsSuperAdmin && !(snapshot.Data.ExactPermissionCodes ?? [])
            .Contains(Permissions.SalesDashboard.BatchProductSalesView, StringComparer.OrdinalIgnoreCase))
            throw new BatchProductSalesAnalysisForbiddenException();
        if (snapshot.Data.IsSuperAdmin) return null;
        var roles = snapshot.Data.RoleNames ?? [];
        if (roles.Any(role => Permissions.SuperAdminRoleNames.Contains(role, StringComparer.OrdinalIgnoreCase)
            || Permissions.WarehouseManagerRoleNames.Contains(role, StringComparer.OrdinalIgnoreCase))) return null;
        var user = await _userService.GetUserByGuidAsync(userGuid);
        if (user?.Success != true || user.Data == null) return [];
        return user.Data.Stores?.Where(store => !string.IsNullOrWhiteSpace(store.StoreCode))
            .Select(store => store.StoreCode.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? [];
    }

    private ObjectResult InternalError<T>(Exception ex, string message)
    {
        _logger.LogError(ex, "{Message}", message);
        return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse<T>.Error(message));
    }
}
