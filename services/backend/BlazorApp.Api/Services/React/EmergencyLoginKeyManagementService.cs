using System.Security.Cryptography;
using System.Text.Json;
using BlazorApp.Api.Data;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models.POSM;
using Microsoft.AspNetCore.DataProtection;
using SqlSugar;

namespace BlazorApp.Api.Services.React;

public sealed class EmergencyLoginKeyManagementService
{
    internal const string PrivateKeyProtectionPurpose =
        "Hbweb.EmergencyLogin.SigningPrivateKey.v1";
    private const string LockExpectedVersionSql = """
        SELECT [StateId], [Version], [ActiveKeyId], [UpdatedAtUtc]
        FROM [dbo].[POSM_EmergencyLoginKeySetState] WITH (UPDLOCK, HOLDLOCK)
        WHERE [StateId] = 1 AND [Version] = @ExpectedVersion;
        """;
    private const string LockEnabledPosDevicesSql = """
        SELECT
            [ID] AS [DeviceRegistrationId],
            [分店代码] AS [StoreCode],
            [系统设备编号] AS [DeviceNumber],
            [设备硬件识别码] AS [HardwareId],
            [最后心跳时间] AS [LastOnlineAtUtc]
        FROM [dbo].[POSM_设备注册信息表] WITH (UPDLOCK, HOLDLOCK)
        WHERE [设备状态] = 1 AND [设备类型] = N'POS';
        """;
    private const string LockDeviceAcknowledgementsSql = """
        SELECT
            [DeviceRegistrationId],
            [KeySetVersion],
            [KeyId],
            [AcknowledgedAtUtc],
            [LastSeenAtUtc],
            CASE
                WHEN [KeySetVersion] = @KeySetVersion AND [KeyId] = @KeyId THEN 1
                ELSE 0
            END AS [IsCurrent]
        FROM [dbo].[POSM_EmergencyLoginKeyDeviceSync] WITH (UPDLOCK, HOLDLOCK);
        """;
    private const string LockKeyByIdSql = """
        SELECT
            [KeyId], [Status], [PublicKeyPem], [PublicKeyFingerprint], [ProtectedPrivateKey],
            [CreatedAtUtc], [CreatedBy], [CreatedReason], [ActivatedAtUtc], [ActivatedBy],
            [RetiredAtUtc], [RetiredBy], [UpdatedAtUtc]
        FROM [dbo].[POSM_EmergencyLoginKey] WITH (UPDLOCK, HOLDLOCK)
        WHERE [KeyId] = @KeyId;
        """;
    private const string LockLiveGrantsForRetireSql = """
        SELECT [GrantId]
        FROM [dbo].[POSM_EmergencyLoginGrant] WITH (UPDLOCK, HOLDLOCK)
        WHERE [KeyId] = @KeyId
          AND [RevokedAtUtc] IS NULL
          AND [ExpiresAtUtc] > @UtcNow;
        """;
    internal static IReadOnlyList<string> ActivationLockSqlForTests { get; } =
    [
        LockExpectedVersionSql,
        LockEnabledPosDevicesSql,
        LockDeviceAcknowledgementsSql,
    ];
    internal static IReadOnlyList<string> RetireLockSqlForTests { get; } =
    [
        LockKeyByIdSql,
        LockLiveGrantsForRetireSql,
    ];
    private const int MaxReasonLength = 200;
    private const int MaxActorLength = 128;
    private readonly ISqlSugarClient _db;
    private readonly IDataProtector _privateKeyProtector;
    private readonly ILogger<EmergencyLoginKeyManagementService> _logger;
    private readonly TimeProvider _timeProvider;

    public EmergencyLoginKeyManagementService(
        POSMSqlSugarContext context,
        IDataProtectionProvider dataProtectionProvider,
        ILogger<EmergencyLoginKeyManagementService> logger,
        TimeProvider? timeProvider = null
    )
    {
        _db = context.Db;
        _privateKeyProtector = dataProtectionProvider.CreateProtector(PrivateKeyProtectionPurpose);
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<ApiResponse<EmergencyLoginKeyListDto>> ListAsync()
    {
        var state = await GetStateAsync();
        var keys = await _db.Queryable<EmergencyLoginKeyEntity>()
            .OrderByDescending(item => item.CreatedAtUtc)
            .ToListAsync();
        var coverageKeyId = keys.FirstOrDefault(item => item.Status == EmergencyLoginKeyStatus.Staged)
            ?.KeyId ?? state.ActiveKeyId;
        var coverage = await GetCoverageAsync(state.Version, coverageKeyId);
        var (healthy, status) = CheckDataProtectionHealth(keys);

        return ApiResponse<EmergencyLoginKeyListDto>.OK(new EmergencyLoginKeyListDto
        {
            ActiveKeyId = state.ActiveKeyId,
            CoverageKeyId = coverageKeyId,
            Version = state.Version,
            DataProtectionHealthy = healthy,
            DataProtectionStatus = status,
            Keys = keys.Select(Map).ToList(),
            Coverage = new EmergencyLoginKeyCoverageDto
            {
                TotalDevices = coverage.TotalDevices,
                AcknowledgedDevices = coverage.AcknowledgedDevices,
            },
            MissingDevices = coverage.MissingDevices,
        });
    }

    public async Task<ApiResponse<EmergencyLoginKeyMutationDto>> GenerateAsync(
        EmergencyLoginKeyGenerateRequestDto request,
        string actor
    )
    {
        if (!request.ExpectedVersion.HasValue)
        {
            return ExpectedVersionRequired();
        }

        var expectedVersion = request.ExpectedVersion.Value;
        var reason = NormalizeReason(request.Reason);
        if (reason == null)
        {
            return InvalidReason("生成");
        }

        var currentState = await GetStateAsync();
        if (currentState.Version != expectedVersion)
        {
            return VersionConflict(currentState.Version);
        }

        if (await _db.Queryable<EmergencyLoginKeyEntity>()
                .AnyAsync(item => item.Status == EmergencyLoginKeyStatus.Staged))
        {
            return ApiResponse<EmergencyLoginKeyMutationDto>.Error(
                "已有待激活密钥，请先激活或废弃",
                "EMERGENCY_KEY_STAGED_ALREADY_EXISTS"
            );
        }

        var now = UtcNow();
        var normalizedActor = NormalizeActor(actor);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var keyId = CreateKeyId(now);
        var publicKeyBytes = key.ExportSubjectPublicKeyInfo();
        var publicKeyPem = key.ExportSubjectPublicKeyInfoPem();
        var fingerprint = Convert.ToHexString(SHA256.HashData(publicKeyBytes));
        string protectedPrivateKey;
        try
        {
            // 关键逻辑：私钥只在内存中短暂停留，入库前必须使用固定 purpose 的 Data Protection 加密。
            protectedPrivateKey = _privateKeyProtector.Protect(key.ExportPkcs8PrivateKeyPem());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "紧急登录密钥生成时 Data Protection 不可用，KeyId={KeyId}", keyId);
            return ApiResponse<EmergencyLoginKeyMutationDto>.Error(
                "Data Protection 当前不可用，未生成紧急登录密钥",
                "EMERGENCY_KEY_DATA_PROTECTION_UNAVAILABLE"
            );
        }

        var entity = new EmergencyLoginKeyEntity
        {
            KeyId = keyId,
            Status = EmergencyLoginKeyStatus.Staged,
            PublicKeyPem = publicKeyPem,
            PublicKeyFingerprint = fingerprint,
            ProtectedPrivateKey = protectedPrivateKey,
            CreatedAtUtc = now,
            CreatedBy = normalizedActor,
            CreatedReason = reason,
            UpdatedAtUtc = now,
        };
        var resultVersion = expectedVersion + 1;
        var transaction = await _db.Ado.UseTranAsync(async () =>
        {
            await AdvanceVersionAsync(
                expectedVersion,
                resultVersion,
                currentState.ActiveKeyId,
                now
            );
            await _db.Insertable(entity).ExecuteCommandAsync();
            await InsertAuditAsync(
                keyId,
                "Generate",
                normalizedActor,
                reason,
                expectedVersion,
                resultVersion,
                null,
                now
            );
        });
        if (!transaction.IsSuccess)
        {
            return HandleMutationFailure(transaction.ErrorException, "生成");
        }

        _logger.LogWarning(
            "已生成紧急登录待激活密钥，KeyId={KeyId}, Version={Version}, Actor={Actor}",
            keyId,
            resultVersion,
            normalizedActor
        );
        return ApiResponse<EmergencyLoginKeyMutationDto>.OK(new EmergencyLoginKeyMutationDto
        {
            Version = resultVersion,
            ActiveKeyId = currentState.ActiveKeyId,
            Key = Map(entity),
        }, "紧急登录密钥已生成并进入待激活状态");
    }

    public async Task<ApiResponse<EmergencyLoginKeyMutationDto>> ActivateAsync(
        string keyId,
        EmergencyLoginKeyActivateRequestDto request,
        string actor
    )
    {
        if (!request.ExpectedVersion.HasValue)
        {
            return ExpectedVersionRequired();
        }

        var expectedVersion = request.ExpectedVersion.Value;
        var reason = NormalizeReason(request.Reason);
        if (reason == null)
        {
            return InvalidReason("激活");
        }

        var state = await GetStateAsync();
        if (state.Version != expectedVersion)
        {
            return VersionConflict(state.Version);
        }

        var staged = await _db.Queryable<EmergencyLoginKeyEntity>()
            .FirstAsync(item => item.KeyId == keyId && item.Status == EmergencyLoginKeyStatus.Staged);
        if (staged == null)
        {
            return ApiResponse<EmergencyLoginKeyMutationDto>.Error(
                "指定密钥不存在或不处于待激活状态",
                "EMERGENCY_KEY_NOT_STAGED"
            );
        }

        var now = UtcNow();
        var normalizedActor = NormalizeActor(actor);
        var resultVersion = expectedVersion + 1;
        CoverageResult? coverage = null;
        var transaction = await _db.Ado.UseTranAsync(async () =>
        {
            // 关键逻辑：版本、启用 POS 范围和 ACK 范围必须在同一事务内锁定并重算，关闭检查后启用设备的竞态窗口。
            await LockExpectedVersionAsync(expectedVersion);
            var lockedStaged = await LockKeyByIdAsync(staged.KeyId);
            if (lockedStaged?.Status != EmergencyLoginKeyStatus.Staged)
            {
                throw new EmergencyLoginKeyConcurrencyException();
            }

            // 状态切换前在持锁事务内完整验证私钥、公钥、曲线和指纹，任何损坏都回滚。
            EmergencyLoginKeyMaterialValidator.ValidateAndUnprotect(
                lockedStaged,
                _privateKeyProtector
            );
            coverage = await GetLockedActivationCoverageAsync(expectedVersion, lockedStaged.KeyId);
            if (coverage.MissingDevices.Count > 0 && !request.Force)
            {
                throw new EmergencyLoginKeyCoverageException(coverage.MissingDevices);
            }

            var auditDetails = coverage.MissingDevices.Count == 0
                ? null
                : JsonSerializer.Serialize(coverage.MissingDevices.Select(device => new
                {
                    device.DeviceRegistrationId,
                    device.StoreCode,
                    device.DeviceNumber,
                    device.LastOnlineAtUtc,
                    device.LastSyncAtUtc,
                }));
            await AdvanceVersionAsync(expectedVersion, resultVersion, staged.KeyId, now);
            // 关键逻辑：先让旧活动密钥进入退役中，再激活新密钥，满足活动密钥唯一索引。
            await _db.Updateable<EmergencyLoginKeyEntity>()
                .SetColumns(item => item.Status == EmergencyLoginKeyStatus.Retiring)
                .SetColumns(item => item.UpdatedAtUtc == now)
                .Where(item => item.Status == EmergencyLoginKeyStatus.Active)
                .ExecuteCommandAsync();
            var activatedRows = await _db.Updateable<EmergencyLoginKeyEntity>()
                .SetColumns(item => item.Status == EmergencyLoginKeyStatus.Active)
                .SetColumns(item => item.ActivatedAtUtc == now)
                .SetColumns(item => item.ActivatedBy == normalizedActor)
                .SetColumns(item => item.UpdatedAtUtc == now)
                .Where(item => item.KeyId == staged.KeyId && item.Status == EmergencyLoginKeyStatus.Staged)
                .ExecuteCommandAsync();
            if (activatedRows != 1)
            {
                throw new EmergencyLoginKeyConcurrencyException();
            }

            await InsertAuditAsync(
                staged.KeyId,
                request.Force ? "ForceActivate" : "Activate",
                normalizedActor,
                reason,
                expectedVersion,
                resultVersion,
                auditDetails,
                now
            );
        });
        if (!transaction.IsSuccess)
        {
            if (transaction.ErrorException is EmergencyLoginKeyMaterialException materialException)
            {
                _logger.LogError(
                    materialException,
                    "紧急登录待激活密钥材料校验失败，KeyId={KeyId}",
                    staged.KeyId
                );
                return ApiResponse<EmergencyLoginKeyMutationDto>.Error(
                    "待激活密钥材料无效，激活已回滚",
                    "EMERGENCY_KEY_MATERIAL_INVALID"
                );
            }

            if (transaction.ErrorException is EmergencyLoginKeyCoverageException coverageException)
            {
                return ApiResponse<EmergencyLoginKeyMutationDto>.Error(
                    "仍有已启用 POS 设备未确认当前密钥版本",
                    "EMERGENCY_KEY_DEVICE_ACK_INCOMPLETE",
                    coverageException.MissingDevices
                );
            }

            return HandleMutationFailure(transaction.ErrorException, "激活");
        }

        coverage ??= new CoverageResult(0, 0, []);
        staged.Status = EmergencyLoginKeyStatus.Active;
        staged.ActivatedAtUtc = now;
        staged.ActivatedBy = normalizedActor;
        _logger.LogWarning(
            "已激活紧急登录密钥，KeyId={KeyId}, Version={Version}, Force={Force}, MissingDeviceCount={MissingDeviceCount}, Actor={Actor}",
            staged.KeyId,
            resultVersion,
            request.Force,
            coverage.MissingDevices.Count,
            normalizedActor
        );
        return ApiResponse<EmergencyLoginKeyMutationDto>.OK(new EmergencyLoginKeyMutationDto
        {
            Version = resultVersion,
            ActiveKeyId = staged.KeyId,
            Key = Map(staged),
        }, request.Force ? "紧急登录密钥已强制激活" : "紧急登录密钥已激活");
    }

    public async Task<ApiResponse<EmergencyLoginKeyMutationDto>> RetireAsync(
        string keyId,
        EmergencyLoginKeyRetireRequestDto request,
        string actor
    )
    {
        if (!request.ExpectedVersion.HasValue)
        {
            return ExpectedVersionRequired();
        }

        var expectedVersion = request.ExpectedVersion.Value;
        var reason = NormalizeReason(request.Reason);
        if (reason == null)
        {
            return InvalidReason("退役");
        }

        var state = await GetStateAsync();
        if (state.Version != expectedVersion)
        {
            return VersionConflict(state.Version);
        }

        var entity = await _db.Queryable<EmergencyLoginKeyEntity>()
            .FirstAsync(item => item.KeyId == keyId);
        if (entity == null)
        {
            return ApiResponse<EmergencyLoginKeyMutationDto>.Error(
                "指定紧急登录密钥不存在",
                "EMERGENCY_KEY_NOT_FOUND"
            );
        }

        if (entity.Status is not (EmergencyLoginKeyStatus.Staged or EmergencyLoginKeyStatus.Retiring))
        {
            return ApiResponse<EmergencyLoginKeyMutationDto>.Error(
                "仅待激活或退役中的密钥可以退役",
                "EMERGENCY_KEY_RETIRE_STATE_INVALID"
            );
        }

        var now = UtcNow();
        var normalizedActor = NormalizeActor(actor);
        var resultVersion = expectedVersion + 1;
        var transaction = await _db.Ado.UseTranAsync(async () =>
        {
            // 锁序固定为版本状态 -> 密钥行 -> grant 范围，避免与激活并发形成环路。
            await LockExpectedVersionAsync(expectedVersion);
            var lockedKey = await LockKeyByIdAsync(entity.KeyId);
            if (lockedKey?.Status is not (EmergencyLoginKeyStatus.Staged or EmergencyLoginKeyStatus.Retiring))
            {
                throw new EmergencyLoginKeyConcurrencyException();
            }

            if (lockedKey.Status == EmergencyLoginKeyStatus.Retiring
                && await HasLockedLiveGrantAsync(lockedKey.KeyId, now))
            {
                throw new EmergencyLoginKeyLiveGrantException();
            }

            await AdvanceVersionAsync(expectedVersion, resultVersion, state.ActiveKeyId, now);
            var retiredRows = await _db.Updateable<EmergencyLoginKeyEntity>()
                .SetColumns(item => item.Status == EmergencyLoginKeyStatus.Retired)
                // 关键逻辑：退役只做逻辑保留，并永久清除加密私钥，审计记录仍可追溯。
                .SetColumns(item => item.ProtectedPrivateKey == null)
                .SetColumns(item => item.RetiredAtUtc == now)
                .SetColumns(item => item.RetiredBy == normalizedActor)
                .SetColumns(item => item.UpdatedAtUtc == now)
                .Where(item => item.KeyId == entity.KeyId
                    && (item.Status == EmergencyLoginKeyStatus.Staged
                        || item.Status == EmergencyLoginKeyStatus.Retiring))
                .ExecuteCommandAsync();
            if (retiredRows != 1)
            {
                throw new EmergencyLoginKeyConcurrencyException();
            }

            await InsertAuditAsync(
                entity.KeyId,
                entity.Status == EmergencyLoginKeyStatus.Staged ? "DiscardStaged" : "Retire",
                normalizedActor,
                reason,
                expectedVersion,
                resultVersion,
                null,
                now
            );
        });
        if (!transaction.IsSuccess)
        {
            if (transaction.ErrorException is EmergencyLoginKeyLiveGrantException)
            {
                return ApiResponse<EmergencyLoginKeyMutationDto>.Error(
                    "该密钥仍有未撤销且未过期的紧急登录授权，暂不能退役",
                    "EMERGENCY_KEY_ACTIVE_GRANTS_EXIST"
                );
            }

            return HandleMutationFailure(transaction.ErrorException, "退役");
        }

        entity.Status = EmergencyLoginKeyStatus.Retired;
        entity.ProtectedPrivateKey = null;
        entity.RetiredAtUtc = now;
        entity.RetiredBy = normalizedActor;
        return ApiResponse<EmergencyLoginKeyMutationDto>.OK(new EmergencyLoginKeyMutationDto
        {
            Version = resultVersion,
            ActiveKeyId = state.ActiveKeyId,
            Key = Map(entity),
        }, "紧急登录密钥已退役");
    }

    private async Task<EmergencyLoginKeySetStateEntity> GetStateAsync() =>
        await _db.Queryable<EmergencyLoginKeySetStateEntity>()
            .FirstAsync(item => item.StateId == 1)
        ?? throw new InvalidOperationException("紧急登录密钥版本状态未初始化");

    private async Task AdvanceVersionAsync(
        long expectedVersion,
        long resultVersion,
        string? activeKeyId,
        DateTime now
    )
    {
        var rows = await _db.Updateable<EmergencyLoginKeySetStateEntity>()
            .SetColumns(item => item.Version == resultVersion)
            .SetColumns(item => item.ActiveKeyId == activeKeyId)
            .SetColumns(item => item.UpdatedAtUtc == now)
            .Where(item => item.StateId == 1 && item.Version == expectedVersion)
            .ExecuteCommandAsync();
        if (rows != 1)
        {
            throw new EmergencyLoginKeyConcurrencyException();
        }
    }

    private async Task<CoverageResult> GetCoverageAsync(long version, string? keyId)
    {
        var devices = await _db.Queryable<POSM_设备注册信息表>()
            .Where(device => device.设备状态 == 1 && device.设备类型 == "POS")
            .ToListAsync();
        var syncRows = await _db.Queryable<EmergencyLoginKeyDeviceSyncEntity>().ToListAsync();
        return BuildCoverage(
            devices.Select(device => new ActivationDeviceRow
            {
                DeviceRegistrationId = device.ID,
                StoreCode = device.分店代码,
                DeviceNumber = device.系统设备编号,
                HardwareId = device.设备硬件识别码,
                LastOnlineAtUtc = device.最后心跳时间,
            }).ToList(),
            syncRows.Select(sync => new ActivationSyncRow
            {
                DeviceRegistrationId = sync.DeviceRegistrationId,
                KeySetVersion = sync.KeySetVersion,
                KeyId = sync.KeyId,
                AcknowledgedAtUtc = sync.AcknowledgedAtUtc,
                LastSeenAtUtc = sync.LastSeenAtUtc,
                IsCurrent = sync.KeySetVersion == version && sync.KeyId == keyId ? 1 : 0,
            }).ToList(),
            version,
            keyId
        );
    }

    private async Task LockExpectedVersionAsync(long expectedVersion)
    {
        if (_db.CurrentConnectionConfig.DbType == DbType.SqlServer)
        {
            var rows = await _db.Ado.SqlQueryAsync<EmergencyLoginKeySetStateEntity>(
                LockExpectedVersionSql,
                new SugarParameter("@ExpectedVersion", expectedVersion)
            );
            if (rows.Count != 1)
            {
                throw new EmergencyLoginKeyConcurrencyException();
            }

            return;
        }

        var state = await GetStateAsync();
        if (state.Version != expectedVersion)
        {
            throw new EmergencyLoginKeyConcurrencyException();
        }
    }

    private async Task<CoverageResult> GetLockedActivationCoverageAsync(
        long version,
        string keyId
    )
    {
        if (_db.CurrentConnectionConfig.DbType != DbType.SqlServer)
        {
            return await GetCoverageAsync(version, keyId);
        }

        var devices = await _db.Ado.SqlQueryAsync<ActivationDeviceRow>(
            LockEnabledPosDevicesSql
        );
        var syncRows = await _db.Ado.SqlQueryAsync<ActivationSyncRow>(
            LockDeviceAcknowledgementsSql,
            new SugarParameter("@KeySetVersion", version),
            new SugarParameter("@KeyId", keyId)
        );
        return BuildCoverage(devices, syncRows, version, keyId);
    }

    private async Task<EmergencyLoginKeyEntity?> LockKeyByIdAsync(string keyId)
    {
        if (_db.CurrentConnectionConfig.DbType == DbType.SqlServer)
        {
            return (await _db.Ado.SqlQueryAsync<EmergencyLoginKeyEntity>(
                LockKeyByIdSql,
                new SugarParameter("@KeyId", keyId)
            )).SingleOrDefault();
        }

        return await _db.Queryable<EmergencyLoginKeyEntity>()
            .FirstAsync(item => item.KeyId == keyId);
    }

    private async Task<bool> HasLockedLiveGrantAsync(string keyId, DateTime utcNow)
    {
        if (_db.CurrentConnectionConfig.DbType == DbType.SqlServer)
        {
            return (await _db.Ado.SqlQueryAsync<GrantIdRow>(
                LockLiveGrantsForRetireSql,
                new SugarParameter("@KeyId", keyId),
                new SugarParameter("@UtcNow", utcNow)
            )).Count > 0;
        }

        return await _db.Queryable<EmergencyLoginGrantEntity>().AnyAsync(grant =>
            grant.KeyId == keyId
            && grant.RevokedAtUtc == null
            && grant.ExpiresAtUtc > utcNow);
    }

    private static CoverageResult BuildCoverage(
        List<ActivationDeviceRow> devices,
        List<ActivationSyncRow> syncRows,
        long version,
        string? keyId
    )
    {
        var acknowledgedIds = string.IsNullOrWhiteSpace(keyId)
            ? []
            : syncRows
                .Where(sync => sync.IsCurrent == 1
                    || (sync.KeySetVersion == version && sync.KeyId == keyId))
                .Select(sync => sync.DeviceRegistrationId)
                .ToHashSet();
        var lastSyncByDevice = syncRows
            .GroupBy(sync => sync.DeviceRegistrationId)
            .ToDictionary(
                group => group.Key,
                group => group.Max(sync => (DateTime?)sync.AcknowledgedAtUtc)
            );
        var missing = devices
            .Where(device => !acknowledgedIds.Contains(device.DeviceRegistrationId))
            .Select(device => new EmergencyLoginKeyMissingDeviceDto
            {
                DeviceRegistrationId = device.DeviceRegistrationId,
                StoreCode = device.StoreCode,
                DeviceNumber = device.DeviceNumber,
                HardwareId = device.HardwareId,
                LastOnlineAtUtc = device.LastOnlineAtUtc,
                LastSyncAtUtc = lastSyncByDevice.GetValueOrDefault(device.DeviceRegistrationId),
            })
            .OrderBy(device => device.StoreCode)
            .ThenBy(device => device.DeviceNumber)
            .ToList();
        return new CoverageResult(devices.Count, devices.Count - missing.Count, missing);
    }

    private (bool Healthy, string Status) CheckDataProtectionHealth(
        IEnumerable<EmergencyLoginKeyEntity> keys
    )
    {
        try
        {
            var probe = Guid.NewGuid().ToString("N");
            var protectedProbe = _privateKeyProtector.Protect(probe);
            if (_privateKeyProtector.Unprotect(protectedProbe) != probe)
            {
                return (false, "RoundTripFailed");
            }

            // 关键逻辑：健康检查还必须验证现存可签名密钥，防止 key ring 丢失后新探针仍误报健康。
            foreach (var key in keys.Where(item =>
                         item.Status != EmergencyLoginKeyStatus.Retired
                         && !string.IsNullOrWhiteSpace(item.ProtectedPrivateKey)))
            {
                try
                {
                    EmergencyLoginKeyMaterialValidator.ValidateAndUnprotect(
                        key,
                        _privateKeyProtector
                    );
                }
                catch (Exception ex) when (
                    ex is CryptographicException or EmergencyLoginKeyMaterialException
                )
                {
                    _logger.LogError(
                        ex,
                        "紧急登录已存密钥无法通过 Data Protection 健康检查，KeyId={KeyId}",
                        key.KeyId
                    );
                    return (false, "StoredKeyDecryptFailed");
                }
            }

            return (true, "Healthy");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "紧急登录密钥 Data Protection 健康检查失败");
            return (false, "Unavailable");
        }
    }

    private Task InsertAuditAsync(
        string? keyId,
        string action,
        string actor,
        string reason,
        long expectedVersion,
        long resultVersion,
        string? details,
        DateTime now
    ) => _db.Insertable(new EmergencyLoginKeyAuditEntity
    {
        KeyId = keyId,
        Action = action,
        Actor = actor,
        Reason = reason,
        ExpectedVersion = expectedVersion,
        ResultVersion = resultVersion,
        Details = details,
        CreatedAtUtc = now,
    }).ExecuteCommandAsync();

    private static string CreateKeyId(DateTime utcNow) =>
        $"K{utcNow:yyyyMMddHHmmss}{Convert.ToHexString(RandomNumberGenerator.GetBytes(8))}";

    private static EmergencyLoginKeyDto Map(EmergencyLoginKeyEntity entity) => new()
    {
        KeyId = entity.KeyId,
        Status = entity.Status,
        PublicKeyPem = entity.PublicKeyPem,
        PublicKeyFingerprint = entity.PublicKeyFingerprint,
        CreatedAtUtc = entity.CreatedAtUtc,
        CreatedBy = entity.CreatedBy,
        CreatedReason = entity.CreatedReason,
        ActivatedAtUtc = entity.ActivatedAtUtc,
        ActivatedBy = entity.ActivatedBy,
        RetiredAtUtc = entity.RetiredAtUtc,
        RetiredBy = entity.RetiredBy,
    };

    private static string? NormalizeReason(string? reason)
    {
        var normalized = reason?.Trim();
        return string.IsNullOrWhiteSpace(normalized) || normalized.Length > MaxReasonLength
            ? null
            : normalized;
    }

    private static string NormalizeActor(string? actor)
    {
        var normalized = string.IsNullOrWhiteSpace(actor) ? "System" : actor.Trim();
        return normalized.Length <= MaxActorLength ? normalized : normalized[..MaxActorLength];
    }

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;

    private static ApiResponse<EmergencyLoginKeyMutationDto> InvalidReason(string action) =>
        ApiResponse<EmergencyLoginKeyMutationDto>.Error(
            $"{action}原因不能为空且不能超过 {MaxReasonLength} 个字符",
            "EMERGENCY_KEY_REASON_INVALID"
        );

    private static ApiResponse<EmergencyLoginKeyMutationDto> ExpectedVersionRequired() =>
        ApiResponse<EmergencyLoginKeyMutationDto>.Error(
            "expectedVersion 不能为空",
            "EMERGENCY_KEY_EXPECTED_VERSION_REQUIRED"
        );

    private static ApiResponse<EmergencyLoginKeyMutationDto> VersionConflict(long actualVersion) =>
        ApiResponse<EmergencyLoginKeyMutationDto>.Error(
            "紧急登录密钥版本已变化，请刷新后重试",
            "EMERGENCY_KEY_VERSION_CONFLICT",
            new { ActualVersion = actualVersion }
        );

    private ApiResponse<EmergencyLoginKeyMutationDto> HandleMutationFailure(
        Exception? exception,
        string action
    )
    {
        if (exception is EmergencyLoginKeyConcurrencyException)
        {
            _logger.LogWarning("紧急登录密钥{Action}发生版本并发冲突", action);
            return VersionConflict(-1);
        }

        _logger.LogError(exception, "紧急登录密钥{Action}事务失败", action);
        return ApiResponse<EmergencyLoginKeyMutationDto>.Error(
            $"紧急登录密钥{action}失败",
            "EMERGENCY_KEY_OPERATION_FAILED"
        );
    }

    private sealed record CoverageResult(
        int TotalDevices,
        int AcknowledgedDevices,
        List<EmergencyLoginKeyMissingDeviceDto> MissingDevices
    );

    private sealed class ActivationDeviceRow
    {
        public int DeviceRegistrationId { get; set; }
        public string? StoreCode { get; set; }
        public string DeviceNumber { get; set; } = string.Empty;
        public string HardwareId { get; set; } = string.Empty;
        public DateTime? LastOnlineAtUtc { get; set; }
    }

    private sealed class ActivationSyncRow
    {
        public int DeviceRegistrationId { get; set; }
        public long KeySetVersion { get; set; }
        public string KeyId { get; set; } = string.Empty;
        public DateTime AcknowledgedAtUtc { get; set; }
        public DateTime? LastSeenAtUtc { get; set; }
        public int IsCurrent { get; set; }
    }

    private sealed class GrantIdRow
    {
        public Guid GrantId { get; set; }
    }

    private sealed class EmergencyLoginKeyCoverageException(
        List<EmergencyLoginKeyMissingDeviceDto> missingDevices
    ) : Exception
    {
        internal List<EmergencyLoginKeyMissingDeviceDto> MissingDevices { get; } = missingDevices;
    }

    private sealed class EmergencyLoginKeyConcurrencyException : Exception;
    private sealed class EmergencyLoginKeyLiveGrantException : Exception;
}

internal static class EmergencyLoginKeyMaterialValidator
{
    internal static string ValidateAndUnprotect(
        EmergencyLoginKeyEntity keyEntity,
        IDataProtector protector
    )
    {
        if (string.IsNullOrWhiteSpace(keyEntity.ProtectedPrivateKey))
        {
            throw new EmergencyLoginKeyMaterialException("受保护私钥为空");
        }

        string privateKeyPem;
        try
        {
            privateKeyPem = protector.Unprotect(keyEntity.ProtectedPrivateKey);
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            throw new EmergencyLoginKeyMaterialException(
                "Data Protection 私钥解密失败",
                ex,
                isDataProtectionFailure: true
            );
        }

        try
        {
            using var privateKey = ECDsa.Create();
            privateKey.ImportFromPem(privateKeyPem);
            EnsureP256(privateKey);
            var privateSpki = privateKey.ExportSubjectPublicKeyInfo();

            using var storedPublicKey = ECDsa.Create();
            storedPublicKey.ImportFromPem(keyEntity.PublicKeyPem);
            EnsureP256(storedPublicKey);
            var storedSpki = storedPublicKey.ExportSubjectPublicKeyInfo();
            if (!CryptographicOperations.FixedTimeEquals(privateSpki, storedSpki))
            {
                throw new EmergencyLoginKeyMaterialException("公私钥不匹配");
            }

            var fingerprint = Convert.ToHexString(SHA256.HashData(privateSpki));
            if (!string.Equals(
                    fingerprint,
                    keyEntity.PublicKeyFingerprint,
                    StringComparison.OrdinalIgnoreCase
                ))
            {
                throw new EmergencyLoginKeyMaterialException("公钥指纹不匹配");
            }

            return privateKeyPem;
        }
        catch (EmergencyLoginKeyMaterialException)
        {
            throw;
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException or FormatException)
        {
            throw new EmergencyLoginKeyMaterialException("密钥 PEM 或 Data Protection 密文无效", ex);
        }
    }

    private static void EnsureP256(ECDsa key)
    {
        var curve = key.ExportParameters(false).Curve.Oid;
        var p256 = ECCurve.NamedCurves.nistP256.Oid;
        if (key.KeySize != 256
            || (!string.Equals(curve.Value, p256.Value, StringComparison.Ordinal)
                && !string.Equals(curve.FriendlyName, p256.FriendlyName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new EmergencyLoginKeyMaterialException("签名密钥不是 ECDSA P-256");
        }
    }
}

internal sealed class EmergencyLoginKeyMaterialException : Exception
{
    internal EmergencyLoginKeyMaterialException(
        string message,
        Exception? innerException = null,
        bool isDataProtectionFailure = false
    )
        : base(message, innerException)
    {
        IsDataProtectionFailure = isDataProtectionFailure;
    }

    internal bool IsDataProtectionFailure { get; }
}

internal static class EmergencyLoginKeyStatus
{
    internal const string Staged = "Staged";
    internal const string Active = "Active";
    internal const string Retiring = "Retiring";
    internal const string Retired = "Retired";
}

[SugarTable("POSM_EmergencyLoginKey")]
internal sealed class EmergencyLoginKeyEntity
{
    [SugarColumn(IsPrimaryKey = true)]
    public string KeyId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string PublicKeyPem { get; set; } = string.Empty;
    public string PublicKeyFingerprint { get; set; } = string.Empty;
    public string? ProtectedPrivateKey { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string CreatedReason { get; set; } = string.Empty;
    public DateTime? ActivatedAtUtc { get; set; }
    public string? ActivatedBy { get; set; }
    public DateTime? RetiredAtUtc { get; set; }
    public string? RetiredBy { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

[SugarTable("POSM_EmergencyLoginKeySetState")]
internal sealed class EmergencyLoginKeySetStateEntity
{
    [SugarColumn(IsPrimaryKey = true)]
    public int StateId { get; set; }
    public long Version { get; set; }
    public string? ActiveKeyId { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

[SugarTable("POSM_EmergencyLoginKeyDeviceSync")]
internal sealed class EmergencyLoginKeyDeviceSyncEntity
{
    [SugarColumn(IsPrimaryKey = true)]
    public int DeviceRegistrationId { get; set; }
    [SugarColumn(IsPrimaryKey = true)]
    public long KeySetVersion { get; set; }
    public string KeyId { get; set; } = string.Empty;
    public DateTime AcknowledgedAtUtc { get; set; }
    public DateTime? LastSeenAtUtc { get; set; }
}

[SugarTable("POSM_EmergencyLoginKeyAudit")]
internal sealed class EmergencyLoginKeyAuditEntity
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public long AuditId { get; set; }
    public string? KeyId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Actor { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public long ExpectedVersion { get; set; }
    public long ResultVersion { get; set; }
    public string? Details { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
