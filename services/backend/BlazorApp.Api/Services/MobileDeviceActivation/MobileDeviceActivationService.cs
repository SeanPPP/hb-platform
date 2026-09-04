using System.Security.Claims;
using System.Security.Cryptography;
using BlazorApp.Api.Data;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.POSM;
using BlazorApp.Shared.Security;
using Microsoft.AspNetCore.RateLimiting;
using SqlSugar;
using System.Threading.RateLimiting;

namespace BlazorApp.Api.Services.MobileDeviceActivation;

public sealed record MobileDeviceBindingContext(
    Guid BindingId,
    int BindingVersion,
    int DeviceRegistrationId,
    string HardwareId,
    string UserGuid);

public sealed record MobileDeviceBoundAccountValidationResult(
    bool IsValid,
    string? UserGuid = null,
    string? Username = null,
    IReadOnlyList<string>? Roles = null);

public sealed record MobileDeviceCredentialValidationResult(
    bool RequiresBoundCredential,
    bool IsValid)
{
    // 兼容既有调用点；语义已扩展为“存在任意绑定历史”，撤销后同样禁止回退内部授权码。
    public bool HasActiveBinding => RequiresBoundCredential;
}

public interface IMobileDeviceActivationService
{
    Task<ApiResponse<MobileDeviceActivationPreviewResponseDto>> PreviewAsync(
        MobileDeviceActivationPreviewRequestDto request,
        CancellationToken cancellationToken);

    Task<ApiResponse<MobileDeviceActivationMutationResponseDto>> RedeemAsync(
        MobileDeviceActivationRedeemRequestDto request,
        bool recoveryOnly,
        CancellationToken cancellationToken);

    Task<ApiResponse<MobileDeviceActivationMutationResponseDto>> RebindAsync(
        MobileDeviceActivationRebindRequestDto request,
        MobileDeviceBindingContext? currentBinding,
        bool recoveryOnly,
        CancellationToken cancellationToken);

    Task<ApiResponse<MobileDeviceSessionExchangeResponseDto>> ExchangeSessionAsync(
        MobileDeviceSessionExchangeRequestDto request,
        CancellationToken cancellationToken);

    Task<ApiResponse<MobileDeviceUnbindResponseDto>> UnbindAsync(
        MobileDeviceBindingContext currentBinding,
        MobileDeviceUnbindRequestDto request,
        string actor,
        CancellationToken cancellationToken);

    Task<MobileDeviceBoundAccountValidationResult> ValidateTokenBindingAsync(
        MobileDeviceBindingContext currentBinding,
        CancellationToken cancellationToken);

    Task<MobileDeviceCredentialValidationResult> ValidateBoundDeviceCredentialAsync(
        string hardwareId,
        string credential,
        CancellationToken cancellationToken = default);
}

public static class MobileDeviceActivationRateLimits
{
    public const string AnonymousMutationPolicy = "mobile-device-activation-anonymous";
    public const string SessionExchangePolicy = "mobile-device-session-exchange";
    public const int PermitLimit = 10;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    public static void Configure(RateLimiterOptions options)
    {
        options.AddPolicy(
            AnonymousMutationPolicy,
            context => RateLimitPartition.GetFixedWindowLimiter(
                ResolvePartitionKey(context),
                _ => CreateOptions()));
        options.AddPolicy(
            SessionExchangePolicy,
            context => RateLimitPartition.GetFixedWindowLimiter(
                ResolvePartitionKey(context),
                _ => CreateOptions()));
    }

    public static string ResolvePartitionKey(HttpContext context)
    {
        var resolver = context.RequestServices.GetService<IClientIpResolver>();
        var clientIp = resolver?.Resolve(context);
        if (string.IsNullOrWhiteSpace(clientIp))
        {
            clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        }
        return $"mobile-device-ip:{clientIp}";
    }

    private static FixedWindowRateLimiterOptions CreateOptions() => new()
    {
        AutoReplenishment = true,
        PermitLimit = PermitLimit,
        QueueLimit = 0,
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        Window = Window,
    };
}

public sealed class MobileDeviceActivationService : IMobileDeviceActivationService
{
    private const int DisabledStatus = 0;
    private const int EnabledStatus = 1;
    private const string CreatedBy = "MOBILE_ACTIVATION";
    private const string InvalidCredentialProbe = "invalid-mobile-device-credential";
    private static readonly byte[] MissingCredentialVerifier = new byte[SHA256.HashSizeInBytes];

    private const string AcquireApplicationLockSql = """
        DECLARE @Result int;
        EXEC @Result = sys.sp_getapplock
            @Resource = @Resource,
            @LockMode = N'Exclusive',
            @LockOwner = N'Transaction',
            @LockTimeout = 5000;
        SELECT @Result;
        """;

    private const string LockGrantSql = """
        SELECT TOP 1
            [GrantId], [SecretHash], [StoreCode], [DeviceSystem],
            [TargetUserGuid], [TargetUsernameSnapshot], [TargetFullNameSnapshot],
            [CreatedAtUtc], [CreatedBy], [Reason], [ExpiresAtUtc],
            [RevokedAtUtc], [RevokedBy], [RevokeReason],
            [ConsumedAtUtc], [ConsumedHardwareId], [ConsumedDeviceCode],
            [ConsumedDeviceRegistrationId], [ConsumedBindingId],
            [ConsumedDeviceSystem], [ConsumptionKind], [PreviousBindingId], [RowVersion]
        FROM [dbo].[POSM_MobileDeviceActivationGrant] WITH (UPDLOCK, HOLDLOCK)
        WHERE [GrantId] = @GrantId;
        """;

    private const string LockBindingSql = """
        SELECT TOP 1
            [BindingId], [DeviceRegistrationId], [HardwareId], [DeviceCode],
            [StoreCode], [DeviceSystem], [TargetUserGuid], [CredentialVerifier],
            [Version], [BoundAtUtc], [LastSessionExchangeAtUtc], [RevokedAtUtc],
            [RevokedBy], [RevokeReason], [ReplacedByBindingId], [RowVersion]
        FROM [dbo].[POSM_MobileDeviceAccountBinding] WITH (UPDLOCK, HOLDLOCK)
        WHERE [BindingId] = @BindingId;
        """;

    private const string LockHardwareBindingsSql = """
        SELECT
            [BindingId], [DeviceRegistrationId], [HardwareId], [DeviceCode],
            [StoreCode], [DeviceSystem], [TargetUserGuid], [CredentialVerifier],
            [Version], [BoundAtUtc], [LastSessionExchangeAtUtc], [RevokedAtUtc],
            [RevokedBy], [RevokeReason], [ReplacedByBindingId], [RowVersion]
        FROM [dbo].[POSM_MobileDeviceAccountBinding] WITH (UPDLOCK, HOLDLOCK)
        WHERE [HardwareId] = @HardwareId
        ORDER BY [BoundAtUtc] DESC;
        """;

    private const string LockHardwareRegistrationsSql = """
        SELECT
            [ID] AS [Id], [系统设备编号] AS [DeviceCode],
            [分店代码] AS [StoreCode], [设备硬件识别码] AS [HardwareId],
            [设备类型] AS [DeviceType], [设备状态] AS [DeviceStatus],
            [设备系统] AS [DeviceSystem]
        FROM [dbo].[POSM_设备注册信息表] WITH (UPDLOCK, HOLDLOCK)
        WHERE [设备硬件识别码] = @HardwareId
        ORDER BY [ID] DESC;
        """;

    private readonly ISqlSugarClient _posmDb;
    private readonly ISqlSugarClient _mainDb;
    private readonly IMobileDeviceAccountTokenIssuer _tokenIssuer;
    private readonly ILogger<MobileDeviceActivationService> _logger;
    private readonly TimeProvider _timeProvider;

    public MobileDeviceActivationService(
        POSMSqlSugarContext posmContext,
        SqlSugarContext mainContext,
        IMobileDeviceAccountTokenIssuer tokenIssuer,
        ILogger<MobileDeviceActivationService> logger,
        TimeProvider? timeProvider = null)
    {
        _posmDb = posmContext.Db;
        _mainDb = mainContext.Db;
        _tokenIssuer = tokenIssuer;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<ApiResponse<MobileDeviceActivationPreviewResponseDto>> PreviewAsync(
        MobileDeviceActivationPreviewRequestDto request,
        CancellationToken cancellationToken)
    {
        if (!DeviceActivationCodeCodec.TryParse(request.ActivationCode, out var parsed))
        {
            return PreviewDenied(MobileDeviceActivationReasonCodes.NotAvailable);
        }

        MobileDeviceActivationGrant? grant;
        MobileDeviceActivationGateDecision decision;
        try
        {
            grant = await _posmDb.Queryable<MobileDeviceActivationGrant>()
                .Where(item => item.GrantId == parsed.GrantId)
                .FirstAsync(cancellationToken);
            decision = MobileDeviceActivationRules.EvaluatePreview(
                grant,
                parsed.Secret,
                request.DeviceSystem,
                UtcNow());
        }
        finally
        {
            CryptographicOperations.ZeroMemory(parsed.Secret);
        }
        if (!decision.IsAllowed)
        {
            return PreviewDenied(decision.ReasonCode!);
        }

        var target = await LoadActiveTargetAsync(
            grant!.StoreCode,
            grant.TargetUserGuid,
            cancellationToken);
        if (target == null)
        {
            // 匿名预览统一收敛账号与分店失效，避免枚举目标账号状态。
            return PreviewDenied(MobileDeviceActivationReasonCodes.NotAvailable);
        }

        var assignedStoreCount = await CountAssignedStoresAsync(
            grant.TargetUserGuid,
            cancellationToken);
        return ApiResponse<MobileDeviceActivationPreviewResponseDto>.OK(
            new MobileDeviceActivationPreviewResponseDto(
                true,
                null,
                Redact(target.StoreCode),
                Redact(target.StoreName),
                decision.DeviceSystem,
                Redact(target.Username),
                RedactNullable(target.FullName),
                assignedStoreCount,
                DeviceActivationCodeCodec.NormalizeUtcForWire(grant.ExpiresAtUtc),
                "Mobile device activation code is ready."));
    }

    public async Task<ApiResponse<MobileDeviceActivationMutationResponseDto>> RedeemAsync(
        MobileDeviceActivationRedeemRequestDto request,
        bool recoveryOnly,
        CancellationToken cancellationToken)
    {
        var hardwareId = Normalize(request.HardwareId);
        var deviceName = Normalize(request.DeviceName) ?? string.Empty;
        if (hardwareId == null
            || hardwareId.Length > 100
            || deviceName.Length > 200
            || DeviceActivationCodeCodec.ContainsReservedActivationCode(hardwareId)
            || DeviceActivationCodeCodec.ContainsReservedActivationCode(deviceName)
            || !DeviceActivationCodeCodec.TryParse(request.ActivationCode, out var parsed))
        {
            return MutationDenied(MobileDeviceActivationReasonCodes.NotAvailable);
        }
        if (!MobileDeviceCredentialCodec.TryParseVerifier(
                request.CredentialVerifier,
                out var credentialVerifier))
        {
            CryptographicOperations.ZeroMemory(parsed.Secret);
            return MutationDenied(MobileDeviceActivationReasonCodes.NotAvailable);
        }

        try
        {
            var initialGrant = await _posmDb.Queryable<MobileDeviceActivationGrant>()
                .Where(item => item.GrantId == parsed.GrantId)
                .FirstAsync(cancellationToken);
            if (initialGrant == null
                || !DeviceActivationCodeCodec.Matches(initialGrant.SecretHash, parsed.Secret))
            {
                return MutationDenied(MobileDeviceActivationReasonCodes.NotAvailable);
            }

            ApiResponse<MobileDeviceActivationMutationResponseDto>? response = null;
            await ExecuteTransactionAsync(async () =>
            {
                await AcquireApplicationLockAsync($"HBMobile:ActivationGrant:{parsed.GrantId:N}");
                await AcquireApplicationLockAsync($"HBMobile:ActivationHardware:{hardwareId}");
                var grant = await LockGrantAsync(parsed.GrantId);
                if (grant == null
                    || !DeviceActivationCodeCodec.Matches(grant.SecretHash, parsed.Secret))
                {
                    response = MutationDenied(MobileDeviceActivationReasonCodes.NotAvailable);
                    return;
                }

                var operationNow = UtcNow();
                var decision = MobileDeviceActivationRules.EvaluateRedeem(
                    grant,
                    hardwareId,
                    request.DeviceSystem,
                    recoveryOnly,
                    operationNow);
                if (!decision.IsAllowed)
                {
                    response = MutationDenied(decision.ReasonCode!);
                    return;
                }

                if (decision.IsRecovery)
                {
                    response = await RecoverRedeemAsync(
                        grant,
                        credentialVerifier,
                        cancellationToken);
                    return;
                }

                var target = await LoadActiveTargetAsync(
                    grant.StoreCode,
                    grant.TargetUserGuid,
                    cancellationToken);
                if (target == null)
                {
                    response = MutationDenied(MobileDeviceActivationReasonCodes.NotAvailable);
                    return;
                }

                var bindings = await LockHardwareBindingsAsync(hardwareId);
                if (bindings.Any(binding => binding.RevokedAtUtc == null))
                {
                    response = MutationDenied(MobileDeviceActivationReasonCodes.DeviceConflict);
                    return;
                }

                var registrations = await LockHardwareRegistrationsAsync(hardwareId);
                if (registrations.Any(item =>
                        item.DeviceStatus == EnabledStatus
                        && !string.Equals(item.DeviceType, "Mobile", StringComparison.Ordinal)))
                {
                    response = MutationDenied(MobileDeviceActivationReasonCodes.DeviceConflict);
                    return;
                }

                var registration = registrations.FirstOrDefault(item =>
                    string.Equals(item.DeviceType, "Mobile", StringComparison.Ordinal));
                var bindingId = Guid.NewGuid();
                var deviceCode = await AllocateDeviceCodeAsync(grant.StoreCode);
                var registrationId = registration == null
                    ? await CreateMobileRegistrationAsync(
                        hardwareId,
                        deviceCode,
                        grant.StoreCode,
                        decision.DeviceSystem,
                        deviceName,
                        bindingId)
                    : await ReuseMobileRegistrationAsync(
                        registration,
                        deviceCode,
                        grant.StoreCode,
                        decision.DeviceSystem,
                        deviceName,
                        bindingId);

                await DisableOtherMobileRegistrationsAsync(
                    hardwareId,
                    registrationId,
                    grant.StoreCode);

                var binding = new MobileDeviceAccountBinding
                {
                    BindingId = bindingId,
                    DeviceRegistrationId = registrationId,
                    HardwareId = hardwareId,
                    DeviceCode = deviceCode,
                    StoreCode = grant.StoreCode,
                    DeviceSystem = decision.DeviceSystem,
                    TargetUserGuid = grant.TargetUserGuid,
                    CredentialVerifier = credentialVerifier.ToArray(),
                    Version = 1,
                    BoundAtUtc = operationNow,
                };
                await _posmDb.Insertable(binding).ExecuteCommandAsync();
                if (await ConsumeGrantAsync(
                        grant,
                        binding,
                        "Initial",
                        null,
                        operationNow) != 1)
                {
                    throw new InvalidOperationException(
                        "Mobile activation grant was not consumed atomically.");
                }

                response = ApiResponse<MobileDeviceActivationMutationResponseDto>.OK(
                    new MobileDeviceActivationMutationResponseDto(
                        true,
                        MobileDeviceActivationReasonCodes.Activated,
                        "Mobile device was bound to the target account.",
                        MapBinding(binding, target)));
            });

            if (response?.Data?.IsAllowed == true)
            {
                _logger.LogInformation(
                    "Mobile 设备已完成账号绑定，GrantId={GrantId}, BindingId={BindingId}, StoreCode={StoreCode}, TargetUserGuid={TargetUserGuid}",
                    parsed.GrantId,
                    response.Data.Binding?.BindingId,
                    response.Data.Binding?.StoreCode,
                    response.Data.Binding?.TargetUserGuid);
            }
            return response ?? MutationDenied(MobileDeviceActivationReasonCodes.NotAvailable);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(parsed.Secret);
            CryptographicOperations.ZeroMemory(credentialVerifier);
        }
    }

    public async Task<ApiResponse<MobileDeviceActivationMutationResponseDto>> RebindAsync(
        MobileDeviceActivationRebindRequestDto request,
        MobileDeviceBindingContext? currentBinding,
        bool recoveryOnly,
        CancellationToken cancellationToken)
    {
        var deviceName = Normalize(request.DeviceName) ?? string.Empty;
        if (deviceName.Length > 200
            || DeviceActivationCodeCodec.ContainsReservedActivationCode(deviceName)
            || !DeviceActivationCodeCodec.TryParse(request.ActivationCode, out var parsed))
        {
            return MutationDenied(MobileDeviceActivationReasonCodes.NotAvailable);
        }
        if (!MobileDeviceCredentialCodec.TryParseVerifier(
                request.CredentialVerifier,
                out var newVerifier))
        {
            CryptographicOperations.ZeroMemory(parsed.Secret);
            return MutationDenied(MobileDeviceActivationReasonCodes.NotAvailable);
        }

        try
        {
            var initialGrant = await _posmDb.Queryable<MobileDeviceActivationGrant>()
                .Where(item => item.GrantId == parsed.GrantId)
                .FirstAsync(cancellationToken);
            if (initialGrant == null
                || !DeviceActivationCodeCodec.Matches(initialGrant.SecretHash, parsed.Secret))
            {
                return MutationDenied(MobileDeviceActivationReasonCodes.NotAvailable);
            }

            if (recoveryOnly)
            {
                return await RecoverRebindAsync(
                    initialGrant,
                    request,
                    newVerifier,
                    parsed.Secret,
                    cancellationToken);
            }
            var credentialAuthorized = currentBinding == null;
            var sourceHardwareId = currentBinding?.HardwareId
                ?? Normalize(request.CurrentHardwareId);
            if (sourceHardwareId == null
                || sourceHardwareId.Length > 100
                || (credentialAuthorized
                    && !MobileDeviceCredentialCodec.IsCredentialShapeValid(
                        request.CurrentCredential)))
            {
                return MutationDenied(MobileDeviceActivationReasonCodes.NotAvailable);
            }

            ApiResponse<MobileDeviceActivationMutationResponseDto>? response = null;
            await ExecuteTransactionAsync(async () =>
            {
                await AcquireApplicationLockAsync($"HBMobile:ActivationGrant:{parsed.GrantId:N}");
                await AcquireApplicationLockAsync(
                    $"HBMobile:ActivationHardware:{sourceHardwareId}");
                var grant = await LockGrantAsync(parsed.GrantId);
                var operationNow = UtcNow();
                if (grant == null
                    || !DeviceActivationCodeCodec.Matches(grant.SecretHash, parsed.Secret)
                    || grant.RevokedAtUtc != null
                    || grant.ConsumedAtUtc != null
                    || grant.ExpiresAtUtc <= operationNow)
                {
                    response = MutationDenied(MobileDeviceActivationReasonCodes.NotAvailable);
                    return;
                }

                var bindings = await LockHardwareBindingsAsync(sourceHardwareId);
                var activeBindings = bindings
                    .Where(binding => binding.RevokedAtUtc == null)
                    .ToList();
                MobileDeviceAccountBinding? source;
                if (credentialAuthorized)
                {
                    var candidate = activeBindings.Count == 1 ? activeBindings[0] : null;
                    var credentialMatches = MobileDeviceCredentialCodec.MatchesCredential(
                        candidate?.CredentialVerifier ?? MissingCredentialVerifier,
                        request.CurrentCredential);
                    source = candidate != null && credentialMatches ? candidate : null;
                }
                else
                {
                    source = await LockBindingAsync(currentBinding!.BindingId);
                    if (source != null
                        && (!IsCurrentBinding(source, currentBinding)
                            || activeBindings.Count != 1
                            || activeBindings[0].BindingId != source.BindingId))
                    {
                        source = null;
                    }
                }

                if (source == null
                    || MobileDeviceCredentialCodec.MatchesVerifier(
                        source.CredentialVerifier,
                        newVerifier))
                {
                    response = MutationDenied(MobileDeviceActivationReasonCodes.NotAvailable);
                    return;
                }

                var registrationRecord = (await LockHardwareRegistrationsAsync(sourceHardwareId))
                    .FirstOrDefault(item => item.Id == source.DeviceRegistrationId);
                var registration = registrationRecord == null
                    ? null
                    : ToRegistrationState(registrationRecord);
                var sourceAuthorized = credentialAuthorized
                    ? MobileDeviceActivationRules.IsRebindSourceCredentialValid(
                        source,
                        registration,
                        request.CurrentCredential)
                    : MobileDeviceActivationRules.IsBindingRegistrationValid(
                        source,
                        registration);
                if (!sourceAuthorized)
                {
                    response = MutationDenied(MobileDeviceActivationReasonCodes.NotAvailable);
                    return;
                }

                if (!string.Equals(
                        source.DeviceSystem,
                        grant.DeviceSystem,
                        StringComparison.Ordinal))
                {
                    response = MutationDenied(credentialAuthorized
                        ? MobileDeviceActivationReasonCodes.NotAvailable
                        : MobileDeviceActivationReasonCodes.PlatformMismatch);
                    return;
                }

                var target = await LoadActiveTargetAsync(
                    grant.StoreCode,
                    grant.TargetUserGuid,
                    cancellationToken);
                if (target == null)
                {
                    response = MutationDenied(MobileDeviceActivationReasonCodes.NotAvailable);
                    return;
                }

                var nextBindingId = Guid.NewGuid();
                var nextDeviceCode = await AllocateDeviceCodeAsync(grant.StoreCode);
                if (await UpdateRegistrationForRebindAsync(
                        source,
                        nextDeviceCode,
                        grant.StoreCode,
                        deviceName,
                        nextBindingId) != 1)
                {
                    response = MutationDenied(credentialAuthorized
                        ? MobileDeviceActivationReasonCodes.NotAvailable
                        : MobileDeviceActivationReasonCodes.DeviceStateConflict);
                    return;
                }

                var revokedAtUtc = operationNow;
                var revoked = await _posmDb.Ado.ExecuteCommandAsync(
                    """
                    UPDATE [dbo].[POSM_MobileDeviceAccountBinding]
                    SET [RevokedAtUtc] = @RevokedAtUtc,
                        [RevokedBy] = @RevokedBy,
                        [RevokeReason] = @RevokeReason,
                        [ReplacedByBindingId] = @ReplacedByBindingId
                    WHERE [BindingId] = @BindingId
                      AND [Version] = @Version
                      AND [RevokedAtUtc] IS NULL;
                    """,
                    new SugarParameter("@RevokedAtUtc", revokedAtUtc),
                    new SugarParameter("@RevokedBy", CreatedBy),
                    new SugarParameter("@RevokeReason", "Rebound by one-time activation code"),
                    new SugarParameter("@ReplacedByBindingId", nextBindingId),
                    new SugarParameter("@BindingId", source.BindingId),
                    new SugarParameter("@Version", source.Version));
                if (revoked != 1)
                {
                    throw new InvalidOperationException(
                        "Current Mobile device binding changed during rebind.");
                }

                var nextBinding = new MobileDeviceAccountBinding
                {
                    BindingId = nextBindingId,
                    DeviceRegistrationId = source.DeviceRegistrationId,
                    HardwareId = source.HardwareId,
                    DeviceCode = nextDeviceCode,
                    StoreCode = grant.StoreCode,
                    DeviceSystem = source.DeviceSystem,
                    TargetUserGuid = grant.TargetUserGuid,
                    CredentialVerifier = newVerifier.ToArray(),
                    Version = checked(source.Version + 1),
                    BoundAtUtc = revokedAtUtc,
                };
                await _posmDb.Insertable(nextBinding).ExecuteCommandAsync();
                if (await ConsumeGrantAsync(
                        grant,
                        nextBinding,
                        "Rebind",
                        source.BindingId,
                        operationNow) != 1)
                {
                    throw new InvalidOperationException(
                        "Mobile rebind grant was not consumed atomically.");
                }

                response = ApiResponse<MobileDeviceActivationMutationResponseDto>.OK(
                    new MobileDeviceActivationMutationResponseDto(
                        true,
                        MobileDeviceActivationReasonCodes.Rebound,
                        "Mobile device was rebound to the target account.",
                        MapBinding(nextBinding, target)));
            });

            return response ?? MutationDenied(MobileDeviceActivationReasonCodes.NotAvailable);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(parsed.Secret);
            CryptographicOperations.ZeroMemory(newVerifier);
        }
    }

    public async Task<ApiResponse<MobileDeviceSessionExchangeResponseDto>> ExchangeSessionAsync(
        MobileDeviceSessionExchangeRequestDto request,
        CancellationToken cancellationToken)
    {
        var hardwareId = Normalize(request.HardwareId);
        if (hardwareId == null
            || hardwareId.Length > 100
            || !MobileDeviceCredentialCodec.IsCredentialShapeValid(request.Credential)
            || DeviceActivationCodeCodec.ContainsReservedActivationCode(hardwareId))
        {
            return SessionDenied();
        }

        var binding = await _posmDb.Queryable<MobileDeviceAccountBinding>()
            .Where(item => item.HardwareId == hardwareId && item.RevokedAtUtc == null)
            .FirstAsync(cancellationToken);
        if (binding == null
            || !MobileDeviceCredentialCodec.MatchesCredential(
                binding.CredentialVerifier,
                request.Credential))
        {
            return SessionDenied();
        }

        var registration = await LoadRegistrationStateAsync(
            binding.DeviceRegistrationId,
            cancellationToken);
        var target = await LoadActiveTargetAsync(
            binding.StoreCode,
            binding.TargetUserGuid,
            cancellationToken);
        if (target == null)
        {
            return SessionDenied();
        }
        var account = new MobileDeviceTargetAccountState(
            target.UserGuid,
            true,
            false,
            true);
        if (!MobileDeviceActivationRules.IsBoundCredentialValid(
                binding,
                registration,
                account,
                request.Credential))
        {
            return SessionDenied();
        }

        var roles = await LoadActiveRolesAsync(binding.TargetUserGuid, cancellationToken);
        var stores = await LoadAssignedStoresAsync(binding.TargetUserGuid, cancellationToken);
        var issued = _tokenIssuer.Issue(new MobileDeviceAccountTokenSubject(
            target.UserGuid,
            target.Username,
            target.Email,
            target.FullName,
            binding.BindingId,
            binding.DeviceRegistrationId,
            binding.HardwareId,
            binding.Version,
            roles));

        await _posmDb.Updateable<MobileDeviceAccountBinding>()
            .SetColumns(item => new MobileDeviceAccountBinding
            {
                LastSessionExchangeAtUtc = UtcNow(),
            })
            .Where(item => item.BindingId == binding.BindingId
                && item.Version == binding.Version
                && item.RevokedAtUtc == null)
            .ExecuteCommandAsync(cancellationToken);

        return ApiResponse<MobileDeviceSessionExchangeResponseDto>.OK(
            new MobileDeviceSessionExchangeResponseDto(
                issued.AccessToken,
                DeviceActivationCodeCodec.NormalizeUtcForWire(issued.ExpiresAtUtc),
                "Bearer",
                "deviceAccount",
                new MobileDeviceSessionUserDto(
                    target.UserGuid,
                    Redact(target.Username),
                    RedactNullable(target.FullName),
                    roles,
                    stores)));
    }

    public async Task<ApiResponse<MobileDeviceUnbindResponseDto>> UnbindAsync(
        MobileDeviceBindingContext currentBinding,
        MobileDeviceUnbindRequestDto request,
        string actor,
        CancellationToken cancellationToken)
    {
        var reason = Normalize(request.Reason) ?? "Unbound from Mobile settings";
        if (reason.Length > 200
            || DeviceActivationCodeCodec.ContainsReservedActivationCode(reason))
        {
            return ApiResponse<MobileDeviceUnbindResponseDto>.Error(
                "解绑原因无效",
                MobileDeviceActivationReasonCodes.DeviceStateConflict);
        }

        var success = false;
        await ExecuteTransactionAsync(async () =>
        {
            await AcquireApplicationLockAsync(
                $"HBMobile:ActivationHardware:{currentBinding.HardwareId}");
            var binding = await LockBindingAsync(currentBinding.BindingId);
            if (binding == null || !IsCurrentBinding(binding, currentBinding))
            {
                return;
            }

            var revokedAtUtc = UtcNow();
            var revoked = await _posmDb.Ado.ExecuteCommandAsync(
                """
                UPDATE [dbo].[POSM_MobileDeviceAccountBinding]
                SET [RevokedAtUtc] = @RevokedAtUtc,
                    [RevokedBy] = @RevokedBy,
                    [RevokeReason] = @RevokeReason
                WHERE [BindingId] = @BindingId
                  AND [Version] = @Version
                  AND [RevokedAtUtc] IS NULL;
                """,
                new SugarParameter("@RevokedAtUtc", revokedAtUtc),
                new SugarParameter("@RevokedBy", NormalizeActor(actor)),
                new SugarParameter("@RevokeReason", reason),
                new SugarParameter("@BindingId", binding.BindingId),
                new SugarParameter("@Version", binding.Version));
            if (revoked != 1)
            {
                return;
            }

            var disabled = await _posmDb.Ado.ExecuteCommandAsync(
                """
                UPDATE [dbo].[POSM_设备注册信息表]
                SET [设备状态] = @DisabledStatus,
                    [设备授权码] = @InvalidatedAuthorizationCode,
                    [最后修改时间] = @ModifiedAt,
                    [最后修改人] = @ModifiedBy,
                    [是否在线] = 0,
                    [最后心跳时间] = NULL,
                    [当前收银员ID] = NULL,
                    [当前收银员姓名] = NULL,
                    [收银员登录时间] = NULL
                WHERE [ID] = @RegistrationId
                  AND [设备硬件识别码] = @HardwareId
                  AND [设备类型] = N'Mobile'
                  AND [设备状态] = @EnabledStatus;
                """,
                new SugarParameter("@DisabledStatus", DisabledStatus),
                new SugarParameter("@InvalidatedAuthorizationCode", RandomNumberGenerator.GetHexString(32)),
                new SugarParameter("@ModifiedAt", LocalNow()),
                new SugarParameter("@ModifiedBy", CreatedBy),
                new SugarParameter("@RegistrationId", binding.DeviceRegistrationId),
                new SugarParameter("@HardwareId", binding.HardwareId),
                new SugarParameter("@EnabledStatus", EnabledStatus));
            if (disabled != 1)
            {
                throw new InvalidOperationException(
                    "Mobile device registration changed during unbind.");
            }
            success = true;
        });

        return success
            ? ApiResponse<MobileDeviceUnbindResponseDto>.OK(
                new MobileDeviceUnbindResponseDto(true),
                "Mobile 设备已解绑")
            : ApiResponse<MobileDeviceUnbindResponseDto>.Error(
                "Mobile 设备绑定已失效",
                MobileDeviceActivationReasonCodes.BindingUnavailable);
    }

    public async Task<MobileDeviceBoundAccountValidationResult> ValidateTokenBindingAsync(
        MobileDeviceBindingContext currentBinding,
        CancellationToken cancellationToken)
    {
        var binding = await _posmDb.Queryable<MobileDeviceAccountBinding>()
            .Where(item =>
                item.BindingId == currentBinding.BindingId
                && item.Version == currentBinding.BindingVersion
                && item.DeviceRegistrationId == currentBinding.DeviceRegistrationId
                && item.HardwareId == currentBinding.HardwareId
                && item.TargetUserGuid == currentBinding.UserGuid
                && item.RevokedAtUtc == null)
            .FirstAsync(cancellationToken);
        var registration = binding == null
            ? null
            : await LoadRegistrationStateAsync(
                binding.DeviceRegistrationId,
                cancellationToken);
        var target = binding == null
            ? null
            : await LoadActiveTargetAsync(
                binding.StoreCode,
                binding.TargetUserGuid,
                cancellationToken);
        var account = target == null
            ? null
            : new MobileDeviceTargetAccountState(
                target.UserGuid,
                true,
                false,
                true);
        if (!MobileDeviceActivationRules.IsTokenBindingValid(
                binding,
                currentBinding,
                registration,
                account))
        {
            return new MobileDeviceBoundAccountValidationResult(false);
        }

        return new MobileDeviceBoundAccountValidationResult(
            true,
            binding!.TargetUserGuid);
    }

    public async Task<MobileDeviceCredentialValidationResult> ValidateBoundDeviceCredentialAsync(
        string hardwareId,
        string credential,
        CancellationToken cancellationToken = default)
    {
        var normalizedHardwareId = Normalize(hardwareId);
        if (normalizedHardwareId == null
            || normalizedHardwareId.Length > 100
            || DeviceActivationCodeCodec.ContainsReservedActivationCode(normalizedHardwareId))
        {
            return new MobileDeviceCredentialValidationResult(false, false);
        }

        var bindingRecords = await _posmDb.Queryable<MobileDeviceAccountBinding>()
            .Where(item => item.HardwareId == normalizedHardwareId)
            .OrderBy(
                item => item.RevokedAtUtc == null ? 0 : 1,
                OrderByType.Asc)
            .OrderBy(item => item.BoundAtUtc, OrderByType.Desc)
            .Take(2)
            .ToListAsync(cancellationToken);
        var gate = MobileDeviceActivationRules.SelectBindingCredentialGate(bindingRecords);
        var binding = gate.ActiveBinding;
        var credentialShapeValid = MobileDeviceCredentialCodec.IsCredentialShapeValid(credential);
        // 即使硬件或凭据格式无效也执行一轮固定大小 SHA-256，降低硬件枚举时间差。
        var credentialMatches = MobileDeviceCredentialCodec.MatchesCredential(
            binding?.CredentialVerifier ?? MissingCredentialVerifier,
            credentialShapeValid ? credential : InvalidCredentialProbe);
        if (!gate.RequiresBoundCredential)
        {
            return new MobileDeviceCredentialValidationResult(false, false);
        }
        if (binding == null || !credentialShapeValid || !credentialMatches)
        {
            // 有任意绑定历史即由新域永久接管；撤销或结构异常同样不能回退内部授权码。
            return new MobileDeviceCredentialValidationResult(true, false);
        }

        var registration = await LoadRegistrationStateAsync(
            binding.DeviceRegistrationId,
            cancellationToken);
        var target = await LoadActiveTargetAsync(
            binding.StoreCode,
            binding.TargetUserGuid,
            cancellationToken);
        var account = target == null
            ? null
            : new MobileDeviceTargetAccountState(
                target.UserGuid,
                true,
                false,
                true);
        return new MobileDeviceCredentialValidationResult(
            true,
            MobileDeviceActivationRules.IsBoundCredentialValid(
                binding,
                registration,
                account,
                credential));
    }

    private async Task<ApiResponse<MobileDeviceActivationMutationResponseDto>> RecoverRedeemAsync(
        MobileDeviceActivationGrant grant,
        byte[] credentialVerifier,
        CancellationToken cancellationToken)
    {
        if (grant.ConsumedBindingId == null)
        {
            return MutationDenied(MobileDeviceActivationReasonCodes.NotAvailable);
        }
        var binding = await LockBindingAsync(grant.ConsumedBindingId.Value);
        if (binding == null
            || binding.RevokedAtUtc != null
            || !MobileDeviceCredentialCodec.MatchesVerifier(
                binding.CredentialVerifier,
                credentialVerifier)
            || !string.Equals(
                binding.HardwareId,
                grant.ConsumedHardwareId,
                StringComparison.Ordinal))
        {
            return MutationDenied(MobileDeviceActivationReasonCodes.NotAvailable);
        }

        var registration = await LoadRegistrationStateAsync(
            binding.DeviceRegistrationId,
            cancellationToken);
        var target = await LoadActiveTargetAsync(
            binding.StoreCode,
            binding.TargetUserGuid,
            cancellationToken);
        return target == null
            || !MobileDeviceActivationRules.IsBindingRegistrationValid(
                binding,
                registration)
            ? MutationDenied(MobileDeviceActivationReasonCodes.NotAvailable)
            : ApiResponse<MobileDeviceActivationMutationResponseDto>.OK(
                new MobileDeviceActivationMutationResponseDto(
                    true,
                    MobileDeviceActivationReasonCodes.ActivationRecovered,
                    "Mobile device activation result was recovered.",
                    MapBinding(binding, target)));
    }

    private async Task<ApiResponse<MobileDeviceActivationMutationResponseDto>> RecoverRebindAsync(
        MobileDeviceActivationGrant grant,
        MobileDeviceActivationRebindRequestDto request,
        byte[] newVerifier,
        byte[] activationSecret,
        CancellationToken cancellationToken)
    {
        var hardwareId = Normalize(request.CurrentHardwareId);
        if (grant.ConsumedAtUtc == null
            || !string.Equals(grant.ConsumptionKind, "Rebind", StringComparison.Ordinal)
            || grant.PreviousBindingId == null
            || grant.ConsumedBindingId == null
            || hardwareId == null
            || string.IsNullOrWhiteSpace(request.CurrentCredential))
        {
            return MutationDenied(MobileDeviceActivationReasonCodes.NotAvailable);
        }

        MobileDeviceAccountBinding? next = null;
        await ExecuteTransactionAsync(async () =>
        {
            await AcquireApplicationLockAsync($"HBMobile:ActivationGrant:{grant.GrantId:N}");
            await AcquireApplicationLockAsync($"HBMobile:ActivationHardware:{hardwareId}");
            var lockedGrant = await LockGrantAsync(grant.GrantId);
            if (lockedGrant == null
                || !DeviceActivationCodeCodec.Matches(
                    lockedGrant.SecretHash,
                    activationSecret)
                || lockedGrant.ConsumedAtUtc == null
                || !string.Equals(
                    lockedGrant.ConsumptionKind,
                    "Rebind",
                    StringComparison.Ordinal)
                || lockedGrant.PreviousBindingId is not Guid previousBindingId
                || lockedGrant.ConsumedBindingId is not Guid consumedBindingId)
            {
                return;
            }
            grant = lockedGrant;
            var previous = await LockBindingAsync(previousBindingId);
            var candidate = await LockBindingAsync(consumedBindingId);
            if (previous == null
                || candidate == null
                || candidate.RevokedAtUtc != null
                || previous.ReplacedByBindingId != candidate.BindingId
                || !string.Equals(previous.HardwareId, hardwareId, StringComparison.Ordinal)
                || !MobileDeviceCredentialCodec.MatchesCredential(
                    previous.CredentialVerifier,
                    request.CurrentCredential)
                || !MobileDeviceCredentialCodec.MatchesVerifier(
                    candidate.CredentialVerifier,
                    newVerifier))
            {
                return;
            }
            next = candidate;
        });

        if (next == null)
        {
            return MutationDenied(MobileDeviceActivationReasonCodes.NotAvailable);
        }
        var registration = await LoadRegistrationStateAsync(
            next.DeviceRegistrationId,
            cancellationToken);
        var target = await LoadActiveTargetAsync(
            next.StoreCode,
            next.TargetUserGuid,
            cancellationToken);
        return target == null
            || !MobileDeviceActivationRules.IsBindingRegistrationValid(
                next,
                registration)
            ? MutationDenied(MobileDeviceActivationReasonCodes.NotAvailable)
            : ApiResponse<MobileDeviceActivationMutationResponseDto>.OK(
                new MobileDeviceActivationMutationResponseDto(
                    true,
                    MobileDeviceActivationReasonCodes.RebindRecovered,
                    "Mobile device rebind result was recovered.",
                    MapBinding(next, target)));
    }

    private async Task<int> CreateMobileRegistrationAsync(
        string hardwareId,
        string deviceCode,
        string storeCode,
        string deviceSystem,
        string deviceName,
        Guid bindingId) =>
        await _posmDb.Ado.GetIntAsync(
            """
            INSERT INTO [dbo].[POSM_设备注册信息表]
                ([设备硬件识别码], [系统设备编号], [分店代码], [设备类型], [设备系统],
                 [设备状态], [设备授权码], [备注], [创建时间], [创建人])
            VALUES
                (@HardwareId, @DeviceCode, @StoreCode, N'Mobile', @DeviceSystem,
                 @EnabledStatus, @AuthorizationCode, @Remark, @CreatedAt, @CreatedBy);
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """,
            new SugarParameter("@HardwareId", hardwareId),
            new SugarParameter("@DeviceCode", deviceCode),
            new SugarParameter("@StoreCode", storeCode),
            new SugarParameter("@DeviceSystem", deviceSystem),
            new SugarParameter("@EnabledStatus", EnabledStatus),
            new SugarParameter("@AuthorizationCode", BuildInternalAuthorizationCode(bindingId)),
            new SugarParameter("@Remark", BuildRemark("Activated", deviceName)),
            new SugarParameter("@CreatedAt", LocalNow()),
            new SugarParameter("@CreatedBy", CreatedBy));

    private async Task<int> ReuseMobileRegistrationAsync(
        ActivationDeviceRecord registration,
        string deviceCode,
        string storeCode,
        string deviceSystem,
        string deviceName,
        Guid bindingId)
    {
        var affected = await _posmDb.Ado.ExecuteCommandAsync(
            """
            UPDATE [dbo].[POSM_设备注册信息表]
            SET [系统设备编号] = @DeviceCode,
                [分店代码] = @StoreCode,
                [设备类型] = N'Mobile',
                [设备系统] = @DeviceSystem,
                [设备状态] = @EnabledStatus,
                [设备授权码] = @AuthorizationCode,
                [备注] = RIGHT(CONCAT(ISNULL([备注], ''), @Remark), 500),
                [最后修改时间] = @ModifiedAt,
                [最后修改人] = @ModifiedBy,
                [是否在线] = 0,
                [最后心跳时间] = NULL,
                [当前收银员ID] = NULL,
                [当前收银员姓名] = NULL,
                [收银员登录时间] = NULL
            WHERE [ID] = @RegistrationId
              AND [设备硬件识别码] = @HardwareId
              AND [设备类型] = N'Mobile';
            """,
            new SugarParameter("@DeviceCode", deviceCode),
            new SugarParameter("@StoreCode", storeCode),
            new SugarParameter("@DeviceSystem", deviceSystem),
            new SugarParameter("@EnabledStatus", EnabledStatus),
            new SugarParameter("@AuthorizationCode", BuildInternalAuthorizationCode(bindingId)),
            new SugarParameter("@Remark", " | " + BuildRemark("Upgraded", deviceName)),
            new SugarParameter("@ModifiedAt", LocalNow()),
            new SugarParameter("@ModifiedBy", CreatedBy),
            new SugarParameter("@RegistrationId", registration.Id),
            new SugarParameter("@HardwareId", registration.HardwareId));
        if (affected != 1)
        {
            throw new InvalidOperationException(
                "Legacy Mobile registration changed during activation upgrade.");
        }
        return registration.Id;
    }

    private Task<int> UpdateRegistrationForRebindAsync(
        MobileDeviceAccountBinding source,
        string nextDeviceCode,
        string targetStoreCode,
        string deviceName,
        Guid nextBindingId) =>
        _posmDb.Ado.ExecuteCommandAsync(
            """
            UPDATE [dbo].[POSM_设备注册信息表]
            SET [系统设备编号] = @DeviceCode,
                [分店代码] = @StoreCode,
                [设备授权码] = @AuthorizationCode,
                [备注] = RIGHT(CONCAT(ISNULL([备注], ''), @Remark), 500),
                [最后修改时间] = @ModifiedAt,
                [最后修改人] = @ModifiedBy,
                [是否在线] = 0,
                [最后心跳时间] = NULL,
                [当前收银员ID] = NULL,
                [当前收银员姓名] = NULL,
                [收银员登录时间] = NULL
            WHERE [ID] = @RegistrationId
              AND [设备硬件识别码] = @HardwareId
              AND [系统设备编号] = @CurrentDeviceCode
              AND [分店代码] = @CurrentStoreCode
              AND [设备类型] = N'Mobile'
              AND [设备系统] = @DeviceSystem
              AND [设备状态] = @EnabledStatus;
            """,
            new SugarParameter("@DeviceCode", nextDeviceCode),
            new SugarParameter("@StoreCode", targetStoreCode),
            new SugarParameter("@AuthorizationCode", BuildInternalAuthorizationCode(nextBindingId)),
            new SugarParameter("@Remark", " | " + BuildRemark("Rebound", deviceName)),
            new SugarParameter("@ModifiedAt", LocalNow()),
            new SugarParameter("@ModifiedBy", CreatedBy),
            new SugarParameter("@RegistrationId", source.DeviceRegistrationId),
            new SugarParameter("@HardwareId", source.HardwareId),
            new SugarParameter("@CurrentDeviceCode", source.DeviceCode),
            new SugarParameter("@CurrentStoreCode", source.StoreCode),
            new SugarParameter("@DeviceSystem", source.DeviceSystem),
            new SugarParameter("@EnabledStatus", EnabledStatus));

    private Task<int> DisableOtherMobileRegistrationsAsync(
        string hardwareId,
        int activeRegistrationId,
        string targetStoreCode) =>
        _posmDb.Ado.ExecuteCommandAsync(
            """
            UPDATE [dbo].[POSM_设备注册信息表]
            SET [设备状态] = @DisabledStatus,
                [设备授权码] = @InvalidatedAuthorizationCode,
                [备注] = RIGHT(CONCAT(ISNULL([备注], ''), @Remark), 500),
                [最后修改时间] = @ModifiedAt,
                [最后修改人] = @ModifiedBy,
                [是否在线] = 0,
                [最后心跳时间] = NULL,
                [当前收银员ID] = NULL,
                [当前收银员姓名] = NULL,
                [收银员登录时间] = NULL
            WHERE [设备硬件识别码] = @HardwareId
              AND [ID] <> @ActiveRegistrationId
              AND [设备类型] = N'Mobile'
              AND [设备状态] <> @DisabledStatus;
            """,
            new SugarParameter("@DisabledStatus", DisabledStatus),
            new SugarParameter("@InvalidatedAuthorizationCode", RandomNumberGenerator.GetHexString(32)),
            new SugarParameter("@Remark", $" | Disabled by Mobile account binding to {targetStoreCode}"),
            new SugarParameter("@ModifiedAt", LocalNow()),
            new SugarParameter("@ModifiedBy", CreatedBy),
            new SugarParameter("@HardwareId", hardwareId),
            new SugarParameter("@ActiveRegistrationId", activeRegistrationId));

    private async Task<int> ConsumeGrantAsync(
        MobileDeviceActivationGrant grant,
        MobileDeviceAccountBinding binding,
        string consumptionKind,
        Guid? previousBindingId,
        DateTime consumedAtUtc) =>
        await _posmDb.Ado.ExecuteCommandAsync(
            """
            UPDATE [dbo].[POSM_MobileDeviceActivationGrant]
            SET [ConsumedAtUtc] = @ConsumedAtUtc,
                [ConsumedHardwareId] = @HardwareId,
                [ConsumedDeviceCode] = @DeviceCode,
                [ConsumedDeviceRegistrationId] = @RegistrationId,
                [ConsumedBindingId] = @BindingId,
                [ConsumedDeviceSystem] = @DeviceSystem,
                [ConsumptionKind] = @ConsumptionKind,
                [PreviousBindingId] = @PreviousBindingId
            WHERE [GrantId] = @GrantId
              AND [RevokedAtUtc] IS NULL
              AND [ConsumedAtUtc] IS NULL
              AND [ExpiresAtUtc] > @ConsumedAtUtc;
            """,
            new SugarParameter("@ConsumedAtUtc", consumedAtUtc),
            new SugarParameter("@HardwareId", binding.HardwareId),
            new SugarParameter("@DeviceCode", binding.DeviceCode),
            new SugarParameter("@RegistrationId", binding.DeviceRegistrationId),
            new SugarParameter("@BindingId", binding.BindingId),
            new SugarParameter("@DeviceSystem", binding.DeviceSystem),
            new SugarParameter("@ConsumptionKind", consumptionKind),
            new SugarParameter("@PreviousBindingId", previousBindingId),
            new SugarParameter("@GrantId", grant.GrantId));

    private async Task<string> AllocateDeviceCodeAsync(string storeCode)
    {
        var normalizedStore = new string(storeCode
            .Where(character => char.IsAsciiLetterOrDigit(character) || character == '_')
            .Take(20)
            .ToArray());
        if (normalizedStore.Length == 0)
        {
            normalizedStore = "STORE";
        }

        for (var attempt = 0; attempt < 20; attempt++)
        {
            var candidate = $"MOB_{normalizedStore}_{RandomNumberGenerator.GetHexString(8)}";
            var exists = await _posmDb.Ado.GetIntAsync(
                """
                SELECT COUNT(1)
                FROM [dbo].[POSM_设备注册信息表] WITH (UPDLOCK, HOLDLOCK)
                WHERE [系统设备编号] = @DeviceCode;
                """,
                new SugarParameter("@DeviceCode", candidate));
            if (exists == 0)
            {
                return candidate;
            }
        }
        throw new InvalidOperationException("Could not allocate Mobile device code.");
    }

    private async Task<MobileDeviceActivationGrant?> LockGrantAsync(Guid grantId) =>
        await _posmDb.Ado.SqlQuerySingleAsync<MobileDeviceActivationGrant>(
            LockGrantSql,
            new SugarParameter("@GrantId", grantId));

    private async Task<MobileDeviceAccountBinding?> LockBindingAsync(Guid bindingId) =>
        await _posmDb.Ado.SqlQuerySingleAsync<MobileDeviceAccountBinding>(
            LockBindingSql,
            new SugarParameter("@BindingId", bindingId));

    private async Task<IReadOnlyList<MobileDeviceAccountBinding>> LockHardwareBindingsAsync(
        string hardwareId) =>
        await _posmDb.Ado.SqlQueryAsync<MobileDeviceAccountBinding>(
            LockHardwareBindingsSql,
            new SugarParameter("@HardwareId", hardwareId));

    private async Task<IReadOnlyList<ActivationDeviceRecord>> LockHardwareRegistrationsAsync(
        string hardwareId) =>
        await _posmDb.Ado.SqlQueryAsync<ActivationDeviceRecord>(
            LockHardwareRegistrationsSql,
            new SugarParameter("@HardwareId", hardwareId));

    private async Task AcquireApplicationLockAsync(string resource)
    {
        var result = await _posmDb.Ado.GetIntAsync(
            AcquireApplicationLockSql,
            new SugarParameter("@Resource", resource));
        if (result < 0)
        {
            throw new InvalidOperationException("Could not acquire Mobile activation lock.");
        }
    }

    private async Task ExecuteTransactionAsync(Func<Task> action)
    {
        await _posmDb.Ado.BeginTranAsync();
        try
        {
            await action();
            await _posmDb.Ado.CommitTranAsync();
        }
        catch
        {
            await _posmDb.Ado.RollbackTranAsync();
            throw;
        }
    }

    private async Task<ActivationTargetRow?> LoadActiveTargetAsync(
        string storeCode,
        string userGuid,
        CancellationToken cancellationToken)
    {
        var rows = await MobileDeviceActivationQueries
            .BuildActiveTargetQuery(_mainDb, storeCode, userGuid)
            .ToListAsync(cancellationToken);
        return rows.FirstOrDefault();
    }

    private async Task<int> CountAssignedStoresAsync(
        string userGuid,
        CancellationToken cancellationToken) =>
        await MobileDeviceActivationQueries
            .BuildAssignedStoreCountQuery(_mainDb, userGuid)
            .CountAsync(cancellationToken);

    private async Task<MobileDeviceRegistrationState?> LoadRegistrationStateAsync(
        int registrationId,
        CancellationToken cancellationToken)
    {
        // SqlSugar 先投影到可无参构造的行模型，避免位置 record 中的空值表达式被误解析为列名。
        var row = await MobileDeviceActivationQueries
            .BuildRegistrationStateQuery(_posmDb, registrationId)
            .FirstAsync(cancellationToken);
        return row is null
            ? null
            : new MobileDeviceRegistrationState(
                row.DeviceRegistrationId,
                row.HardwareId,
                row.DeviceCode,
                row.StoreCode ?? string.Empty,
                row.DeviceSystem,
                row.DeviceType,
                row.DeviceStatus);
    }

    private async Task<List<string>> LoadActiveRolesAsync(
        string userGuid,
        CancellationToken cancellationToken) =>
        await MobileDeviceActivationQueries
            .BuildActiveRolesQuery(_mainDb, userGuid)
            .ToListAsync(cancellationToken);

    private async Task<List<MobileDeviceSessionStoreDto>> LoadAssignedStoresAsync(
        string userGuid,
        CancellationToken cancellationToken)
    {
        // SqlSugar 多表投影先落到可无参构造的行模型，再在内存中创建只读 DTO。
        var rows = await MobileDeviceActivationQueries
            .BuildAssignedStoresQuery(_mainDb, userGuid)
            .ToListAsync(cancellationToken);
        return rows
            .Select(row => new MobileDeviceSessionStoreDto(
                row.StoreGuid,
                row.StoreCode,
                row.StoreName,
                row.IsPrimary))
            .ToList();
    }

    private static bool IsCurrentBinding(
        MobileDeviceAccountBinding binding,
        MobileDeviceBindingContext context) =>
        binding.RevokedAtUtc == null
        && binding.BindingId == context.BindingId
        && binding.Version == context.BindingVersion
        && binding.DeviceRegistrationId == context.DeviceRegistrationId
        && string.Equals(binding.HardwareId, context.HardwareId, StringComparison.Ordinal)
        && string.Equals(binding.TargetUserGuid, context.UserGuid, StringComparison.Ordinal);

    private static MobileDeviceRegistrationState ToRegistrationState(
        ActivationDeviceRecord registration) =>
        new(
            registration.Id,
            registration.HardwareId,
            registration.DeviceCode,
            registration.StoreCode,
            registration.DeviceSystem,
            registration.DeviceType,
            registration.DeviceStatus);

    private static MobileDeviceBindingDto MapBinding(
        MobileDeviceAccountBinding binding,
        ActivationTargetRow target) =>
        new(
            binding.BindingId,
            binding.DeviceRegistrationId,
            Redact(binding.DeviceCode),
            Redact(binding.StoreCode),
            Redact(target.StoreName),
            Redact(binding.DeviceSystem),
            Redact(binding.TargetUserGuid),
            Redact(target.Username),
            RedactNullable(target.FullName),
            DeviceActivationCodeCodec.NormalizeUtcForWire(binding.BoundAtUtc));

    private static ApiResponse<MobileDeviceActivationPreviewResponseDto> PreviewDenied(
        string reasonCode) =>
        ApiResponse<MobileDeviceActivationPreviewResponseDto>.OK(
            new MobileDeviceActivationPreviewResponseDto(
                false,
                reasonCode,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                "Mobile device activation code is not available."));

    private static ApiResponse<MobileDeviceActivationMutationResponseDto> MutationDenied(
        string reasonCode) =>
        ApiResponse<MobileDeviceActivationMutationResponseDto>.OK(
            new MobileDeviceActivationMutationResponseDto(
                false,
                reasonCode,
                "Mobile device activation code is not available.",
                null));

    private static ApiResponse<MobileDeviceSessionExchangeResponseDto> SessionDenied() =>
        ApiResponse<MobileDeviceSessionExchangeResponseDto>.Error(
            "Mobile device credential is not available.",
            MobileDeviceActivationReasonCodes.CredentialInvalid);

    private static string BuildInternalAuthorizationCode(Guid bindingId) =>
        $"M{bindingId:N}{RandomNumberGenerator.GetHexString(16)}";

    private static string BuildRemark(string action, string deviceName)
    {
        var remark = string.IsNullOrWhiteSpace(deviceName)
            ? $"{action} by Mobile account activation"
            : $"{action} by Mobile account activation: {deviceName}";
        return remark[..Math.Min(500, remark.Length)];
    }

    private static string? Normalize(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    private static string NormalizeActor(string? value)
    {
        var actor = Normalize(value) ?? "System";
        if (DeviceActivationCodeCodec.ContainsReservedActivationCode(actor))
        {
            return "[REDACTED]";
        }
        return actor[..Math.Min(128, actor.Length)];
    }

    private static string Redact(string value) =>
        DeviceActivationCodeCodec.RedactReservedActivationMetadata(value) ?? string.Empty;

    private static string? RedactNullable(string? value) =>
        DeviceActivationCodeCodec.RedactReservedActivationMetadata(value);

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;

    private DateTime LocalNow() => _timeProvider.GetLocalNow().DateTime;

    private sealed class ActivationDeviceRecord
    {
        public int Id { get; set; }
        public string DeviceCode { get; set; } = string.Empty;
        public string StoreCode { get; set; } = string.Empty;
        public string HardwareId { get; set; } = string.Empty;
        public string DeviceType { get; set; } = string.Empty;
        public int DeviceStatus { get; set; }
        public string DeviceSystem { get; set; } = string.Empty;
    }

}
