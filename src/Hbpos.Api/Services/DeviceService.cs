using Hbpos.Api.Data;
using Hbpos.Contracts.Devices;
using SqlSugar;

namespace Hbpos.Api.Services;

public interface IDeviceService
{
    Task<DeviceVerifyResponse> VerifyAsync(DeviceVerifyRequest request, CancellationToken cancellationToken);
}

public sealed class DeviceService(HbposSqlSugarContext dbContext) : IDeviceService
{
    public async Task<DeviceVerifyResponse> VerifyAsync(
        DeviceVerifyRequest request,
        CancellationToken cancellationToken)
    {
        var store = await dbContext.MainDb.Queryable<BlazorApp.Shared.Models.Store>()
            .FirstAsync(x => x.StoreCode == request.StoreCode && x.IsActive && !x.IsDeleted, cancellationToken);

        if (store is null)
        {
            return new DeviceVerifyResponse(
                request.DeviceCode,
                request.StoreCode,
                string.Empty,
                false,
                "门店不存在或已停用");
        }

        const string sql = """
            SELECT TOP 1
                [系统设备编号] AS DeviceCode,
                [分店代码] AS StoreCode,
                [设备硬件识别码] AS HardwareId,
                [设备状态] AS DeviceStatus
            FROM [POSM_设备注册信息表]
            WHERE [系统设备编号] = @DeviceCode
              AND [分店代码] = @StoreCode
            """;

        var parameters = new[]
        {
            new SugarParameter("@DeviceCode", request.DeviceCode),
            new SugarParameter("@StoreCode", request.StoreCode)
        };

        var device = await dbContext.PosmDb.Ado.SqlQuerySingleAsync<DeviceRegistrationRow>(sql, parameters);

        if (device is null)
        {
            return new DeviceVerifyResponse(
                request.DeviceCode,
                request.StoreCode,
                store.StoreName,
                false,
                "设备未注册");
        }

        if (!string.IsNullOrWhiteSpace(request.HardwareId)
            && !string.Equals(device.HardwareId, request.HardwareId, StringComparison.OrdinalIgnoreCase))
        {
            return new DeviceVerifyResponse(
                request.DeviceCode,
                request.StoreCode,
                store.StoreName,
                false,
                "设备硬件识别码不匹配");
        }

        var allowed = device.DeviceStatus == 1;
        return new DeviceVerifyResponse(
            request.DeviceCode,
            request.StoreCode,
            store.StoreName,
            allowed,
            allowed ? null : "设备未启用");
    }

    private sealed class DeviceRegistrationRow
    {
        public string? DeviceCode { get; set; }

        public string? StoreCode { get; set; }

        public string? HardwareId { get; set; }

        public int DeviceStatus { get; set; }
    }
}
