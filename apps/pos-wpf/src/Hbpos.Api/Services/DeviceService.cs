using Hbpos.Api.Data;
using Hbpos.Contracts.Devices;
using SqlSugar;

namespace Hbpos.Api.Services;

public interface IDeviceService
{
    Task<DeviceRegisterResponse> RegisterAsync(DeviceRegisterRequest request, CancellationToken cancellationToken);

    Task<DeviceVerifyResponse> VerifyAsync(DeviceVerifyRequest request, CancellationToken cancellationToken);

    Task<DeviceReregisterResponse> ReregisterAsync(
        DeviceReregisterRequest request,
        DeviceReregisterContext currentDevice,
        CancellationToken cancellationToken);

    Task<bool> UpdateRuntimeStatusAsync(
        string hardwareId,
        string deviceCode,
        string storeCode,
        bool isOnline,
        string? cashierId,
        string? cashierName,
        CancellationToken cancellationToken);
}

public interface IDeviceRegistrationRepository
{
    Task<DeviceRegistrationRecord?> FindLatestByHardwareIdAsync(
        string hardwareId,
        CancellationToken cancellationToken);

    Task<DeviceRegistrationRecord?> FindByDeviceCodeAsync(
        string deviceCode,
        string storeCode,
        CancellationToken cancellationToken);

    Task<DeviceRegistrationRecord?> FindActiveOrLockedRegistrationAsync(
        string hardwareId,
        CancellationToken cancellationToken);

    Task<DeviceRegistrationRecord?> FindLatestByHardwareIdAndStoreCodeAsync(
        string hardwareId,
        string storeCode,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<DeviceRegistrationRecord>> FindAllByHardwareIdForRegistrationAsync(
        string hardwareId,
        CancellationToken cancellationToken);

    Task<int> DisablePendingRegistrationAsync(
        DeviceRegistrationDisableRequest request,
        CancellationToken cancellationToken);

    Task<int> DisableActiveRegistrationAsync(
        string hardwareId,
        string deviceCode,
        string storeCode,
        string remarkSuffix,
        CancellationToken cancellationToken);

    Task<int> ResetRegistrationForReregisterAsync(
        DeviceRegistrationResetForReregisterRequest request,
        CancellationToken cancellationToken);

    Task CreateRegistrationAsync(
        DeviceRegistrationCreateRequest request,
        CancellationToken cancellationToken);

    Task<int> UpdateRuntimeStatusAsync(
        DeviceRuntimeStatusUpdateRequest request,
        CancellationToken cancellationToken);

    Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken);
}

public sealed record DeviceReregisterContext(
    string DeviceCode,
    string StoreCode,
    string HardwareId);

public sealed record DeviceStoreInfo(
    string StoreCode,
    string StoreName);

public sealed class DeviceRegistrationRecord
{
    public int Id { get; set; }

    public string? DeviceCode { get; set; }

    public string? StoreCode { get; set; }

    public string? HardwareId { get; set; }

    public int DeviceStatus { get; set; }

    public string? AuthorizationCode { get; set; }
}

public sealed class DeviceRegistrationDisableRequest
{
    public string HardwareId { get; init; } = string.Empty;

    public string StoreCode { get; init; } = string.Empty;

    public string DeviceCode { get; init; } = string.Empty;

    public string RemarkSuffix { get; init; } = string.Empty;
}

public sealed class DeviceRegistrationCreateRequest
{
    public string HardwareId { get; init; } = string.Empty;

    public string DeviceCode { get; init; } = string.Empty;

    public string StoreCode { get; init; } = string.Empty;

    public int DeviceStatus { get; init; }

    public string AuthorizationCode { get; init; } = string.Empty;

    public string Remark { get; init; } = string.Empty;

    public DateTime CreatedAt { get; init; }

    public string CreatedBy { get; init; } = string.Empty;

    public string DeviceType { get; init; } = "POS";

    public string DeviceSystem { get; init; } = "Windows";
}

public sealed class DeviceRegistrationResetForReregisterRequest
{
    public int RegistrationId { get; init; }

    public string HardwareId { get; init; } = string.Empty;

    public string StoreCode { get; init; } = string.Empty;

    public string DeviceCode { get; init; } = string.Empty;

    public int ExpectedDeviceStatus { get; init; }

    public string? ExpectedAuthorizationCode { get; init; }

    public string AuthorizationCode { get; init; } = string.Empty;

    public string RemarkSuffix { get; init; } = string.Empty;

    public DateTime ModifiedAt { get; init; }

    public string ModifiedBy { get; init; } = string.Empty;
}

public sealed record DeviceRuntimeStatusUpdateRequest(
    string HardwareId,
    string DeviceCode,
    string StoreCode,
    bool IsOnline,
    string? CashierId,
    string? CashierName,
    DateTime ReportedAt);

public sealed class DeviceService : IDeviceService
{
    private const int PendingStatus = -1;
    private const int DisabledStatus = 0;
    private const int EnabledStatus = 1;
    private const int LockedStatus = 2;
    private const int UnregisteredStatus = 3;

    private readonly HbposSqlSugarContext? dbContext;
    private readonly IDeviceRegistrationRepository deviceRegistrationRepository;
    private readonly Func<string, CancellationToken, Task<DeviceStoreInfo?>> loadStoreAsync;
    private readonly Func<DateTime> nowProvider;

    public DeviceService(
        HbposSqlSugarContext dbContext,
        IDeviceRegistrationRepository deviceRegistrationRepository,
        Func<DateTime>? nowProvider = null)
    {
        this.dbContext = dbContext;
        this.deviceRegistrationRepository = deviceRegistrationRepository;
        loadStoreAsync = LoadStoreAsync;
        this.nowProvider = nowProvider ?? (() => DateTime.Now);
    }

    public DeviceService(
        IDeviceRegistrationRepository deviceRegistrationRepository,
        Func<string, CancellationToken, Task<DeviceStoreInfo?>> loadStoreAsync,
        Func<DateTime>? nowProvider = null)
    {
        this.deviceRegistrationRepository = deviceRegistrationRepository;
        this.loadStoreAsync = loadStoreAsync;
        this.nowProvider = nowProvider ?? (() => DateTime.Now);
    }

    public async Task<DeviceRegisterResponse> RegisterAsync(
        DeviceRegisterRequest request,
        CancellationToken cancellationToken)
    {
        var storeCode = Normalize(request.StoreCode);
        var hardwareId = Normalize(request.HardwareId);
        var terminalName = Normalize(request.TerminalName);

        if (string.IsNullOrEmpty(storeCode))
        {
            return CreateRegisterResponse(string.Empty, storeCode, string.Empty, UnregisteredStatus, "storeCode is required");
        }

        if (string.IsNullOrEmpty(hardwareId))
        {
            return CreateRegisterResponse(string.Empty, storeCode, string.Empty, UnregisteredStatus, "hardwareId is required");
        }

        var store = await loadStoreAsync(storeCode, cancellationToken);
        if (store is null)
        {
            return CreateRegisterResponse(string.Empty, storeCode, string.Empty, UnregisteredStatus, "Store was not found or inactive.");
        }

        var now = nowProvider();
        DeviceRegisterResponse? response = null;

        // 关键逻辑：匿名注册的全量检查、旧待确认禁用与目标重置/新建必须在同一锁定事务中完成。
        await deviceRegistrationRepository.ExecuteInTransactionAsync(
            async token =>
            {
                var registrations = await deviceRegistrationRepository
                    .FindAllByHardwareIdForRegistrationAsync(hardwareId, token);

                // 关键逻辑：同一硬件任意启用或锁定记录都会阻止匿名注册，且不得产生任何写入。
                var blockingRegistration = registrations.FirstOrDefault(static registration =>
                    registration.DeviceStatus is EnabledStatus or LockedStatus);
                if (blockingRegistration is not null)
                {
                    response = CreateRegisterResponse(
                        blockingRegistration.DeviceCode ?? string.Empty,
                        blockingRegistration.StoreCode ?? storeCode,
                        string.Equals(blockingRegistration.StoreCode, storeCode, StringComparison.OrdinalIgnoreCase)
                            ? store.StoreName
                            : string.Empty,
                        blockingRegistration.DeviceStatus,
                        "Device hardware is already registered and cannot be registered anonymously.");
                    return;
                }

                var targetRegistration = registrations.FirstOrDefault(registration =>
                    string.Equals(registration.StoreCode, storeCode, StringComparison.OrdinalIgnoreCase));

                // 关键逻辑：所有目标状态与设备号校验必须先于任何写入，拒绝请求不得顺带清理其他待确认记录。
                if (targetRegistration is not null
                    && targetRegistration.DeviceStatus is not PendingStatus and not DisabledStatus and not UnregisteredStatus)
                {
                    response = CreateRegisterResponse(
                        targetRegistration.DeviceCode ?? string.Empty,
                        storeCode,
                        store.StoreName,
                        targetRegistration.DeviceStatus,
                        "Device registration cannot be reused anonymously in its current status.");
                    return;
                }

                if (targetRegistration is not null && string.IsNullOrWhiteSpace(targetRegistration.DeviceCode))
                {
                    response = CreateRegisterResponse(
                        string.Empty,
                        storeCode,
                        store.StoreName,
                        targetRegistration.DeviceStatus == PendingStatus
                            ? DisabledStatus
                            : targetRegistration.DeviceStatus,
                        "Existing device registration has no reusable device code.");
                    return;
                }

                // 关键逻辑：只保留目标店最新的待确认记录，其余同硬件待确认记录必须逐条条件禁用。
                foreach (var pendingRegistration in registrations.Where(registration =>
                             registration.DeviceStatus == PendingStatus
                             && (targetRegistration?.DeviceStatus != PendingStatus
                                 || registration.Id != targetRegistration.Id)))
                {
                    var disabledCount = await deviceRegistrationRepository.DisablePendingRegistrationAsync(
                        new DeviceRegistrationDisableRequest
                        {
                            HardwareId = hardwareId,
                            StoreCode = pendingRegistration.StoreCode ?? string.Empty,
                            DeviceCode = pendingRegistration.DeviceCode ?? string.Empty,
                            RemarkSuffix = $" | Disabled by registration switch to {storeCode} at {now:O}"
                        },
                        token);
                    if (disabledCount != 1)
                    {
                        throw new InvalidOperationException("Pending device registration changed during registration.");
                    }
                }

                if (targetRegistration?.DeviceStatus == PendingStatus)
                {
                    response = new DeviceRegisterResponse(
                        targetRegistration.DeviceCode ?? string.Empty,
                        storeCode,
                        store.StoreName,
                        PendingStatus,
                        false,
                        GetStatusMessage(PendingStatus),
                        null);
                    return;
                }

                if (targetRegistration is not null)
                {
                    var authorizationCode = Guid.NewGuid().ToString("N");
                    var resetCount = await deviceRegistrationRepository.ResetRegistrationForReregisterAsync(
                        new DeviceRegistrationResetForReregisterRequest
                        {
                            RegistrationId = targetRegistration.Id,
                            HardwareId = targetRegistration.HardwareId ?? hardwareId,
                            StoreCode = targetRegistration.StoreCode ?? storeCode,
                            DeviceCode = targetRegistration.DeviceCode!,
                            ExpectedDeviceStatus = targetRegistration.DeviceStatus,
                            ExpectedAuthorizationCode = targetRegistration.AuthorizationCode,
                            AuthorizationCode = authorizationCode,
                            RemarkSuffix = $" | Reset by anonymous registration at {now:O}",
                            ModifiedAt = now,
                            ModifiedBy = "HBPOS_CLIENT"
                        },
                        token);
                    if (resetCount != 1)
                    {
                        throw new InvalidOperationException("Target device registration changed during registration.");
                    }

                    response = new DeviceRegisterResponse(
                        targetRegistration.DeviceCode,
                        storeCode,
                        store.StoreName,
                        PendingStatus,
                        false,
                        GetStatusMessage(PendingStatus),
                        null);
                    return;
                }

                var newRegistration = CreatePendingRegistration(
                    hardwareId,
                    storeCode,
                    terminalName,
                    now);
                await deviceRegistrationRepository.CreateRegistrationAsync(newRegistration, token);
                response = new DeviceRegisterResponse(
                    newRegistration.DeviceCode,
                    storeCode,
                    store.StoreName,
                    PendingStatus,
                    false,
                    GetStatusMessage(PendingStatus),
                    null);
            },
            cancellationToken);

        return response ?? throw new InvalidOperationException("Device registration did not produce a response.");
    }

    public async Task<DeviceVerifyResponse> VerifyAsync(
        DeviceVerifyRequest request,
        CancellationToken cancellationToken)
    {
        var deviceCode = Normalize(request.DeviceCode);
        var storeCode = Normalize(request.StoreCode);
        var hardwareId = Normalize(request.HardwareId);

        var store = await loadStoreAsync(storeCode, cancellationToken);
        if (store is null)
        {
            return CreateVerifyResponse(deviceCode, storeCode, string.Empty, UnregisteredStatus, "Store was not found or inactive.");
        }

        var device = await deviceRegistrationRepository.FindByDeviceCodeAsync(deviceCode, storeCode, cancellationToken);
        if (device is null)
        {
            return CreateVerifyResponse(deviceCode, storeCode, store.StoreName, UnregisteredStatus, "Device is not registered.");
        }

        if (!string.IsNullOrWhiteSpace(hardwareId)
            && !string.Equals(device.HardwareId, hardwareId, StringComparison.OrdinalIgnoreCase))
        {
            return CreateVerifyResponse(deviceCode, storeCode, store.StoreName, device.DeviceStatus, "Device hardware id does not match.");
        }

        return new DeviceVerifyResponse(
            deviceCode,
            storeCode,
            store.StoreName,
            device.DeviceStatus,
            device.DeviceStatus == EnabledStatus,
            GetStatusMessage(device.DeviceStatus),
            device.DeviceStatus == EnabledStatus ? device.AuthorizationCode : null);
    }

    public async Task<DeviceReregisterResponse> ReregisterAsync(
        DeviceReregisterRequest request,
        DeviceReregisterContext currentDevice,
        CancellationToken cancellationToken)
    {
        var targetStoreCode = Normalize(request.TargetStoreCode);
        var hardwareId = Normalize(request.HardwareId);
        var currentDeviceCode = Normalize(currentDevice.DeviceCode);
        var currentStoreCode = Normalize(currentDevice.StoreCode);
        var currentHardwareId = Normalize(currentDevice.HardwareId);
        var terminalName = Normalize(request.TerminalName);

        if (string.IsNullOrEmpty(targetStoreCode))
        {
            return CreateReregisterResponse(string.Empty, targetStoreCode, string.Empty, UnregisteredStatus, "targetStoreCode is required");
        }

        if (string.IsNullOrEmpty(hardwareId))
        {
            return CreateReregisterResponse(string.Empty, targetStoreCode, string.Empty, UnregisteredStatus, "hardwareId is required");
        }

        if (!string.Equals(hardwareId, currentHardwareId, StringComparison.OrdinalIgnoreCase))
        {
            return CreateReregisterResponse(currentDeviceCode, currentStoreCode, string.Empty, DisabledStatus, "Device hardware id does not match.");
        }

        if (string.Equals(targetStoreCode, currentStoreCode, StringComparison.OrdinalIgnoreCase))
        {
            return CreateReregisterResponse(currentDeviceCode, currentStoreCode, string.Empty, DisabledStatus, "Please select a different store for device reregistration.");
        }

        var store = await loadStoreAsync(targetStoreCode, cancellationToken);
        if (store is null)
        {
            return CreateReregisterResponse(string.Empty, targetStoreCode, string.Empty, UnregisteredStatus, "Store was not found or inactive.");
        }

        var now = nowProvider();
        var authorizationCode = Guid.NewGuid().ToString("N");
        var deviceCode = string.Empty;
        var disableRemark = $" | Disabled by reregistration to {targetStoreCode} at {now:O}";
        var resetRemark = string.IsNullOrWhiteSpace(terminalName)
            ? $" | Reset by reregistration from {currentStoreCode}/{currentDeviceCode} at {now:O}"
            : $" | Reset by reregistration from {currentStoreCode}/{currentDeviceCode}: {terminalName} at {now:O}";

        // 关键逻辑：目标记录查询、当前设备禁用及目标记录重置/创建必须处于同一事务，任一步并发失配都整体回滚。
        await deviceRegistrationRepository.ExecuteInTransactionAsync(
            async token =>
            {
                var targetRegistration = await deviceRegistrationRepository
                    .FindLatestByHardwareIdAndStoreCodeAsync(hardwareId, targetStoreCode, token);

                var disabledCount = await deviceRegistrationRepository.DisableActiveRegistrationAsync(
                    hardwareId,
                    currentDeviceCode,
                    currentStoreCode,
                    disableRemark,
                    token);
                if (disabledCount != 1)
                {
                    throw new InvalidOperationException("Current device registration changed during reregistration.");
                }

                if (targetRegistration is not null && !string.IsNullOrWhiteSpace(targetRegistration.DeviceCode))
                {
                    // 关键逻辑：目标分店已有记录时保留原设备号，只刷新授权并重置为待确认状态。
                    var resetCount = await deviceRegistrationRepository.ResetRegistrationForReregisterAsync(
                        new DeviceRegistrationResetForReregisterRequest
                        {
                            RegistrationId = targetRegistration.Id,
                            HardwareId = targetRegistration.HardwareId ?? hardwareId,
                            StoreCode = targetRegistration.StoreCode ?? targetStoreCode,
                            DeviceCode = targetRegistration.DeviceCode,
                            ExpectedDeviceStatus = targetRegistration.DeviceStatus,
                            ExpectedAuthorizationCode = targetRegistration.AuthorizationCode,
                            AuthorizationCode = authorizationCode,
                            RemarkSuffix = resetRemark,
                            ModifiedAt = now,
                            ModifiedBy = "HBPOS_CLIENT"
                        },
                        token);
                    if (resetCount != 1)
                    {
                        throw new InvalidOperationException("Target device registration changed during reregistration.");
                    }

                    deviceCode = targetRegistration.DeviceCode;
                    return;
                }

                // 关键逻辑：只有目标分店没有可复用设备号时，才生成新的待确认设备记录和设备号。
                var pendingRegistration = CreatePendingRegistration(
                    hardwareId,
                    targetStoreCode,
                    terminalName,
                    now,
                    authorizationCode);
                await deviceRegistrationRepository.CreateRegistrationAsync(pendingRegistration, token);
                deviceCode = pendingRegistration.DeviceCode;
            },
            cancellationToken);

        return new DeviceReregisterResponse(
            deviceCode,
            targetStoreCode,
            store.StoreName,
            PendingStatus,
            false,
            GetStatusMessage(PendingStatus),
            null);
    }

    public async Task<bool> UpdateRuntimeStatusAsync(
        string hardwareId,
        string deviceCode,
        string storeCode,
        bool isOnline,
        string? cashierId,
        string? cashierName,
        CancellationToken cancellationToken)
    {
        var normalizedHardwareId = Normalize(hardwareId);
        var normalizedDeviceCode = Normalize(deviceCode);
        var normalizedStoreCode = Normalize(storeCode);
        if (string.IsNullOrEmpty(normalizedHardwareId)
            || string.IsNullOrEmpty(normalizedDeviceCode)
            || string.IsNullOrEmpty(normalizedStoreCode))
        {
            return false;
        }

        var rows = await deviceRegistrationRepository.UpdateRuntimeStatusAsync(
            new DeviceRuntimeStatusUpdateRequest(
                normalizedHardwareId,
                normalizedDeviceCode,
                normalizedStoreCode,
                isOnline,
                NormalizeOptional(cashierId),
                NormalizeOptional(cashierName),
                nowProvider()),
            cancellationToken);
        return rows > 0;
    }

    internal static string CreateDeviceCode(string storeCode, DateTime localTime)
    {
        return $"POS_{storeCode}_{localTime:HHmm}";
    }

    private async Task<DeviceStoreInfo?> LoadStoreAsync(string storeCode, CancellationToken cancellationToken)
    {
        var context = dbContext ?? throw new InvalidOperationException("Db context is required for store lookup.");

        var store = await context.MainDb.Queryable<BlazorApp.Shared.Models.Store>()
            .FirstAsync(x => x.StoreCode == storeCode && x.IsActive && !x.IsDeleted, cancellationToken);

        return store is null
            ? null
            : new DeviceStoreInfo(store.StoreCode, store.StoreName);
    }

    private static DeviceRegistrationCreateRequest CreatePendingRegistration(
        string hardwareId,
        string storeCode,
        string terminalName,
        DateTime createdAt,
        string? authorizationCode = null)
    {
        return new DeviceRegistrationCreateRequest
        {
            HardwareId = hardwareId,
            DeviceCode = CreateDeviceCode(storeCode, createdAt),
            StoreCode = storeCode,
            DeviceStatus = PendingStatus,
            AuthorizationCode = authorizationCode ?? Guid.NewGuid().ToString("N"),
            Remark = string.IsNullOrWhiteSpace(terminalName)
                ? "HBPOS client registration"
                : $"HBPOS client registration: {terminalName}",
            CreatedAt = createdAt,
            CreatedBy = "HBPOS_CLIENT"
        };
    }

    private static DeviceRegisterResponse CreateRegisterResponse(
        string deviceCode,
        string storeCode,
        string storeName,
        int status,
        string message)
    {
        return new DeviceRegisterResponse(deviceCode, storeCode, storeName, status, false, message);
    }

    private static DeviceVerifyResponse CreateVerifyResponse(
        string deviceCode,
        string storeCode,
        string storeName,
        int status,
        string message)
    {
        return new DeviceVerifyResponse(deviceCode, storeCode, storeName, status, false, message);
    }

    private static DeviceReregisterResponse CreateReregisterResponse(
        string deviceCode,
        string storeCode,
        string storeName,
        int status,
        string message)
    {
        return new DeviceReregisterResponse(deviceCode, storeCode, storeName, status, false, message);
    }

    private static string GetStatusMessage(int status)
    {
        return status switch
        {
            PendingStatus => "Device registration is pending approval.",
            DisabledStatus => "Device is disabled.",
            EnabledStatus => "Device is enabled.",
            LockedStatus => "Device is locked.",
            UnregisteredStatus => "Device is not registered.",
            _ => "Device status is unknown."
        };
    }

    private static string Normalize(string? value)
    {
        return (value ?? string.Empty).Trim();
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = Normalize(value);
        return normalized.Length == 0 ? null : normalized;
    }
}

public sealed class SqlSugarDeviceRegistrationRepository(HbposSqlSugarContext dbContext) : IDeviceRegistrationRepository
{
    internal const string FindAllByHardwareIdForRegistrationSql = """
        SELECT
            [ID] AS Id,
            [系统设备编号] AS DeviceCode,
            [分店代码] AS StoreCode,
            [设备硬件识别码] AS HardwareId,
            [设备状态] AS DeviceStatus,
            [设备授权码] AS AuthorizationCode
        FROM [POSM_设备注册信息表] WITH (UPDLOCK, HOLDLOCK)
        WHERE [设备硬件识别码] = @HardwareId
        ORDER BY [ID] DESC;
        """;

    internal const string FindLatestByHardwareIdAndStoreCodeSql = """
        SELECT TOP 1
            [ID] AS Id,
            [系统设备编号] AS DeviceCode,
            [分店代码] AS StoreCode,
            [设备硬件识别码] AS HardwareId,
            [设备状态] AS DeviceStatus,
            [设备授权码] AS AuthorizationCode
        FROM [POSM_设备注册信息表] WITH (UPDLOCK, HOLDLOCK)
        WHERE [设备硬件识别码] = @HardwareId
          AND [分店代码] = @StoreCode
        ORDER BY [ID] DESC;
        """;

    internal const string ResetRegistrationForReregisterSql = """
        UPDATE [POSM_设备注册信息表]
        SET [设备状态] = @PendingStatus,
            [设备授权码] = @AuthorizationCode,
            [备注] = CONCAT(ISNULL([备注], ''), @RemarkSuffix),
            [最后修改时间] = @ModifiedAt,
            [最后修改人] = @ModifiedBy,
            [是否在线] = 0,
            [最后心跳时间] = NULL,
            [当前收银员ID] = NULL,
            [当前收银员姓名] = NULL,
            [收银员登录时间] = NULL
        WHERE [ID] = @RegistrationId
          AND [设备硬件识别码] = @HardwareId
          AND [分店代码] = @StoreCode
          AND [系统设备编号] = @DeviceCode
          AND [设备状态] = @ExpectedDeviceStatus
          AND (
              [设备授权码] = @ExpectedAuthorizationCode
              OR ([设备授权码] IS NULL AND @ExpectedAuthorizationCode IS NULL)
          );
        """;

    public async Task<DeviceRegistrationRecord?> FindLatestByHardwareIdAsync(
        string hardwareId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT TOP 1
                [系统设备编号] AS DeviceCode,
                [分店代码] AS StoreCode,
                [设备硬件识别码] AS HardwareId,
                [设备状态] AS DeviceStatus,
                [设备授权码] AS AuthorizationCode
            FROM [POSM_设备注册信息表]
            WHERE [设备硬件识别码] = @HardwareId
            ORDER BY [ID] DESC;
            """;

        var record = await dbContext.PosmDb.Ado.SqlQuerySingleAsync<DeviceRegistrationRecord>(
            sql,
            new SugarParameter("@HardwareId", hardwareId));

        return record;
    }

    public async Task<DeviceRegistrationRecord?> FindByDeviceCodeAsync(
        string deviceCode,
        string storeCode,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT TOP 1
                [系统设备编号] AS DeviceCode,
                [分店代码] AS StoreCode,
                [设备硬件识别码] AS HardwareId,
                [设备状态] AS DeviceStatus,
                [设备授权码] AS AuthorizationCode
            FROM [POSM_设备注册信息表]
            WHERE [系统设备编号] = @DeviceCode
              AND [分店代码] = @StoreCode;
            """;

        var record = await dbContext.PosmDb.Ado.SqlQuerySingleAsync<DeviceRegistrationRecord>(
            sql,
            new SugarParameter("@DeviceCode", deviceCode),
            new SugarParameter("@StoreCode", storeCode));

        return record;
    }

    public async Task<DeviceRegistrationRecord?> FindActiveOrLockedRegistrationAsync(
        string hardwareId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT TOP 1
                [系统设备编号] AS DeviceCode,
                [分店代码] AS StoreCode,
                [设备硬件识别码] AS HardwareId,
                [设备状态] AS DeviceStatus,
                [设备授权码] AS AuthorizationCode
            FROM [POSM_设备注册信息表]
            WHERE [设备硬件识别码] = @HardwareId
              AND [设备状态] IN (1, 2)
            ORDER BY [ID] DESC;
            """;

        var record = await dbContext.PosmDb.Ado.SqlQuerySingleAsync<DeviceRegistrationRecord>(
            sql,
            new SugarParameter("@HardwareId", hardwareId));
        return record;
    }

    public async Task<DeviceRegistrationRecord?> FindLatestByHardwareIdAndStoreCodeAsync(
        string hardwareId,
        string storeCode,
        CancellationToken cancellationToken)
    {
        return await dbContext.PosmDb.Ado.SqlQuerySingleAsync<DeviceRegistrationRecord>(
            FindLatestByHardwareIdAndStoreCodeSql,
            new SugarParameter("@HardwareId", hardwareId),
            new SugarParameter("@StoreCode", storeCode));
    }

    public async Task<IReadOnlyList<DeviceRegistrationRecord>> FindAllByHardwareIdForRegistrationAsync(
        string hardwareId,
        CancellationToken cancellationToken)
    {
        // 关键逻辑：锁定同一硬件的完整键范围，直到匿名注册事务提交或回滚，避免并发插入绕过状态检查。
        return await dbContext.PosmDb.Ado.SqlQueryAsync<DeviceRegistrationRecord>(
            FindAllByHardwareIdForRegistrationSql,
            new SugarParameter("@HardwareId", hardwareId));
    }

    public Task<int> DisablePendingRegistrationAsync(
        DeviceRegistrationDisableRequest request,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE [POSM_设备注册信息表]
            SET [设备状态] = @DisabledStatus,
                [备注] = CONCAT(ISNULL([备注], ''), @RemarkSuffix)
            WHERE [系统设备编号] = @DeviceCode
              AND [分店代码] = @StoreCode
              AND [设备硬件识别码] = @HardwareId
              AND [设备状态] = @PendingStatus;
            """;

        return dbContext.PosmDb.Ado.ExecuteCommandAsync(
            sql,
            new SugarParameter("@DisabledStatus", 0),
            new SugarParameter("@RemarkSuffix", request.RemarkSuffix),
            new SugarParameter("@DeviceCode", request.DeviceCode),
            new SugarParameter("@StoreCode", request.StoreCode),
            new SugarParameter("@HardwareId", request.HardwareId),
            new SugarParameter("@PendingStatus", -1));
    }

    public Task<int> DisableActiveRegistrationAsync(
        string hardwareId,
        string deviceCode,
        string storeCode,
        string remarkSuffix,
        CancellationToken cancellationToken)
    {
        // 关键逻辑：状态条件固定为启用，避免并发状态变化时误禁用非当前授权记录。
        const string sql = """
            UPDATE [POSM_设备注册信息表]
            SET [设备状态] = @DisabledStatus,
                [备注] = CONCAT(ISNULL([备注], ''), @RemarkSuffix)
            WHERE [系统设备编号] = @DeviceCode
              AND [分店代码] = @StoreCode
              AND [设备硬件识别码] = @HardwareId
              AND [设备状态] = @EnabledStatus;
            """;

        return dbContext.PosmDb.Ado.ExecuteCommandAsync(
            sql,
            new SugarParameter("@DisabledStatus", 0),
            new SugarParameter("@RemarkSuffix", remarkSuffix),
            new SugarParameter("@DeviceCode", deviceCode),
            new SugarParameter("@StoreCode", storeCode),
            new SugarParameter("@HardwareId", hardwareId),
            new SugarParameter("@EnabledStatus", 1));
    }

    public Task<int> ResetRegistrationForReregisterAsync(
        DeviceRegistrationResetForReregisterRequest request,
        CancellationToken cancellationToken)
    {
        // 关键逻辑：同时匹配查询快照的身份、状态和旧授权码，任何并发变化都以 0 行更新触发事务回滚。
        return dbContext.PosmDb.Ado.ExecuteCommandAsync(
            ResetRegistrationForReregisterSql,
            new SugarParameter("@PendingStatus", -1),
            new SugarParameter("@AuthorizationCode", request.AuthorizationCode),
            new SugarParameter("@RemarkSuffix", request.RemarkSuffix),
            new SugarParameter("@ModifiedAt", request.ModifiedAt),
            new SugarParameter("@ModifiedBy", request.ModifiedBy),
            new SugarParameter("@RegistrationId", request.RegistrationId),
            new SugarParameter("@HardwareId", request.HardwareId),
            new SugarParameter("@StoreCode", request.StoreCode),
            new SugarParameter("@DeviceCode", request.DeviceCode),
            new SugarParameter("@ExpectedDeviceStatus", request.ExpectedDeviceStatus),
            new SugarParameter("@ExpectedAuthorizationCode", request.ExpectedAuthorizationCode));
    }

    public Task CreateRegistrationAsync(
        DeviceRegistrationCreateRequest request,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO [POSM_设备注册信息表]
                ([设备硬件识别码], [系统设备编号], [分店代码], [设备类型], [设备系统], [设备状态], [设备授权码], [备注], [创建时间], [创建人])
            VALUES
                (@HardwareId, @DeviceCode, @StoreCode, @DeviceType, @DeviceSystem, @DeviceStatus, @AuthorizationCode, @Remark, @CreatedAt, @CreatedBy);
            """;

        return dbContext.PosmDb.Ado.ExecuteCommandAsync(
            sql,
            new SugarParameter("@HardwareId", request.HardwareId),
            new SugarParameter("@DeviceCode", request.DeviceCode),
            new SugarParameter("@StoreCode", request.StoreCode),
            new SugarParameter("@DeviceType", request.DeviceType),
            new SugarParameter("@DeviceSystem", request.DeviceSystem),
            new SugarParameter("@DeviceStatus", request.DeviceStatus),
            new SugarParameter("@AuthorizationCode", request.AuthorizationCode),
            new SugarParameter("@Remark", request.Remark),
            new SugarParameter("@CreatedAt", request.CreatedAt),
            new SugarParameter("@CreatedBy", request.CreatedBy));
    }

    public Task<int> UpdateRuntimeStatusAsync(
        DeviceRuntimeStatusUpdateRequest request,
        CancellationToken cancellationToken)
    {
        // 关键逻辑：心跳只更新当前授权设备的运行态字段；同一收银员连续上报时保留原登录时间。
        const string sql = """
            UPDATE [POSM_设备注册信息表]
            SET [是否在线] = @IsOnline,
                [最后心跳时间] = @ReportedAt,
                [收银员登录时间] = CASE
                    WHEN @HasCashier = 0 THEN NULL
                    WHEN ISNULL([当前收银员ID], '') = ISNULL(@CashierId, '')
                         AND [收银员登录时间] IS NOT NULL THEN [收银员登录时间]
                    ELSE @ReportedAt
                END,
                [当前收银员ID] = @CashierId,
                [当前收银员姓名] = @CashierName
            WHERE [设备硬件识别码] = @HardwareId
              AND [系统设备编号] = @DeviceCode
              AND [分店代码] = @StoreCode;
            """;

        var hasCashier = request.CashierId is not null || request.CashierName is not null;
        return dbContext.PosmDb.Ado.ExecuteCommandAsync(
            sql,
            new SugarParameter("@IsOnline", request.IsOnline),
            new SugarParameter("@ReportedAt", request.ReportedAt),
            new SugarParameter("@HasCashier", hasCashier ? 1 : 0),
            new SugarParameter("@CashierId", request.CashierId),
            new SugarParameter("@CashierName", request.CashierName),
            new SugarParameter("@HardwareId", request.HardwareId),
            new SugarParameter("@DeviceCode", request.DeviceCode),
            new SugarParameter("@StoreCode", request.StoreCode));
    }

    public async Task ExecuteInTransactionAsync(
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken)
    {
        await dbContext.PosmDb.Ado.BeginTranAsync();
        try
        {
            await action(cancellationToken);
            await dbContext.PosmDb.Ado.CommitTranAsync();
        }
        catch
        {
            await dbContext.PosmDb.Ado.RollbackTranAsync();
            throw;
        }
    }
}
