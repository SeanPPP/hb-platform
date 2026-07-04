using BlazorApp.Shared.Models.POSM;

namespace BlazorApp.Api.Interfaces
{
    /// <summary>
    /// 设备注册服务接口
    /// </summary>
    public interface IDeviceRegistrationService
    {
        /// <summary>
        /// 获取所有设备列表
        /// </summary>
        /// <returns></returns>
        Task<List<POSM_设备注册信息表>> GetAllDevicesAsync();

        /// <summary>
        /// 根据ID获取设备信息
        /// </summary>
        /// <param name="id">设备ID</param>
        /// <returns></returns>
        Task<POSM_设备注册信息表?> GetDeviceByIdAsync(int id);

        /// <summary>
        /// 根据硬件识别码获取设备信息
        /// </summary>
        /// <param name="hardwareId">设备硬件识别码</param>
        /// <returns></returns>
        Task<POSM_设备注册信息表?> GetDeviceByHardwareIdAsync(string hardwareId);

        /// <summary>
        /// 根据系统设备编号获取设备信息
        /// </summary>
        /// <param name="systemDeviceNumber">系统设备编号</param>
        /// <returns></returns>
        Task<POSM_设备注册信息表?> GetDeviceBySystemNumberAsync(string systemDeviceNumber);

        /// <summary>
        /// 根据分店代码获取设备列表
        /// </summary>
        /// <param name="storeCode">分店代码</param>
        /// <returns></returns>
        Task<List<POSM_设备注册信息表>> GetDevicesByStoreCodeAsync(string storeCode);

        /// <summary>
        /// 根据设备状态获取设备列表
        /// </summary>
        /// <param name="status">设备状态</param>
        /// <returns></returns>
        Task<List<POSM_设备注册信息表>> GetDevicesByStatusAsync(int status);

        /// <summary>
        /// 创建新设备
        /// </summary>
        /// <param name="device">设备信息</param>
        /// <param name="createdBy">创建人</param>
        /// <returns></returns>
        Task<POSM_设备注册信息表> CreateDeviceAsync(POSM_设备注册信息表 device, string createdBy);

        /// <summary>
        /// 更新设备信息
        /// </summary>
        /// <param name="device">设备信息</param>
        /// <param name="updatedBy">更新人</param>
        /// <returns></returns>
        Task<bool> UpdateDeviceAsync(POSM_设备注册信息表 device, string updatedBy);

        /// <summary>
        /// 删除设备
        /// </summary>
        /// <param name="id">设备ID</param>
        /// <returns></returns>
        Task<bool> DeleteDeviceAsync(int id);

        /// <summary>
        /// 设备注册
        /// </summary>
        /// <param name="hardwareId">设备硬件识别码</param>
        /// <param name="deviceType">设备类型</param>
        /// <param name="deviceSystem">设备系统</param>
        /// <param name="storeCode">分店代码</param>
        /// <returns></returns>
        Task<POSM_设备注册信息表> RegisterDeviceAsync(string hardwareId, string deviceType, string deviceSystem, string? storeCode = null);

        /// <summary>
        /// 激活设备
        /// </summary>
        /// <param name="id">设备ID</param>
        /// <param name="activatedBy">激活人</param>
        /// <returns></returns>
        Task<bool> ActivateDeviceAsync(int id, string activatedBy);

        /// <summary>
        /// 禁用设备
        /// </summary>
        /// <param name="id">设备ID</param>
        /// <param name="disabledBy">禁用人</param>
        /// <returns></returns>
        Task<bool> DisableDeviceAsync(int id, string disabledBy);

        /// <summary>
        /// 锁定设备
        /// </summary>
        /// <param name="id">设备ID</param>
        /// <param name="lockedBy">锁定人</param>
        /// <returns></returns>
        Task<bool> LockDeviceAsync(int id, string lockedBy);

        /// <summary>
        /// 验证设备授权码
        /// </summary>
        /// <param name="hardwareId">设备硬件识别码</param>
        /// <param name="authCode">授权码</param>
        /// <returns></returns>
        Task<bool> ValidateDeviceAuthCodeAsync(string hardwareId, string authCode);

        /// <summary>
        /// 更新设备运行状态
        /// </summary>
        /// <param name="hardwareId">设备硬件识别码</param>
        /// <param name="isOnline">是否在线</param>
        /// <param name="cashierId">当前收银员ID</param>
        /// <param name="cashierName">当前收银员姓名</param>
        /// <returns></returns>
        Task<bool> UpdateRuntimeStatusAsync(
            string hardwareId,
            bool isOnline,
            string? cashierId,
            string? cashierName);

        /// <summary>
        /// 验证设备授权码并在授权码不匹配时返回数据库中的最新授权码（仅限启用设备）
        /// </summary>
        /// <param name="hardwareId">硬件ID</param>
        /// <param name="authCode">当前授权码</param>
        /// <returns>验证结果，包含是否有效和数据库中的最新授权码（如果不匹配）</returns>
        Task<(bool IsValid, string? NewAuthCode)> ValidateAndUpdateDeviceAuthCodeAsync(string hardwareId, string authCode);

        /// <summary>
        /// 解绑设备：校验授权码后将设备标记为未注册并清空授权码
        /// </summary>
        /// <param name="hardwareId">设备硬件识别码</param>
        /// <param name="authCode">当前授权码</param>
        /// <param name="updatedBy">更新人</param>
        /// <returns>解绑是否成功</returns>
        Task<bool> UnbindDeviceAsync(string hardwareId, string authCode, string updatedBy);

        /// <summary>
        /// 生成新的设备授权码
        /// </summary>
        /// <param name="id">设备ID</param>
        /// <param name="generatedBy">生成人</param>
        /// <returns></returns>
        Task<string> GenerateNewAuthCodeAsync(int id, string generatedBy);

        /// <summary>
        /// 获取设备统计信息
        /// </summary>
        /// <returns></returns>
        Task<object> GetDeviceStatisticsAsync();

        /// <summary>
        /// 分页获取设备列表
        /// </summary>
        /// <param name="page">页码</param>
        /// <param name="pageSize">每页数量</param>
        /// <param name="storeCode">分店代码过滤</param>
        /// <param name="deviceType">设备类型过滤</param>
        /// <param name="deviceSystem">设备系统过滤</param>
        /// <param name="status">状态过滤</param>
        /// <param name="keyword">关键词搜索</param>
        /// <returns></returns>
        Task<(List<POSM_设备注册信息表> devices, int total)> GetDevicesPagedAsync(
            int page = 1,
            int pageSize = 20,
            string? storeCode = null,
            string? deviceType = null,
            string? deviceSystem = null,
            int? status = null,
            string? keyword = null);
    }
}
