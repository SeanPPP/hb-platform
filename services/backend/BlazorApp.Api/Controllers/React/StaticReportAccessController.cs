using System.Security.Claims;
using System.Text.RegularExpressions;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SqlSugar;

namespace BlazorApp.Api.Controllers.React;

/// <summary>
/// 静态报告访问鉴权。/reports/kfc-uncle-bills/ 下的文件由 nginx 直接提供，
/// 每个请求先经 auth_request 调用这里（nginx 用 X-Original-URI 传原始请求地址）：
/// - 页面外壳、缩略图、放大图等公共文件：有「查看 KFC 补货信号」即放行；
/// - data/all.json（全链汇总）：还要有「查看 KFC 补货信号全部门店」；
/// - data/store-{门店}.json：有全部门店权限，或该门店在用户的关联门店里。
/// 204 放行；401 未登录（nginx 转续期中转页）；403 无权限。不返回任何业务数据。
/// 另提供 scope 接口，告诉报告页面当前用户能看哪些门店。
/// </summary>
[ApiController]
[Route("api/react/v1/static-report-access")]
public sealed class StaticReportAccessController(
    IRoleService roleService,
    SqlSugarContext context,
    ILogger<StaticReportAccessController> logger
) : ControllerBase
{
    internal const string KfcReportRoot = "/reports/kfc-uncle-bills";
    internal const string KfcDataPrefix = KfcReportRoot + "/data/";

    // 数据文件名必须严格匹配；data/ 下任何其它名字一律拒绝（失败即关闭）
    private static readonly Regex KfcDataFile = new(
        @"^/reports/kfc-uncle-bills/data/(?:(?<all>all)|store-(?<store>[A-Za-z0-9_-]{1,32}))\.json$",
        RegexOptions.CultureInvariant
    );

    [HttpGet("kfc-uncle-bills")]
    [Authorize(Policy = Permissions.SalesDashboard.KfcRestockSignalView)]
    public async Task<IActionResult> KfcUncleBills()
    {
        // nginx 与浏览器都不能缓存鉴权结果，否则撤销权限后仍可访问
        Response.Headers.CacheControl = "no-store";

        var path = NormalizeOriginalPath(Request.Headers["X-Original-URI"].FirstOrDefault());
        if (path == null || !(path == KfcReportRoot || path.StartsWith(KfcReportRoot + "/", StringComparison.Ordinal)))
        {
            // 只为 nginx 转发的本报告请求服务；缺少或伪造到别处的地址一律拒绝
            return Forbidden();
        }
        if (!path.StartsWith(KfcDataPrefix, StringComparison.Ordinal))
        {
            return NoContent();
        }

        var match = KfcDataFile.Match(path);
        var userGuid = CurrentUserGuid();
        if (!match.Success || userGuid == null)
        {
            return Forbidden();
        }

        try
        {
            var scope = await ResolveScopeAsync(userGuid);
            if (scope.AllStores)
            {
                return NoContent();
            }
            if (match.Groups["all"].Success)
            {
                return Forbidden();
            }
            var storeCode = match.Groups["store"].Value;
            return scope.StoreCodes.Contains(storeCode, StringComparer.OrdinalIgnoreCase) ? NoContent() : Forbidden();
        }
        catch (Exception ex)
        {
            // auth_request 把 401/403 以外的状态都当 500；这里按拒绝处理并记录
            logger.LogError(ex, "KFC 补货信号报告鉴权失败 UserGuid={UserGuid} Path={Path}", userGuid, path);
            return Forbidden();
        }
    }

    /// <summary>报告页面加载时调用：当前用户能看全部门店，或者能看哪些关联门店。</summary>
    [HttpGet("kfc-uncle-bills/scope")]
    [Authorize(Policy = Permissions.SalesDashboard.KfcRestockSignalView)]
    public async Task<IActionResult> KfcUncleBillsScope()
    {
        Response.Headers.CacheControl = "no-store";
        var userGuid = CurrentUserGuid();
        if (userGuid == null)
        {
            return Forbidden();
        }
        try
        {
            var scope = await ResolveScopeAsync(userGuid);
            return Ok(new { success = true, data = new { allStores = scope.AllStores, storeCodes = scope.StoreCodes } });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "读取 KFC 补货信号报告门店范围失败 UserGuid={UserGuid}", userGuid);
            return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = "读取门店范围失败" });
        }
    }

    /// <summary>
    /// 规整 nginx 传来的原始请求地址：去掉查询串、URL 解码、合并多余斜杠、解析 . 与 ..。
    /// nginx 取文件前会做同样的规整，不规整就判断会被 img/../data/all.json 之类的地址绕过。
    /// 含控制字符、反斜杠或越过根目录的地址返回 null。
    /// </summary>
    internal static string? NormalizeOriginalPath(string? originalUri)
    {
        if (string.IsNullOrEmpty(originalUri))
        {
            return null;
        }
        var raw = originalUri.Split('?', '#')[0];
        string decoded;
        try
        {
            decoded = Uri.UnescapeDataString(raw);
        }
        catch (UriFormatException)
        {
            return null;
        }
        if (!decoded.StartsWith('/') || decoded.Any(char.IsControl) || decoded.Contains('\\'))
        {
            return null;
        }

        var segments = new List<string>();
        foreach (var segment in decoded.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
            {
                continue;
            }
            if (segment == "..")
            {
                if (segments.Count == 0)
                {
                    return null;
                }
                segments.RemoveAt(segments.Count - 1);
                continue;
            }
            segments.Add(segment);
        }
        var trailingSlash = decoded.EndsWith('/') && segments.Count > 0 ? "/" : string.Empty;
        return "/" + string.Join('/', segments) + trailingSlash;
    }

    private string? CurrentUserGuid()
    {
        var value = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("userId")?.Value;
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    // 全部门店权限实时判定（管理员角色在 RoleService 同一条查询里视为拥有）；否则取用户关联的启用门店
    private async Task<(bool AllStores, List<string> StoreCodes)> ResolveScopeAsync(string userGuid)
    {
        var allStores = await roleService.UserHasPermissionAsync(
            userGuid,
            Permissions.SalesDashboard.KfcRestockSignalAllStores
        );
        if (allStores?.Data == true)
        {
            return (true, new List<string>());
        }

        var codes = await context.Db.Queryable<UserStore>()
            .InnerJoin<Store>((userStore, store) => userStore.StoreGUID == store.StoreGUID)
            .Where((userStore, store) => userStore.UserGUID == userGuid && !userStore.IsDeleted && !store.IsDeleted && store.IsActive)
            .Select((userStore, store) => store.StoreCode)
            .ToListAsync();
        var storeCodes = codes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToList();
        return (false, storeCodes);
    }

    private IActionResult Forbidden() => StatusCode(StatusCodes.Status403Forbidden);
}
