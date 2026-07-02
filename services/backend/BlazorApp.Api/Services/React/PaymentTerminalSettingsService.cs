using BlazorApp.Api.Data;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using SqlSugar;

namespace BlazorApp.Api.Services.React;

public sealed class PaymentTerminalSettingsService(
    POSMSqlSugarContext posmContext,
    SqlSugarContext mainContext,
    ILogger<PaymentTerminalSettingsService> logger)
{
    private static readonly string[] PaymentEnvironments = ["Production", "Sandbox"];

    public async Task<ApiResponse<PaymentTerminalSettingsDto>> GetSettingsAsync(
        string? storeCode = null,
        CancellationToken cancellationToken = default
    )
    {
        var stores = await GetStoreOptionsAsync();
        var selectedStoreCode = ResolveSelectedStoreCode(storeCode, stores);

        var settings = new PaymentTerminalSettingsDto
        {
            Square = await GetSquareStatusesAsync(),
            Stores = stores,
            SelectedStoreCode = selectedStoreCode,
            Linkly = string.IsNullOrWhiteSpace(selectedStoreCode)
                ? new List<LinklyCloudCredentialAdminDto>()
                : await GetLinklyStatusesAsync(selectedStoreCode),
        };

        logger.LogDebug("支付终端配置查询完成 StoreCode={StoreCode}", selectedStoreCode);
        return ApiResponse<PaymentTerminalSettingsDto>.OK(settings, "查询成功");
    }

    public async Task<ApiResponse<PaymentTerminalSettingsDto>> UpdateSquareTokenAsync(
        UpdateSquareTokenDto request,
        string? updatedBy,
        string? storeCode = null,
        CancellationToken cancellationToken = default
    )
    {
        var environment = NormalizeEnvironment(request.Environment);
        if (environment is null)
        {
            return ApiResponse<PaymentTerminalSettingsDto>.Error(
                "支付环境必须是 Production 或 Sandbox",
                "PAYMENT_ENVIRONMENT_INVALID"
            );
        }

        var now = DateTime.UtcNow;
        var updater = NormalizeOptional(updatedBy);
        var existingRows = await posmContext.Db.Queryable<PaymentSquareTokenRecord>()
            .Where(row => row.Environment == environment)
            .ToListAsync();

        if (request.ClearToken)
        {
            await posmContext.Db.Ado.UseTranAsync(async () =>
            {
                foreach (var row in existingRows)
                {
                    // 清除时同步置空密钥，避免被禁用行继续残留明文 token。
                    row.IsEnabled = false;
                    row.AccessToken = string.Empty;
                    row.UpdatedAt = now;
                    row.UpdatedBy = updater;
                    await posmContext.Db.Updateable(row).ExecuteCommandAsync();
                }
            });

            return await GetSettingsAsync(storeCode, cancellationToken);
        }

        var accessToken = NormalizeOptional(request.AccessToken);
        if (accessToken is null)
        {
            // 空 token 表示沿用现有配置；没有旧配置时保持未配置状态。
            return await GetSettingsAsync(storeCode, cancellationToken);
        }

        await posmContext.Db.Ado.UseTranAsync(async () =>
        {
            foreach (var row in existingRows.Where(row => row.IsEnabled))
            {
                row.IsEnabled = false;
                row.UpdatedAt = now;
                row.UpdatedBy = updater;
                await posmContext.Db.Updateable(row).ExecuteCommandAsync();
            }

            await posmContext.Db.Insertable(new PaymentSquareTokenRecord
            {
                Environment = environment,
                AccessToken = accessToken,
                IsEnabled = true,
                UpdatedAt = now,
                UpdatedBy = updater,
            }).ExecuteCommandAsync();
        });

        return await GetSettingsAsync(storeCode, cancellationToken);
    }

    public async Task<ApiResponse<PaymentTerminalSettingsDto>> UpdateLinklyCredentialAsync(
        UpdateLinklyCredentialDto request,
        string? updatedBy,
        CancellationToken cancellationToken = default
    )
    {
        var storeCode = NormalizeOptional(request.StoreCode);
        if (storeCode is null)
        {
            return ApiResponse<PaymentTerminalSettingsDto>.Error("门店编码不能为空", "LINKLY_STORE_CODE_REQUIRED");
        }

        var environment = NormalizeEnvironment(request.Environment);
        if (environment is null)
        {
            return ApiResponse<PaymentTerminalSettingsDto>.Error(
                "支付环境必须是 Production 或 Sandbox",
                "PAYMENT_ENVIRONMENT_INVALID"
            );
        }

        var existing = await QueryLinklyCredentialAsync(storeCode, environment);
        if (request.ClearCredential)
        {
            await posmContext.Db.Deleteable<PaymentLinklyCredentialRecord>()
                .Where(row => row.StoreCode == storeCode && row.Environment == environment)
                .ExecuteCommandAsync();
            return await GetSettingsAsync(storeCode, cancellationToken);
        }

        var username = NormalizeOptional(request.Username) ?? NormalizeOptional(existing?.Username);
        if (username is null)
        {
            return ApiResponse<PaymentTerminalSettingsDto>.Error("Linkly 用户名不能为空", "LINKLY_USERNAME_REQUIRED");
        }

        var password = NormalizeOptional(request.Password);
        if (password is null)
        {
            // 密码留空只允许保留旧密码；没有旧密码时必须显式输入，避免误保存不可用配置。
            password = NormalizeOptional(existing?.Password);
            if (password is null)
            {
                return ApiResponse<PaymentTerminalSettingsDto>.Error(
                    "Linkly 密码不能为空",
                    "LINKLY_PASSWORD_REQUIRED"
                );
            }
        }

        var now = DateTime.UtcNow;
        if (existing is null)
        {
            await posmContext.Db.Insertable(new PaymentLinklyCredentialRecord
            {
                StoreCode = storeCode,
                Environment = environment,
                Username = username,
                Password = password,
                UpdatedAt = now,
                UpdatedBy = NormalizeOptional(updatedBy),
            }).ExecuteCommandAsync();
        }
        else
        {
            existing.Username = username;
            existing.Password = password;
            existing.UpdatedAt = now;
            existing.UpdatedBy = NormalizeOptional(updatedBy);
            await posmContext.Db.Updateable(existing).ExecuteCommandAsync();
        }

        return await GetSettingsAsync(storeCode, cancellationToken);
    }

    private async Task<List<PaymentTerminalStoreOptionDto>> GetStoreOptionsAsync()
    {
        return await mainContext.Db.Queryable<Store>()
            .Where(store => store.IsActive && !store.IsDeleted)
            .OrderBy(store => store.StoreCode)
            .Select(store => new PaymentTerminalStoreOptionDto
            {
                StoreCode = store.StoreCode,
                StoreName = store.StoreName,
            })
            .ToListAsync();
    }

    private async Task<List<PaymentTerminalEnvironmentStatusDto>> GetSquareStatusesAsync()
    {
        var rows = await posmContext.Db.Queryable<PaymentSquareTokenRecord>()
            .Where(row => row.Environment == "Production" || row.Environment == "Sandbox")
            .ToListAsync();

        return PaymentEnvironments
            .Select(environment =>
            {
                var active = rows
                    .Where(row =>
                        row.Environment == environment
                        && row.IsEnabled
                        && !string.IsNullOrWhiteSpace(row.AccessToken)
                    )
                    .OrderByDescending(row => row.UpdatedAt)
                    .ThenByDescending(row => row.Id)
                    .FirstOrDefault();

                return new PaymentTerminalEnvironmentStatusDto
                {
                    Environment = environment,
                    Configured = active is not null,
                    Enabled = active?.IsEnabled ?? false,
                    UpdatedAtUtc = active?.UpdatedAt,
                    UpdatedBy = active?.UpdatedBy,
                };
            })
            .ToList();
    }

    private async Task<List<LinklyCloudCredentialAdminDto>> GetLinklyStatusesAsync(string storeCode)
    {
        var rows = (await posmContext.Db.Queryable<PaymentLinklyCredentialRecord>()
                .Where(row =>
                    row.StoreCode == storeCode
                    && (row.Environment == "Production" || row.Environment == "Sandbox")
                )
                .ToListAsync())
            .ToList();

        return PaymentEnvironments
            .Select(environment =>
            {
                var credential = rows
                    .Where(row => row.Environment == environment)
                    .OrderByDescending(row => row.UpdatedAt)
                    .ThenByDescending(row => row.Id)
                    .FirstOrDefault();

                return new LinklyCloudCredentialAdminDto
                {
                    StoreCode = storeCode,
                    Environment = environment,
                    Username = credential?.Username,
                    HasPassword = !string.IsNullOrWhiteSpace(credential?.Password),
                    UpdatedAtUtc = credential?.UpdatedAt,
                    UpdatedBy = credential?.UpdatedBy,
                };
            })
            .ToList();
    }

    private async Task<PaymentLinklyCredentialRecord?> QueryLinklyCredentialAsync(
        string storeCode,
        string environment
    )
    {
        return await posmContext.Db.Queryable<PaymentLinklyCredentialRecord>()
            .Where(row => row.StoreCode == storeCode && row.Environment == environment)
            .OrderByDescending(row => row.UpdatedAt)
            .OrderByDescending(row => row.Id)
            .FirstAsync();
    }

    private static string? ResolveSelectedStoreCode(
        string? requestedStoreCode,
        List<PaymentTerminalStoreOptionDto> stores
    )
    {
        var requested = NormalizeOptional(requestedStoreCode);
        if (requested is not null && stores.Any(store => store.StoreCode == requested))
        {
            return requested;
        }

        return stores.FirstOrDefault()?.StoreCode ?? requested;
    }

    private static string? NormalizeEnvironment(string? environment)
    {
        return (environment ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            "PRODUCTION" => "Production",
            "SANDBOX" => "Sandbox",
            _ => null,
        };
    }

    private static string? NormalizeOptional(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    [SugarTable("POSM_SquareToken")]
    private sealed class PaymentSquareTokenRecord
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
        public long Id { get; set; }

        public string Environment { get; set; } = string.Empty;

        public string AccessToken { get; set; } = string.Empty;

        public bool IsEnabled { get; set; }

        public DateTime UpdatedAt { get; set; }

        public string? UpdatedBy { get; set; }
    }

    [SugarTable("POSM_LinklyCloudCredential")]
    private sealed class PaymentLinklyCredentialRecord
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
        public long Id { get; set; }

        public string StoreCode { get; set; } = string.Empty;

        public string Environment { get; set; } = string.Empty;

        public string Username { get; set; } = string.Empty;

        public string Password { get; set; } = string.Empty;

        public DateTime UpdatedAt { get; set; }

        public string? UpdatedBy { get; set; }
    }
}
