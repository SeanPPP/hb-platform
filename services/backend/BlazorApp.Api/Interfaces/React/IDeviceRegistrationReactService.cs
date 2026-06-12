using System.Threading.Tasks;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces.React
{
    /// <summary>
    /// 设备注册 React 服务接口
    /// </summary>
    public interface IDeviceRegistrationReactService
    {
        /// <summary>
        /// 获取设备网格数据
        /// </summary>
        /// <param name="request">网格请求参数</param>
        /// <returns>设备列表数据</returns>
        Task<GridResponseDto<DeviceRegistrationListDto>> GetGridDataAsync(GridRequestDto request);

        /// <summary>
        /// 根据 ID 获取设备详情
        /// </summary>
        /// <param name="id">设备 ID</param>
        /// <returns>设备详情</returns>
        Task<ApiResponse<DeviceRegistrationDetailDto>> GetByIdAsync(int id);

        /// <summary>
        /// 更新设备信息
        /// </summary>
        /// <param name="id">设备 ID</param>
        /// <param name="dto">更新数据</param>
        /// <param name="updatedBy">更新人</param>
        /// <returns>更新后的设备信息</returns>
        Task<ApiResponse<DeviceRegistrationDetailDto>> UpdateAsync(int id, UpdateDeviceRegistrationDto dto, string updatedBy);
    }
}
