using System.Text.Json;
using BlazorApp.Api.Services.RustDeskCompat;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BlazorApp.Api.Controllers;

/// <summary>
/// RustDesk 1.4.9 客户端兼容 API。该控制器使用独立 opaque bearer，不读取 HB Cookie/JWT 身份。
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/rustdesk/api")]
[RequestSizeLimit(16 * 1024)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class RustDeskCompatController : ControllerBase
{
    private const string CompanyAddressBookGuid = "hb-company-devices";
    private const string PersonalAddressBookGuid = "hb-personal-empty";
    private const string CompanyDeviceGroupName = "公司设备";
    private const string BearerPrefix = "Bearer ";

    private readonly IRustDeskCompatService _service;
    private readonly ILogger<RustDeskCompatController> _logger;

    public RustDeskCompatController(
        IRustDeskCompatService service,
        ILogger<RustDeskCompatController> logger)
    {
        _service = service;
        _logger = logger;
    }

    // 官方客户端打开登录框时先查询第三方登录方式；公司账号使用原生密码表单。
    [HttpGet("login-options")]
    public IActionResult LoginOptions() => Ok(Array.Empty<string>());

    [HttpPost("login")]
    [EnableRateLimiting("RustDeskLogin")]
    public async Task<IActionResult> Login(
        [FromBody] RustDeskLoginRequest request,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(request.Type)
            && !string.Equals(request.Type, "account", StringComparison.OrdinalIgnoreCase))
        {
            return Unauthorized(new { error = "Invalid credentials" });
        }

        try
        {
            var result = await _service.LoginAsync(
                new RustDeskLoginRequest
                {
                    Username = request.Username,
                    Password = request.Password,
                    Id = request.Id,
                    Uuid = request.Uuid,
                    AutoLogin = request.AutoLogin,
                    Type = "account",
                },
                HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
                cancellationToken);
            if (result is null || string.IsNullOrWhiteSpace(result.AccessToken))
            {
                return Unauthorized(new { error = "Invalid credentials" });
            }

            return Ok(new
            {
                type = "access_token",
                access_token = result.AccessToken,
                user = ToUserPayload(result.User),
            });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("RustDesk 登录服务未就绪: {ExceptionType}", ex.GetType().Name);
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "Service unavailable" });
        }
        catch (Exception ex)
        {
            _logger.LogError("RustDesk request failed ({ExceptionType}). {Operation}", ex.GetType().Name, "RustDesk 登录失败");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "Request failed" });
        }
    }

    [HttpPost("currentUser")]
    public async Task<IActionResult> CurrentUser(CancellationToken cancellationToken)
    {
        var required = await RequireUserAsync(cancellationToken);
        if (required.Failure is not null)
        {
            return required.Failure;
        }

        return Ok(ToUserPayload(required.User!));
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        var token = ReadBearerToken();
        if (token is null)
        {
            return Unauthorized(new { error = "Invalid token" });
        }

        try
        {
            if (await _service.AuthenticateAsync(token, cancellationToken) is null)
            {
                return Unauthorized(new { error = "Invalid token" });
            }

            await _service.LogoutAsync(token, cancellationToken);
            return Ok(new { });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("RustDesk 登出服务未就绪: {ExceptionType}", ex.GetType().Name);
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "Service unavailable" });
        }
        catch (Exception ex)
        {
            _logger.LogError("RustDesk request failed ({ExceptionType}). {Operation}", ex.GetType().Name, "RustDesk 登出失败");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "Request failed" });
        }
    }

    // 官方客户端的设备组页与通讯录页使用不同协议，三个 GET 接口必须同时可用。
    [HttpGet("device-group/accessible")]
    public async Task<IActionResult> AccessibleDeviceGroups(
        [FromQuery] int current = 1,
        [FromQuery] int pageSize = 100,
        CancellationToken cancellationToken = default)
    {
        var required = await RequireUserAsync(cancellationToken);
        if (required.Failure is not null)
        {
            return required.Failure;
        }

        if (current < 1 || pageSize is < 1 or > 100)
        {
            return BadRequest(new { error = "Invalid group request" });
        }

        var data = new[] { new { name = CompanyDeviceGroupName } };
        return Ok(new { total = 1, data = current == 1 ? data : [] });
    }

    [HttpGet("users")]
    public async Task<IActionResult> AccessibleUsers(
        [FromQuery] int current = 1,
        [FromQuery] int pageSize = 100,
        CancellationToken cancellationToken = default)
    {
        var required = await RequireUserAsync(cancellationToken);
        if (required.Failure is not null)
        {
            return required.Failure;
        }

        if (current < 1 || pageSize is < 1 or > 100)
        {
            return BadRequest(new { error = "Invalid group request" });
        }

        // 设备统一放在公司组，不公开公司用户目录，也不虚构设备的账号归属。
        return Ok(new { total = 0, data = Array.Empty<object>() });
    }

    [HttpGet("peers")]
    public async Task<IActionResult> GroupPeers(
        [FromQuery] int current = 1,
        [FromQuery] int pageSize = 100,
        CancellationToken cancellationToken = default)
    {
        var required = await RequireUserAsync(cancellationToken);
        if (required.Failure is not null)
        {
            return required.Failure;
        }

        if (current < 1 || pageSize is < 1 or > 100)
        {
            return BadRequest(new { error = "Invalid group request" });
        }

        try
        {
            var peers = await _service.GetPeersAsync(required.User!, cancellationToken);
            var data = peers
                .Skip((int)Math.Min(int.MaxValue, ((long)current - 1) * pageSize))
                .Take(pageSize)
                .Select(peer => new
                {
                    id = peer.Id,
                    // GroupModel 从 info 读取平台和设备名，不能直接复用通讯录的扁平 JSON。
                    info = new
                    {
                        username = peer.Username,
                        device_name = string.IsNullOrWhiteSpace(peer.Alias) ? peer.Hostname : peer.Alias,
                        os = string.Equals(peer.Platform, "Mac OS", StringComparison.OrdinalIgnoreCase)
                            ? "macos" : peer.Platform.ToLowerInvariant(),
                    },
                    device_group_name = CompanyDeviceGroupName,
                    note = string.Empty,
                })
                .ToArray();
            return Ok(new { total = peers.Count, data });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("RustDesk 设备组服务未就绪: {ExceptionType}", ex.GetType().Name);
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "Service unavailable" });
        }
        catch (Exception ex)
        {
            _logger.LogError("RustDesk 设备组读取失败: {ExceptionType}", ex.GetType().Name);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "Request failed" });
        }
    }

    [HttpPost("ab/personal")]
    public async Task<IActionResult> PersonalAddressBook(CancellationToken cancellationToken)
    {
        var required = await RequireUserAsync(cancellationToken);
        return required.Failure ?? Ok(new { guid = PersonalAddressBookGuid });
    }

    [HttpPost("ab/settings")]
    public async Task<IActionResult> AddressBookSettings(CancellationToken cancellationToken)
    {
        var required = await RequireUserAsync(cancellationToken);
        return required.Failure ?? Ok(new { max_peer_one_ab = 0 });
    }

    [HttpPost("ab/shared/profiles")]
    public async Task<IActionResult> SharedAddressBookProfiles(CancellationToken cancellationToken)
    {
        var required = await RequireUserAsync(cancellationToken);
        // 官方客户端仅在共享地址簿连接路径使用 peer.password；rule=1 表示只读。
        return required.Failure ?? Ok(new
        {
            total = 1,
            data = new[] { new { guid = CompanyAddressBookGuid, name = CompanyDeviceGroupName, rule = 1, info = new { } } },
        });
    }

    [HttpPost("ab/peers")]
    public async Task<IActionResult> AddressBookPeers(
        [FromQuery] int current = 1,
        [FromQuery] int pageSize = 100,
        [FromQuery] string? ab = null,
        CancellationToken cancellationToken = default)
    {
        var required = await RequireUserAsync(cancellationToken);
        if (required.Failure is not null)
        {
            return required.Failure;
        }

        var isPersonal = string.Equals(ab, PersonalAddressBookGuid, StringComparison.Ordinal);
        if ((!isPersonal && !string.Equals(ab, CompanyAddressBookGuid, StringComparison.Ordinal))
            || current < 1 || pageSize is < 1 or > 100)
        {
            return BadRequest(new { error = "Invalid address book request" });
        }

        if (isPersonal) return Ok(new { total = 0, data = Array.Empty<RustDeskPeer>() });

        try
        {
            var peers = await _service.GetAddressBookPeersAsync(required.User!, current, pageSize, cancellationToken);
            return Ok(new { total = peers.Total, data = peers.Data });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("RustDesk 通讯录服务未就绪: {ExceptionType}", ex.GetType().Name);
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "Service unavailable" });
        }
        catch (Exception ex)
        {
            _logger.LogError("RustDesk request failed ({ExceptionType}). {Operation}", ex.GetType().Name, "RustDesk 通讯录读取失败");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "Request failed" });
        }
    }

    [HttpPost("ab/tags/{guid}")]
    public async Task<IActionResult> AddressBookTags(
        [FromRoute] string guid,
        CancellationToken cancellationToken)
    {
        var required = await RequireUserAsync(cancellationToken);
        if (required.Failure is not null)
        {
            return required.Failure;
        }

        if (string.Equals(guid, PersonalAddressBookGuid, StringComparison.Ordinal))
            return Ok(Array.Empty<object>());

        if (!string.Equals(guid, CompanyAddressBookGuid, StringComparison.Ordinal))
        {
            return BadRequest(new { error = "Invalid address book" });
        }

        try
        {
            var peers = await _service.GetPeersAsync(required.User!, cancellationToken);
            var tags = peers
                .SelectMany(peer => peer.Tags)
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Distinct(StringComparer.Ordinal)
                .Select(name => new { name, color = 0 })
                .ToArray();
            return Ok(tags);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("RustDesk 标签服务未就绪: {ExceptionType}", ex.GetType().Name);
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "Service unavailable" });
        }
        catch (Exception ex)
        {
            _logger.LogError("RustDesk request failed ({ExceptionType}). {Operation}", ex.GetType().Name, "RustDesk 标签读取失败");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "Request failed" });
        }
    }

    /// <summary>
    /// legacy 客户端读取单一通讯录；data 必须是 RustDesk 期望的 JSON 字符串。
    /// </summary>
    [HttpGet("ab")]
    public async Task<IActionResult> LegacyAddressBook(CancellationToken cancellationToken)
    {
        var required = await RequireUserAsync(cancellationToken);
        if (required.Failure is not null)
        {
            return required.Failure;
        }

        try
        {
            var peers = await _service.GetPeersAsync(required.User!, cancellationToken);
            var tags = peers
                .SelectMany(peer => peer.Tags)
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var data = JsonSerializer.Serialize(new
            {
                tags,
                peers,
                tag_colors = "{}",
            });

            return Ok(new { licensed_devices = 0, data });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("RustDesk legacy 通讯录服务未就绪: {ExceptionType}", ex.GetType().Name);
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "Service unavailable" });
        }
        catch (Exception ex)
        {
            _logger.LogError("RustDesk request failed ({ExceptionType}). {Operation}", ex.GetType().Name, "RustDesk legacy 通讯录读取失败");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "Request failed" });
        }
    }

    [HttpPost("ab")]
    public IActionResult RejectLegacyMutation() => ReadOnlyAddressBook();

    [AcceptVerbs("POST", "PUT", "PATCH", "DELETE")]
    [Route("ab/{**path}")]
    public IActionResult RejectAddressBookMutation(string? path) => ReadOnlyAddressBook();

    [HttpPost("heartbeat")]
    public async Task<IActionResult> Heartbeat(CancellationToken cancellationToken)
    {
        var required = await RequireUserAsync(cancellationToken);
        return required.Failure ?? Ok(new { });
    }

    [HttpPost("sysinfo")]
    public async Task<IActionResult> SysInfo(CancellationToken cancellationToken)
    {
        var required = await RequireUserAsync(cancellationToken);
        if (required.Failure is not null)
        {
            return required.Failure;
        }

        return Content("SYSINFO_UPDATED", "text/plain");
    }

    private IActionResult ReadOnlyAddressBook() =>
        StatusCode(StatusCodes.Status403Forbidden, new { error = "Address book is read-only" });

    private string? ReadBearerToken()
    {
        var header = Request.Headers.Authorization.ToString();
        if (header.Length <= BearerPrefix.Length
            || !header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var token = header[BearerPrefix.Length..].Trim();
        return token.Length == 0 ? null : token;
    }

    private async Task<(RustDeskAuthenticatedUser? User, IActionResult? Failure)> RequireUserAsync(
        CancellationToken cancellationToken)
    {
        var token = ReadBearerToken();
        if (token is null)
        {
            return (null, Unauthorized(new { error = "Invalid token" }));
        }

        try
        {
            var user = await _service.AuthenticateAsync(token, cancellationToken);
            return user is null
                ? (null, Unauthorized(new { error = "Invalid token" }))
                : (user, null);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("RustDesk 认证服务未就绪: {ExceptionType}", ex.GetType().Name);
            return (null, StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "Service unavailable" }));
        }
        catch (Exception ex)
        {
            _logger.LogError("RustDesk request failed ({ExceptionType}). {Operation}", ex.GetType().Name, "RustDesk 认证失败");
            return (null, StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "Request failed" }));
        }
    }

    private static object ToUserPayload(RustDeskAuthenticatedUser user) => new
    {
        name = user.UserName,
        display_name = user.DisplayName,
        avatar = string.Empty,
        email = string.Empty,
        note = string.Empty,
        status = 1,
        is_admin = false,
    };
}
