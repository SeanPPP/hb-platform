using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hbpos.Contracts.AppUpdates;
using Microsoft.Extensions.Options;

namespace Hbpos.Api.Services;

public sealed record PosHandheldNativeUpdateDecisionRequest(
    string StoreCode,
    string Platform,
    string? Version,
    string? Build);

public sealed record PosHandheldOtaUpdateDecisionRequest(
    string StoreCode,
    string Platform,
    string? RuntimeVersion,
    string? CurrentUpdateId,
    string? CurrentUpdateGroupId);

public interface IPosHandheldUpdateDecisionGateway
{
    Task<PosHandheldNativeUpdateResponse?> GetNativeDecisionAsync(
        PosHandheldNativeUpdateDecisionRequest request,
        CancellationToken cancellationToken);

    Task<PosHandheldOtaUpdateResponse?> GetOtaDecisionAsync(
        PosHandheldOtaUpdateDecisionRequest request,
        CancellationToken cancellationToken);
}

public sealed partial class HttpPosHandheldUpdateDecisionGateway(
    HttpClient httpClient,
    IOptions<AppUpdateOptions> options,
    ILogger<HttpPosHandheldUpdateDecisionGateway> logger)
    : IPosHandheldUpdateDecisionGateway
{
    private const string PosHandheldIosBundleIdentifier = "com.hbweb.poshandheld";
    private const string PosHandheldProductionChannel = "pos-handheld-production";
    private const long JavaScriptSafeIntegerMax = 9007199254740991;

    internal const string DecisionReadTokenEnvironmentVariable =
        "HBPOS_APP_UPDATE_DECISION_READ_TOKEN";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> NativeFields = new(
        [
            "state",
            "policyVersion",
            "platform",
            "required",
            "latestVersion",
            "latestBuild",
            "minimumSupportedVersion",
            "distribution",
            "downloadUrl",
            "fileSize",
            "sha256",
            "packageName",
            "signingCertificateSha256",
            "bundleIdentifier",
            "appStoreId",
            "releaseMessage",
        ],
        StringComparer.Ordinal
    );
    private static readonly HashSet<string> OtaFields = new(
        [
            "state",
            "policyVersion",
            "appKey",
            "projectName",
            "platform",
            "required",
            "channel",
            "runtimeVersion",
            "updateId",
            "updateGroupId",
            "releaseMessage",
        ],
        StringComparer.Ordinal
    );

    public Task<PosHandheldNativeUpdateResponse?> GetNativeDecisionAsync(
        PosHandheldNativeUpdateDecisionRequest request,
        CancellationToken cancellationToken) =>
        PostDecisionAsync<PosHandheldNativeUpdateResponse>(
            "api/internal/app-update-decisions/pos-handheld/native",
            request,
            NativeFields,
            decision => IsValidNativeDecision(decision, request),
            cancellationToken
        );

    public Task<PosHandheldOtaUpdateResponse?> GetOtaDecisionAsync(
        PosHandheldOtaUpdateDecisionRequest request,
        CancellationToken cancellationToken) =>
        PostDecisionAsync<PosHandheldOtaUpdateResponse>(
            "api/internal/app-update-decisions/pos-handheld/ota",
            request,
            OtaFields,
            decision => IsValidOtaDecision(decision, request),
            cancellationToken
        );

    private async Task<TDecision?> PostDecisionAsync<TDecision>(
        string path,
        object body,
        IReadOnlySet<string> fields,
        Func<TDecision, bool> validate,
        CancellationToken cancellationToken)
        where TDecision : class
    {
        var baseUrl = ResolveCenterBaseUrl(options.Value.CenterBaseUrl);
        var token = ResolveServiceToken(options.Value);
        if (baseUrl is null || token is null)
        {
            return null;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUrl, path))
            {
                Content = JsonContent.Create(body, options: JsonOptions),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken
            );
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "pos-handheld update decision failed path={Path} status={StatusCode}",
                    path,
                    (int)response.StatusCode
                );
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(
                stream,
                cancellationToken: cancellationToken
            );
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("success", out var success)
                || success.ValueKind != JsonValueKind.True
                || !root.TryGetProperty("data", out var data)
                || !HasExactProperties(data, fields))
            {
                return null;
            }

            var decision = data.Deserialize<TDecision>(JsonOptions);
            return decision is not null && validate(decision) ? decision : null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("pos-handheld update decision timed out path={Path}", path);
            return null;
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "pos-handheld update decision transport failed path={Path}", path);
            return null;
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "pos-handheld update decision JSON invalid path={Path}", path);
            return null;
        }
    }

    private static bool IsValidNativeDecision(
        PosHandheldNativeUpdateResponse decision,
        PosHandheldNativeUpdateDecisionRequest request)
    {
        if (!IsPlatform(decision.Platform)
            || !string.Equals(decision.Platform, request.Platform, StringComparison.Ordinal)
            || !IsState(decision.State)
            || decision.Required != string.Equals(decision.State, "required", StringComparison.Ordinal))
        {
            return false;
        }

        if (decision.State == "none")
        {
            return decision.PolicyVersion == "none" && !decision.Required;
        }

        if (string.IsNullOrWhiteSpace(decision.PolicyVersion)
            || decision.PolicyVersion == "none"
            || !VersionPattern().IsMatch(decision.LatestVersion ?? string.Empty)
            || !IsPositiveBuild(decision.LatestBuild)
            || !IsTrustedHttpsUrl(decision.DownloadUrl))
        {
            return false;
        }

        return decision.Platform == "Android"
            ? decision.Distribution == "apk"
                && decision.FileSize is > 0
                && Sha256Pattern().IsMatch(decision.Sha256 ?? string.Empty)
                && PackagePattern().IsMatch(decision.PackageName ?? string.Empty)
                && Sha256Pattern().IsMatch(decision.SigningCertificateSha256 ?? string.Empty)
                && decision.BundleIdentifier is null
                && decision.AppStoreId is null
            : decision.Distribution is "app-store" or "testflight"
                && string.Equals(
                    decision.BundleIdentifier,
                    PosHandheldIosBundleIdentifier,
                    StringComparison.Ordinal
                )
                && IsValidAppStoreId(decision.AppStoreId)
                && !(decision.Required && decision.Distribution == "testflight")
                && IsTrustedIosUrl(
                    decision.DownloadUrl,
                    decision.Distribution,
                    decision.AppStoreId!
                )
                && decision.FileSize is null
                && decision.Sha256 is null
                && decision.PackageName is null
                && decision.SigningCertificateSha256 is null;
    }

    private static bool IsValidOtaDecision(
        PosHandheldOtaUpdateResponse decision,
        PosHandheldOtaUpdateDecisionRequest request)
    {
        if (!IsPlatform(decision.Platform)
            || !string.Equals(decision.Platform, request.Platform, StringComparison.Ordinal)
            || !IsState(decision.State)
            || decision.AppKey != "pos-handheld"
            || decision.Required != string.Equals(decision.State, "required", StringComparison.Ordinal))
        {
            return false;
        }

        return decision.State == "none"
            ? decision.PolicyVersion == "none" && !decision.Required
            : decision.PolicyVersion != "none"
                && !string.IsNullOrWhiteSpace(decision.ProjectName)
                && IsTrustedOtaChannel(decision.Channel, request.Platform)
                && !string.IsNullOrWhiteSpace(decision.RuntimeVersion)
                && string.Equals(
                    decision.RuntimeVersion.Trim(),
                    request.RuntimeVersion?.Trim(),
                    StringComparison.Ordinal
                )
                && !string.IsNullOrWhiteSpace(decision.UpdateId)
                && Guid.TryParse(decision.UpdateGroupId, out _);
    }

    private static bool IsTrustedOtaChannel(string? channel, string platform)
    {
        if (string.Equals(
                channel,
                PosHandheldProductionChannel,
                StringComparison.Ordinal
            ))
        {
            return true;
        }

        var platformSegment = platform switch
        {
            "iOS" => "ios",
            "Android" => "android",
            _ => null,
        };
        if (platformSegment is null || channel is null)
        {
            return false;
        }

        var prefix = $"{PosHandheldProductionChannel}-{platformSegment}-release-";
        return channel.StartsWith(prefix, StringComparison.Ordinal)
            && ReleaseChannelSuffixPattern().IsMatch(channel[prefix.Length..]);
    }

    private static bool HasExactProperties(JsonElement value, IReadOnlySet<string> fields)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var count = 0;
        foreach (var property in value.EnumerateObject())
        {
            count++;
            if (!fields.Contains(property.Name))
            {
                return false;
            }
        }

        return count == fields.Count;
    }

    private static bool IsState(string value) =>
        value is "none" or "optional" or "required";

    private static bool IsPlatform(string value) => value is "iOS" or "Android";

    private static bool IsPositiveBuild(string? value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        // 网关必须拒绝无法由 JavaScript 精确表示的 build，避免错误信任更新响应。
        return BuildPattern().IsMatch(normalized)
            && long.TryParse(normalized, out var build)
            && build <= JavaScriptSafeIntegerMax;
    }

    private static bool IsTrustedHttpsUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && string.IsNullOrEmpty(uri.UserInfo);

    private static bool IsTrustedIosUrl(
        string? value,
        string distribution,
        string appStoreId)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !uri.IsDefaultPort
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        var pathSegments = uri.AbsolutePath.Split(
            '/',
            StringSplitOptions.RemoveEmptyEntries
        );
        if (distribution == "app-store")
        {
            return IsAppStoreHost(uri.Host)
                && pathSegments.Length > 0
                && string.Equals(
                    pathSegments[^1],
                    $"id{appStoreId}",
                    StringComparison.Ordinal
                );
        }

        if (distribution != "testflight"
            || !string.Equals(
                uri.Host,
                "testflight.apple.com",
                StringComparison.OrdinalIgnoreCase
            )
            || !string.IsNullOrEmpty(uri.Query)
            || pathSegments.Length != 2
            || !string.Equals(pathSegments[0], "join", StringComparison.Ordinal))
        {
            return false;
        }

        var joinCode = pathSegments[1];
        return joinCode.Length is >= 4 and <= 64
            && joinCode.All(char.IsAsciiLetterOrDigit)
            && string.Equals(
                uri.AbsolutePath,
                $"/join/{joinCode}",
                StringComparison.Ordinal
            );
    }

    private static bool IsValidAppStoreId(string? value) =>
        value is { Length: >= 5 and <= 20 }
        && value.All(character => character is >= '0' and <= '9');

    private static bool IsAppStoreHost(string host) =>
        string.Equals(host, "apps.apple.com", StringComparison.OrdinalIgnoreCase)
        || string.Equals(host, "itunes.apple.com", StringComparison.OrdinalIgnoreCase);

    private static Uri? ResolveCenterBaseUrl(string? value)
    {
        var candidate = value?.Trim();
        if (string.IsNullOrWhiteSpace(candidate)
            || !Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || uri.Scheme != Uri.UriSchemeHttps
                && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
        {
            return null;
        }

        return new Uri(
            uri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
                ? uri.AbsoluteUri
                : $"{uri.AbsoluteUri}/"
        );
    }

    internal static string? ResolveServiceToken(
        AppUpdateOptions configuration,
        Func<string, string?>? readEnvironment = null)
    {
        var configured = configuration.ServiceApiToken?.Trim();
        if (configured?.StartsWith("hbsvc_", StringComparison.Ordinal) == true)
        {
            return configured;
        }

        var environment = (readEnvironment ?? Environment.GetEnvironmentVariable)(
            DecisionReadTokenEnvironmentVariable
        )?.Trim();
        return environment?.StartsWith("hbsvc_", StringComparison.Ordinal) == true
            ? environment
            : null;
    }

    [GeneratedRegex("^v?\\d+(?:\\.\\d+){0,3}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();

    [GeneratedRegex("^[1-9]\\d{0,15}$", RegexOptions.CultureInvariant)]
    private static partial Regex BuildPattern();

    [GeneratedRegex("^[a-f0-9]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();

    [GeneratedRegex("^[a-zA-Z][a-zA-Z0-9_]*(?:\\.[a-zA-Z][a-zA-Z0-9_]*)+$", RegexOptions.CultureInvariant)]
    private static partial Regex PackagePattern();

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex ReleaseChannelSuffixPattern();

}
