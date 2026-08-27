using System.Buffers;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BlazorApp.Api.Services.Performance;

public sealed record SentryReleaseHealthSnapshot(
    string Project,
    string Environment,
    string Release,
    string Dist,
    long SessionCount,
    double CrashFreeSessionRatio,
    DateTime ObservedAtUtc
);

public sealed record SentryReleaseHealthWindowResult(
    bool Complete,
    IReadOnlyList<SentryReleaseHealthSnapshot> Snapshots
);

public sealed class SentryReleaseHealthClient
{
    private const string SessionCountField = "sum(session)";
    private const string CrashFreeSessionField = "crash_free_rate(session)";
    private const int MinimumResponseBodyBytes = 1024;
    private const int MaximumResponseBodyBytes = 1024 * 1024;
    private const int MaximumPagesPerProject = 10;
    private const int MaximumGroupsPerProject = 1000;

    private readonly HttpClient _httpClient;
    private readonly SentryReleaseHealthOptions _options;
    private readonly ILogger<SentryReleaseHealthClient> _logger;
    private readonly int _maxResponseBodyBytes;

    public SentryReleaseHealthClient(
        HttpClient httpClient,
        IOptions<SentryReleaseHealthOptions> options,
        ILogger<SentryReleaseHealthClient> logger
    )
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _maxResponseBodyBytes = Math.Clamp(
            _options.MaxResponseBodyBytes,
            MinimumResponseBodyBytes,
            MaximumResponseBodyBytes
        );

        // typed HttpClient 应为本客户端独占；在首次请求前设置硬超时，避免后台任务无限等待。
        _httpClient.Timeout = TimeSpan.FromSeconds(
            Math.Clamp(_options.HttpTimeoutSeconds, 2, 60)
        );
    }

    public bool IsConfigured => TryGetConfiguration(out _, out _, out _, out _);

    public async Task<IReadOnlyList<SentryReleaseHealthSnapshot>> FetchAsync(
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default
    )
    {
        var endUtc = utcNow.ToUniversalTime();
        var lookbackHours = Math.Clamp(_options.LookbackHours, 1, 168);
        var startUtc = endUtc.AddHours(-lookbackHours);
        var result = await FetchWindowAsync(startUtc, endUtc, cancellationToken);
        return result.Complete ? result.Snapshots : [];
    }

    public async Task<SentryReleaseHealthWindowResult> FetchWindowAsync(
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        CancellationToken cancellationToken = default,
        Func<CancellationToken, Task<bool>>? heartbeat = null
    )
    {
        if (!_options.Enabled)
        {
            return new SentryReleaseHealthWindowResult(false, []);
        }

        if (!TryGetConfiguration(out var baseUri, out var organization, out var token, out var environment))
        {
            _logger.LogWarning("Sentry Release Health 配置不完整或不安全，本次同步已禁用");
            return new SentryReleaseHealthWindowResult(false, []);
        }

        startUtc = startUtc.ToUniversalTime();
        endUtc = endUtc.ToUniversalTime();
        if (startUtc >= endUtc)
        {
            _logger.LogWarning("Sentry Release Health 查询窗口无效");
            return new SentryReleaseHealthWindowResult(false, []);
        }
        var snapshots = new List<SentryReleaseHealthSnapshot>();
        var complete = true;

        foreach (var project in SentryReleaseHealthOptions.ProjectWhitelist)
        {
            var projectResult = await FetchProjectAsync(
                baseUri,
                organization,
                token,
                environment,
                project,
                startUtc,
                endUtc,
                heartbeat,
                cancellationToken
            );
            complete &= projectResult.Complete;
            snapshots.AddRange(projectResult.Snapshots);
        }

        return new SentryReleaseHealthWindowResult(complete, snapshots);
    }

    private async Task<SentryProjectFetchResult> FetchProjectAsync(
        Uri baseUri,
        string organization,
        string token,
        string environment,
        string project,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        Func<CancellationToken, Task<bool>>? heartbeat,
        CancellationToken cancellationToken
    )
    {
        var requestUri = BuildRequestUri(
            baseUri,
            organization,
            environment,
            project,
            startUtc,
            endUtc
        );
        Uri? nextPageUri = requestUri;
        var pageCount = 0;
        var groupCount = 0;
        var snapshots = new List<SentryReleaseHealthSnapshot>();

        try
        {
            while (nextPageUri is not null)
            {
                if (++pageCount > MaximumPagesPerProject)
                {
                    _logger.LogWarning(
                        "Sentry Release Health 分页超过安全上限，项目 {Project}",
                        project
                    );
                    return new SentryProjectFetchResult(false, []);
                }

                var currentPageUri = nextPageUri;
                using var request = new HttpRequestMessage(HttpMethod.Get, currentPageUri);
                // 中文注释：认证信息只放在请求头，永不接受或拼接到分页 URL 中。
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                using var response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken
                );
                if (!response.IsSuccessStatusCode)
                {
                    // 不读取或记录错误响应体，避免上游回显凭据或其他敏感数据。
                    _logger.LogWarning(
                        "Sentry Release Health 请求失败，项目 {Project}，HTTP {StatusCode}",
                        project,
                        (int)response.StatusCode
                    );
                    return new SentryProjectFetchResult(false, []);
                }
                if (WasRedirected(response, currentPageUri))
                {
                    _logger.LogWarning(
                        "Sentry Release Health 分页发生重定向，项目 {Project}",
                        project
                    );
                    return new SentryProjectFetchResult(false, []);
                }

                var body = await ReadBoundedBodyAsync(response.Content, cancellationToken);
                if (body == null)
                {
                    _logger.LogWarning(
                        "Sentry Release Health 响应超过大小上限，项目 {Project}",
                        project
                    );
                    return new SentryProjectFetchResult(false, []);
                }

                var parsed = ParseResponse(
                    body,
                    project,
                    environment,
                    startUtc,
                    endUtc
                );
                if (!parsed.Complete)
                {
                    _logger.LogWarning(
                        "Sentry Release Health 响应不完整，项目 {Project}",
                        project
                    );
                    return new SentryProjectFetchResult(false, []);
                }
                if (parsed.GroupCount > MaximumGroupsPerProject - groupCount)
                {
                    _logger.LogWarning(
                        "Sentry Release Health groups 超过安全上限，项目 {Project}",
                        project
                    );
                    return new SentryProjectFetchResult(false, []);
                }

                groupCount += parsed.GroupCount;
                snapshots.AddRange(parsed.Snapshots);

                var nextPage = TryGetNextPage(
                    response,
                    currentPageUri,
                    requestUri.AbsolutePath,
                    baseUri,
                    token
                );
                if (nextPage.Status == SentryNextPageStatus.Invalid)
                {
                    _logger.LogWarning(
                        "Sentry Release Health next 分页链接无效，项目 {Project}",
                        project
                    );
                    return new SentryProjectFetchResult(false, []);
                }

                nextPageUri = nextPage.Uri;
                if (heartbeat != null && !await heartbeat(cancellationToken))
                {
                    _logger.LogWarning(
                        "Sentry Release Health 分页期间采集租约已失效，项目 {Project}",
                        project
                    );
                    return new SentryProjectFetchResult(false, []);
                }
            }

            if (!TryMergeSnapshots(snapshots, out var mergedSnapshots))
            {
                _logger.LogWarning(
                    "Sentry Release Health 重复 selector 无法安全合并，项目 {Project}",
                    project
                );
                return new SentryProjectFetchResult(false, []);
            }
            if (mergedSnapshots.Count == 0)
            {
                _logger.LogWarning(
                    "Sentry Release Health 响应缺少可用 session 数据，项目 {Project}",
                    project
                );
            }
            return new SentryProjectFetchResult(true, mergedSnapshots);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Sentry Release Health 请求超时，项目 {Project}", project);
            return new SentryProjectFetchResult(false, []);
        }
        catch (HttpRequestException ex)
        {
            LogSafeFailure(project, ex);
            return new SentryProjectFetchResult(false, []);
        }
        catch (IOException ex)
        {
            LogSafeFailure(project, ex);
            return new SentryProjectFetchResult(false, []);
        }
        catch (JsonException ex)
        {
            LogSafeFailure(project, ex);
            return new SentryProjectFetchResult(false, []);
        }
        catch (UriFormatException ex)
        {
            LogSafeFailure(project, ex);
            return new SentryProjectFetchResult(false, []);
        }
    }

    private sealed record SentryProjectFetchResult(
        bool Complete,
        IReadOnlyList<SentryReleaseHealthSnapshot> Snapshots
    );

    private sealed record SentryResponseParseResult(
        bool Complete,
        int GroupCount,
        IReadOnlyList<SentryReleaseHealthSnapshot> Snapshots
    );

    private enum SentryNextPageStatus
    {
        None,
        Next,
        Invalid,
    }

    private sealed record SentryNextPageResult(SentryNextPageStatus Status, Uri? Uri);

    private sealed record SentrySnapshotSelector(
        string Project,
        string Environment,
        string Release,
        string Dist
    );

    private async Task<byte[]?> ReadBoundedBodyAsync(
        HttpContent content,
        CancellationToken cancellationToken
    )
    {
        if (content.Headers.ContentLength > _maxResponseBodyBytes)
        {
            return null;
        }

        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream(Math.Min(_maxResponseBodyBytes, 16 * 1024));
        var buffer = ArrayPool<byte>.Shared.Rent(Math.Min(16 * 1024, _maxResponseBodyBytes + 1));
        try
        {
            while (true)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (read == 0)
                {
                    return output.ToArray();
                }

                if (output.Length + read > _maxResponseBodyBytes)
                {
                    return null;
                }

                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static SentryResponseParseResult ParseResponse(
        ReadOnlyMemory<byte> body,
        string project,
        string configuredEnvironment,
        DateTimeOffset requestedStartUtc,
        DateTimeOffset requestedEndUtc
    )
    {
        using var document = JsonDocument.Parse(
            body,
            new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 16,
            }
        );
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Sentry Release Health 根节点不是对象");
        }

        if (!TryReadUtcTimestamp(root, "start", out var responseStartUtc))
        {
            throw new JsonException("Sentry Release Health 缺少有效 UTC start");
        }
        if (!TryReadUtcTimestamp(root, "end", out var observedAt))
        {
            throw new JsonException("Sentry Release Health 缺少有效 UTC end");
        }
        if (
            responseStartUtc != requestedStartUtc.ToUniversalTime()
            || observedAt != requestedEndUtc.ToUniversalTime()
        )
        {
            throw new JsonException("Sentry Release Health 响应窗口与请求不一致");
        }

        if (
            !root.TryGetProperty("groups", out var groups)
            || groups.ValueKind != JsonValueKind.Array
        )
        {
            throw new JsonException("Sentry Release Health 缺少 groups");
        }

        var snapshots = new List<SentryReleaseHealthSnapshot>();
        var complete = true;
        foreach (var group in groups.EnumerateArray())
        {
            if (
                group.ValueKind != JsonValueKind.Object
                || !group.TryGetProperty("by", out var by)
                || by.ValueKind != JsonValueKind.Object
                || !group.TryGetProperty("totals", out var totals)
                || totals.ValueKind != JsonValueKind.Object
                || !TryReadDimension(by, "release", out var release)
                || !TryReadNonNegativeInt64(totals, SessionCountField, out var sessionCount)
            )
            {
                complete = false;
                continue;
            }

            if (
                TryReadDimension(by, "environment", out var responseEnvironment)
                && !string.Equals(
                    responseEnvironment,
                    configuredEnvironment,
                    StringComparison.Ordinal
                )
            )
            {
                continue;
            }

            if (sessionCount == 0)
            {
                continue;
            }
            if (
                !TryReadFiniteNumber(totals, CrashFreeSessionField, out var crashFreePercent)
                || crashFreePercent is < 0 or > 100
            )
            {
                complete = false;
                continue;
            }

            var dist = TryReadDimension(by, "dist", out var responseDist)
                ? responseDist
                : "all";
            snapshots.Add(
                new SentryReleaseHealthSnapshot(
                    project,
                    configuredEnvironment,
                    release,
                    dist,
                    sessionCount,
                    crashFreePercent / 100d,
                    observedAt.UtcDateTime
                )
            );
        }

        return new SentryResponseParseResult(complete, groups.GetArrayLength(), snapshots);
    }

    private static bool WasRedirected(HttpResponseMessage response, Uri requestedUri) =>
        response.RequestMessage?.RequestUri is Uri actualUri
        && Uri.Compare(
            actualUri,
            requestedUri,
            UriComponents.AbsoluteUri,
            UriFormat.SafeUnescaped,
            StringComparison.Ordinal
        ) != 0;

    private static SentryNextPageResult TryGetNextPage(
        HttpResponseMessage response,
        Uri requestedUri,
        string expectedPath,
        Uri baseUri,
        string token
    )
    {
        if (!response.Headers.TryGetValues("Link", out var linkHeaders))
        {
            return new SentryNextPageResult(SentryNextPageStatus.None, null);
        }

        var foundNext = false;
        Uri? nextUri = null;
        foreach (var linkHeader in linkHeaders)
        {
            foreach (var linkValue in SplitLinkValues(linkHeader))
            {
                if (!TryParseLinkValue(linkValue, out var target, out var parameters))
                {
                    return new SentryNextPageResult(SentryNextPageStatus.Invalid, null);
                }
                if (
                    !parameters.TryGetValue("rel", out var relation)
                    || !relation.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                        .Contains("next", StringComparer.OrdinalIgnoreCase)
                )
                {
                    continue;
                }
                if (foundNext || !parameters.TryGetValue("results", out var hasResults))
                {
                    return new SentryNextPageResult(SentryNextPageStatus.Invalid, null);
                }

                foundNext = true;
                if (string.Equals(hasResults, "false", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (
                    !string.Equals(hasResults, "true", StringComparison.OrdinalIgnoreCase)
                    || !Uri.TryCreate(target, UriKind.Absolute, out var candidate)
                    || !IsSafeNextPageUri(candidate, requestedUri, expectedPath, baseUri, token)
                )
                {
                    return new SentryNextPageResult(SentryNextPageStatus.Invalid, null);
                }

                nextUri = candidate;
            }
        }

        return foundNext && nextUri is not null
            ? new SentryNextPageResult(SentryNextPageStatus.Next, nextUri)
            : new SentryNextPageResult(SentryNextPageStatus.None, null);
    }

    private static IEnumerable<string> SplitLinkValues(string value)
    {
        var start = 0;
        var inAngleBrackets = false;
        var inQuotes = false;
        for (var index = 0; index < value.Length; index++)
        {
            switch (value[index])
            {
                case '<' when !inQuotes:
                    inAngleBrackets = true;
                    break;
                case '>' when !inQuotes:
                    inAngleBrackets = false;
                    break;
                case '"' when !inAngleBrackets:
                    inQuotes = !inQuotes;
                    break;
                case ',' when !inAngleBrackets && !inQuotes:
                    yield return value[start..index];
                    start = index + 1;
                    break;
            }
        }

        if (inAngleBrackets || inQuotes)
        {
            yield return string.Empty;
            yield break;
        }

        yield return value[start..];
    }

    private static bool TryParseLinkValue(
        string value,
        out string target,
        out Dictionary<string, string> parameters
    )
    {
        target = string.Empty;
        parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var trimmed = value.Trim();
        if (!trimmed.StartsWith('<'))
        {
            return false;
        }

        var targetEnd = trimmed.IndexOf('>');
        if (targetEnd <= 1)
        {
            return false;
        }

        target = trimmed[1..targetEnd];
        foreach (
            var rawParameter in trimmed[(targetEnd + 1)..].Split(
                ';',
                StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries
            )
        )
        {
            var separator = rawParameter.IndexOf('=');
            if (separator <= 0)
            {
                return false;
            }

            var name = rawParameter[..separator].Trim();
            var rawValue = rawParameter[(separator + 1)..].Trim();
            if (string.IsNullOrWhiteSpace(name) || rawValue.Length == 0)
            {
                return false;
            }
            var startsWithQuote = rawValue.StartsWith('"');
            var endsWithQuote = rawValue.EndsWith('"');
            if (startsWithQuote != endsWithQuote)
            {
                return false;
            }
            if (startsWithQuote)
            {
                if (rawValue.Length < 2)
                {
                    return false;
                }
                rawValue = rawValue[1..^1];
            }
            if (!parameters.TryAdd(name, rawValue))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsSafeNextPageUri(
        Uri candidate,
        Uri requestedUri,
        string expectedPath,
        Uri baseUri,
        string token
    ) =>
        string.Equals(candidate.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
        && string.Equals(candidate.Scheme, baseUri.Scheme, StringComparison.OrdinalIgnoreCase)
        && string.Equals(candidate.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase)
        && candidate.Port == baseUri.Port
        && string.Equals(candidate.AbsolutePath, expectedPath, StringComparison.Ordinal)
        && string.IsNullOrEmpty(candidate.UserInfo)
        && string.IsNullOrEmpty(candidate.Fragment)
        && !Uri.Compare(
            candidate,
            requestedUri,
            UriComponents.AbsoluteUri,
            UriFormat.SafeUnescaped,
            StringComparison.Ordinal
        ).Equals(0)
        && !ContainsSensitiveQueryData(candidate, token);

    private static bool ContainsSensitiveQueryData(Uri uri, string token)
    {
        foreach (
            var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
        )
        {
            var separator = pair.IndexOf('=');
            var name = Uri.UnescapeDataString(separator < 0 ? pair : pair[..separator]);
            var value = Uri.UnescapeDataString(
                separator < 0 ? string.Empty : pair[(separator + 1)..]
            );
            if (
                name.Equals("token", StringComparison.OrdinalIgnoreCase)
                || name.Equals("auth", StringComparison.OrdinalIgnoreCase)
                || name.Equals("authorization", StringComparison.OrdinalIgnoreCase)
                || name.Equals("api_key", StringComparison.OrdinalIgnoreCase)
                || name.Equals("apikey", StringComparison.OrdinalIgnoreCase)
                || name.Equals("bearer", StringComparison.OrdinalIgnoreCase)
                || value.Contains(token, StringComparison.Ordinal)
            )
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryMergeSnapshots(
        IReadOnlyList<SentryReleaseHealthSnapshot> snapshots,
        out IReadOnlyList<SentryReleaseHealthSnapshot> mergedSnapshots
    )
    {
        var indexBySelector = new Dictionary<SentrySnapshotSelector, int>();
        var merged = new List<SentryReleaseHealthSnapshot>();
        try
        {
            foreach (var snapshot in snapshots)
            {
                if (
                    snapshot.SessionCount <= 0
                    || !double.IsFinite(snapshot.CrashFreeSessionRatio)
                    || snapshot.CrashFreeSessionRatio is < 0 or > 1
                )
                {
                    mergedSnapshots = [];
                    return false;
                }

                var selector = new SentrySnapshotSelector(
                    snapshot.Project,
                    snapshot.Environment,
                    snapshot.Release,
                    snapshot.Dist
                );
                if (!indexBySelector.TryGetValue(selector, out var index))
                {
                    indexBySelector.Add(selector, merged.Count);
                    merged.Add(snapshot);
                    continue;
                }

                var previous = merged[index];
                var sessionCount = checked(previous.SessionCount + snapshot.SessionCount);
                var weightedRatio =
                    (previous.CrashFreeSessionRatio * previous.SessionCount)
                    + (snapshot.CrashFreeSessionRatio * snapshot.SessionCount);
                if (!double.IsFinite(weightedRatio))
                {
                    mergedSnapshots = [];
                    return false;
                }

                merged[index] = previous with
                {
                    SessionCount = sessionCount,
                    CrashFreeSessionRatio = weightedRatio / sessionCount,
                };
            }
        }
        catch (OverflowException)
        {
            mergedSnapshots = [];
            return false;
        }

        mergedSnapshots = merged;
        return true;
    }

    private static bool TryReadUtcTimestamp(
        JsonElement container,
        string propertyName,
        out DateTimeOffset value
    )
    {
        value = default;
        return container.TryGetProperty(propertyName, out var element)
            && element.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(
                element.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces
                    | DateTimeStyles.AssumeUniversal
                    | DateTimeStyles.AdjustToUniversal,
                out value
            );
    }

    private static bool TryReadFiniteNumber(
        JsonElement container,
        string propertyName,
        out double value
    )
    {
        value = 0;
        return container.TryGetProperty(propertyName, out var element)
            && element.ValueKind == JsonValueKind.Number
            && element.TryGetDouble(out value)
            && double.IsFinite(value);
    }

    private static bool TryReadNonNegativeInt64(
        JsonElement container,
        string propertyName,
        out long value
    )
    {
        value = 0;
        return container.TryGetProperty(propertyName, out var element)
            && element.ValueKind == JsonValueKind.Number
            && element.TryGetInt64(out value)
            && value >= 0;
    }

    private static bool TryReadDimension(
        JsonElement container,
        string propertyName,
        out string value
    )
    {
        value = string.Empty;
        if (
            !container.TryGetProperty(propertyName, out var element)
            || element.ValueKind != JsonValueKind.String
        )
        {
            return false;
        }

        var candidate = element.GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(candidate) || candidate.Any(char.IsControl))
        {
            return false;
        }

        value = candidate.Length <= 120 ? candidate : candidate[..120];
        return true;
    }

    private static Uri BuildRequestUri(
        Uri baseUri,
        string organization,
        string environment,
        string project,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc
    )
    {
        var query = new (string Key, string Value)[]
        {
            ("field", SessionCountField),
            ("field", CrashFreeSessionField),
            ("start", startUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)),
            ("end", endUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)),
            ("environment", environment),
            ("project", project),
            ("interval", "1h"),
            ("groupBy", "release"),
            ("groupBy", "environment"),
            ("includeTotals", "1"),
            ("includeSeries", "0"),
            ("per_page", "100"),
        };
        var queryString = string.Join(
            "&",
            query.Select(pair =>
                $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"
            )
        );
        var relative =
            $"api/0/organizations/{Uri.EscapeDataString(organization)}/sessions/?{queryString}";
        return new Uri(baseUri, relative);
    }

    private bool TryGetConfiguration(
        out Uri baseUri,
        out string organization,
        out string token,
        out string environment
    )
    {
        baseUri = null!;
        organization = _options.OrganizationSlug?.Trim() ?? string.Empty;
        token = _options.ReadOnlyAuthToken?.Trim() ?? string.Empty;
        environment = _options.Environment?.Trim() ?? string.Empty;

        if (
            !_options.Enabled
            || !IsSafeConfigurationValue(organization, 80)
            || !IsSafeToken(token)
            || !IsSafeConfigurationValue(environment, 120)
            || !Uri.TryCreate(_options.BaseUrl?.Trim(), UriKind.Absolute, out var configuredBaseUri)
            || !string.Equals(
                configuredBaseUri.Scheme,
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase
            )
            || !string.IsNullOrEmpty(configuredBaseUri.UserInfo)
            || !string.IsNullOrEmpty(configuredBaseUri.Query)
            || !string.IsNullOrEmpty(configuredBaseUri.Fragment)
        )
        {
            return false;
        }

        var normalized = configuredBaseUri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? configuredBaseUri.AbsoluteUri
            : configuredBaseUri.AbsoluteUri + "/";
        baseUri = new Uri(normalized, UriKind.Absolute);
        return true;
    }

    private static bool IsSafeConfigurationValue(string value, int maxLength) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= maxLength
        && !value.Any(char.IsControl);

    private static bool IsSafeToken(string token) =>
        !string.IsNullOrWhiteSpace(token)
        && token.Length <= 4096
        && !token.Any(character => char.IsWhiteSpace(character) || char.IsControl(character));

    private void LogSafeFailure(string project, Exception exception)
    {
        // 只记录异常类型，不记录异常消息、请求头或响应体，确保 token 不会被间接带入日志。
        _logger.LogWarning(
            "Sentry Release Health 请求或解析失败，项目 {Project}，类型 {ExceptionType}",
            project,
            exception.GetType().Name
        );
    }
}
