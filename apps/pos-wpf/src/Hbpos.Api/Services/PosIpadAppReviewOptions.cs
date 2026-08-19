using BlazorApp.Shared.Models;
using Hbpos.Api.Data;

namespace Hbpos.Api.Services;

public interface IPosIpadAppReviewAuthorizationBoundary
{
    Task<bool> IsReviewDeviceAsync(
        string storeCode,
        string deviceCode,
        string hardwareId,
        CancellationToken cancellationToken);

    Task<bool> IsActiveEmployeeCashierAsync(
        string cashierId,
        string userGuid,
        CancellationToken cancellationToken);
}

public sealed class PosIpadAppReviewAuthorizationBoundary(
    IDeviceRegistrationRepository deviceRegistrationRepository,
    HbposSqlSugarContext dbContext) : IPosIpadAppReviewAuthorizationBoundary
{
    public Task<bool> IsReviewDeviceAsync(
        string storeCode,
        string deviceCode,
        string hardwareId,
        CancellationToken cancellationToken)
    {
        return deviceRegistrationRepository.IsAppReviewDeviceAsync(
            storeCode.Trim(),
            deviceCode.Trim(),
            hardwareId.Trim(),
            cancellationToken);
    }

    public async Task<bool> IsActiveEmployeeCashierAsync(
        string cashierId,
        string userGuid,
        CancellationToken cancellationToken)
    {
        var normalizedCashierId = cashierId.Trim();
        var normalizedUserGuid = userGuid.Trim();
        if (normalizedCashierId.Length == 0 || normalizedUserGuid.Length == 0)
        {
            return false;
        }

        // 关键逻辑：审核设备的票据必须仍对应唯一启用的员工条码身份，停用条码立即失效。
        return await dbContext.MainDb.Queryable<EmployeeCashierBarcode>()
            .AnyAsync(
                barcode => barcode.HGUID == normalizedCashierId
                    && barcode.UserGUID == normalizedUserGuid
                    && barcode.Status,
                cancellationToken);
    }
}

public sealed class PosIpadAppReviewOptions
{
    public const string SectionName = "PosIpadAppReview";

    public bool Enabled { get; set; }

    public string StoreCode { get; set; } = string.Empty;

    public DateTimeOffset? ExpiresAtUtc { get; set; }

    public int MaxActiveDevices { get; set; } = 1;

    // 每轮审核使用新的非敏感 UUID；成功审批后会写入独立的追加式消费表。
    public string GrantId { get; set; } = string.Empty;

    // 只保存一次性设备开通码的 SHA-256；明文仅通过 App Review Information 交给审核员。
    public string RegistrationCodeSha256 { get; set; } = string.Empty;
}
