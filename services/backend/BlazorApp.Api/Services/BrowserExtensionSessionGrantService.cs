using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BlazorApp.Api.Data;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using SqlSugar;

namespace BlazorApp.Api.Services;

/// <summary>
/// 将已登录的网站 Cookie 会话安全地换成扩展可用的短期 bearer，会话授权码仅可消费一次。
/// </summary>
public sealed partial class BrowserExtensionSessionGrantService
{
    public const string ClientId = "hb-supplier-order";
    public const string InvalidGrantErrorCode = "EXTENSION_GRANT_INVALID";
    public const string CookieSessionRequiredErrorCode = "EXTENSION_AUTH_COOKIE_REQUIRED";

    private const string InvalidRequestErrorCode = "EXTENSION_REQUEST_INVALID";
    private const int GrantLifetimeSeconds = 60;
    internal const int CleanupBatchSize = 200;
    internal static readonly TimeSpan CleanupRetention = TimeSpan.FromHours(24);
    private readonly ISqlSugarClient _db;
    private readonly IAuthService _authService;
    private readonly TimeProvider _timeProvider;

    public BrowserExtensionSessionGrantService(
        SqlSugarContext dbContext,
        IAuthService authService,
        TimeProvider? timeProvider = null
    )
    {
        _db = dbContext.Db;
        _authService = authService;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<ApiResponse<BrowserExtensionAuthorizeResponse>> AuthorizeAsync(
        string userGuid,
        string parentSessionId,
        BrowserExtensionAuthorizeRequest request
    )
    {
        var codeChallenge = request?.CodeChallenge ?? string.Empty;
        var state = request?.State ?? string.Empty;
        var clientId = request?.ClientId ?? string.Empty;
        if (
            string.IsNullOrWhiteSpace(userGuid)
            || string.IsNullOrWhiteSpace(parentSessionId)
            || !string.Equals(clientId, ClientId, StringComparison.Ordinal)
            || !CodeChallengeRegex().IsMatch(codeChallenge)
            || !StateRegex().IsMatch(state)
        )
        {
            return ApiResponse<BrowserExtensionAuthorizeResponse>.Error(
                "扩展授权请求无效",
                InvalidRequestErrorCode
            );
        }

        var now = UtcNow();
        if (!await IsActiveParentSessionAsync(userGuid, parentSessionId, now))
        {
            return ApiResponse<BrowserExtensionAuthorizeResponse>.Error(
                "网站登录会话无效或已过期",
                InvalidGrantErrorCode
            );
        }

        await CleanupExpiredGrantsAsync(now);

        var code = Base64Url(RandomNumberGenerator.GetBytes(32));
        var expiresAtUtc = now.AddSeconds(GrantLifetimeSeconds);
        var entity = new BrowserExtensionSessionGrantEntity
        {
            GrantId = Guid.NewGuid(),
            CodeHash = HashCode(code),
            CodeChallenge = codeChallenge,
            State = state,
            ParentSessionId = parentSessionId,
            UserGuid = userGuid,
            ClientId = ClientId,
            IssuedAtUtc = now,
            ExpiresAtUtc = expiresAtUtc,
        };

        // 关键逻辑：数据库只保存授权码摘要；明文 code 只在本次响应中返回。
        await _db.Insertable(entity).ExecuteCommandAsync();
        return ApiResponse<BrowserExtensionAuthorizeResponse>.OK(
            new BrowserExtensionAuthorizeResponse
            {
                Code = code,
                State = state,
                ExpiresAtUtc = expiresAtUtc,
            }
        );
    }

    public async Task<ApiResponse<BrowserExtensionTokenResponse>> ExchangeAsync(
        BrowserExtensionTokenRequest request
    )
    {
        var code = request?.Code ?? string.Empty;
        var verifier = request?.CodeVerifier ?? string.Empty;
        var state = request?.State ?? string.Empty;
        var clientId = request?.ClientId ?? string.Empty;
        if (
            !string.Equals(clientId, ClientId, StringComparison.Ordinal)
            || !AuthorizationCodeRegex().IsMatch(code)
            || !CodeVerifierRegex().IsMatch(verifier)
            || !StateRegex().IsMatch(state)
        )
        {
            return InvalidGrant();
        }

        var now = UtcNow();
        var codeHash = HashCode(code);
        var grant = await _db.Queryable<BrowserExtensionSessionGrantEntity>()
            .FirstAsync(item => item.CodeHash == codeHash);
        if (
            grant == null
            || grant.ConsumedAtUtc.HasValue
            || grant.ExpiresAtUtc < now
            || !string.Equals(grant.ClientId, ClientId, StringComparison.Ordinal)
            || !FixedTimeEquals(grant.State, state)
            || !FixedTimeEquals(grant.CodeChallenge, CreateCodeChallenge(verifier))
            || !await IsActiveParentSessionAsync(grant.UserGuid, grant.ParentSessionId, now)
        )
        {
            return InvalidGrant();
        }

        // 条件更新是跨进程的一次性消费闸门；并发兑换只有一个请求能把 NULL 改为消费时间。
        var consumed = await _db.Updateable<BrowserExtensionSessionGrantEntity>()
            .SetColumns(item => item.ConsumedAtUtc == now)
            .Where(item =>
                item.GrantId == grant.GrantId
                && item.ConsumedAtUtc == null
                && item.ExpiresAtUtc >= now
            )
            .ExecuteCommandAsync();
        if (consumed != 1)
        {
            return InvalidGrant();
        }

        var token = await _authService.IssueBrowserExtensionAccessTokenAsync(
            grant.UserGuid,
            grant.ParentSessionId,
            now
        );
        return token == null
            ? InvalidGrant()
            : ApiResponse<BrowserExtensionTokenResponse>.OK(token);
    }

    private async Task<bool> IsActiveParentSessionAsync(
        string userGuid,
        string parentSessionId,
        DateTime now
    ) => await _db.Queryable<RefreshToken>().AnyAsync(token =>
        token.RefreshTokenGUID == parentSessionId
        && token.UserGUID == userGuid
        && !token.IsRevoked
        && !token.IsDeleted
        && token.ExpiresAt >= now
    );

    private async Task CleanupExpiredGrantsAsync(DateTime now)
    {
        var cutoff = now - CleanupRetention;
        var staleGrantIds = await _db.Queryable<BrowserExtensionSessionGrantEntity>()
            .Where(item => item.ExpiresAtUtc < cutoff)
            .OrderBy(item => item.ExpiresAtUtc)
            .Select(item => item.GrantId)
            .Take(CleanupBatchSize)
            .ToListAsync();
        if (staleGrantIds.Count == 0)
        {
            return;
        }

        // 每次只删除固定批次，避免一次授权触发大事务；24 小时内记录保留用于故障审计。
        await _db.Deleteable<BrowserExtensionSessionGrantEntity>()
            .Where(item => staleGrantIds.Contains(item.GrantId))
            .ExecuteCommandAsync();
    }

    private static ApiResponse<BrowserExtensionTokenResponse> InvalidGrant() =>
        ApiResponse<BrowserExtensionTokenResponse>.Error(
            "扩展授权无效、已过期或已使用",
            InvalidGrantErrorCode
        );

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;

    private static string HashCode(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(code))).ToLowerInvariant();

    private static string CreateCodeChallenge(string verifier) =>
        Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    private static bool FixedTimeEquals(string expected, string actual)
    {
        var expectedBytes = Encoding.ASCII.GetBytes(expected);
        var actualBytes = Encoding.ASCII.GetBytes(actual);
        return expectedBytes.Length == actualBytes.Length
            && CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes)
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');

    [GeneratedRegex("^[A-Za-z0-9_-]{43}$", RegexOptions.CultureInvariant)]
    private static partial Regex CodeChallengeRegex();

    [GeneratedRegex("^[A-Za-z0-9._~-]{43,128}$", RegexOptions.CultureInvariant)]
    private static partial Regex CodeVerifierRegex();

    [GeneratedRegex("^[A-Za-z0-9_-]{43}$", RegexOptions.CultureInvariant)]
    private static partial Regex AuthorizationCodeRegex();

    [GeneratedRegex("^[A-Za-z0-9_-]{22,128}$", RegexOptions.CultureInvariant)]
    private static partial Regex StateRegex();
}

[SugarTable("BrowserExtensionSessionGrant")]
internal sealed class BrowserExtensionSessionGrantEntity
{
    [SugarColumn(IsPrimaryKey = true, IsNullable = false)]
    public Guid GrantId { get; set; }

    [SugarColumn(IsNullable = false, Length = 64)]
    public string CodeHash { get; set; } = string.Empty;

    [SugarColumn(IsNullable = false, Length = 43)]
    public string CodeChallenge { get; set; } = string.Empty;

    [SugarColumn(IsNullable = false, Length = 128)]
    public string State { get; set; } = string.Empty;

    [SugarColumn(IsNullable = false, Length = 100)]
    public string ParentSessionId { get; set; } = string.Empty;

    [SugarColumn(IsNullable = false, Length = 100)]
    public string UserGuid { get; set; } = string.Empty;

    [SugarColumn(IsNullable = false, Length = 64)]
    public string ClientId { get; set; } = string.Empty;

    [SugarColumn(IsNullable = false)]
    public DateTime IssuedAtUtc { get; set; }

    [SugarColumn(IsNullable = false)]
    public DateTime ExpiresAtUtc { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? ConsumedAtUtc { get; set; }
}
