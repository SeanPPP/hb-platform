using BlazorApp.Api.Data;
using BlazorApp.Shared.Models.POSM;

namespace BlazorApp.Api.Services.Attendance;

public interface IAttendancePosDeviceStatusProvider
{
    Task<bool> IsActiveAsync(
        string deviceCode,
        string storeCode,
        string hardwareId,
        CancellationToken cancellationToken = default);
}

public sealed class AttendancePosDeviceStatusProvider(POSMSqlSugarContext context)
    : IAttendancePosDeviceStatusProvider
{
    public async Task<bool> IsActiveAsync(
        string deviceCode,
        string storeCode,
        string hardwareId,
        CancellationToken cancellationToken = default)
    {
        var device = await context.Db.Queryable<POSM_设备注册信息表>()
            .FirstAsync(item =>
                item.系统设备编号 == deviceCode
                && item.分店代码 == storeCode
                && item.设备硬件识别码 == hardwareId);
        return device?.设备状态 == (int)DeviceStatus.启用;
    }
}
