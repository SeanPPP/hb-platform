using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces
{
    /// <summary>
    /// PDA购物车转订单服务接口
    /// </summary>
    public interface IPDACartToOrderService
    {
        /// <summary>
        /// 将购物车转换为仓库订单
        /// </summary>
        /// <param name="request">转换请求</param>
        /// <param name="deviceHardwareId">设备硬件ID</param>
        /// <param name="expectedStoreCode">已认证设备绑定分店代码</param>
        /// <returns>转换结果</returns>
        Task<CartToOrderResponseDto> ConvertCartToOrderAsync(
            CartToOrderRequestDto request,
            string deviceHardwareId,
            string expectedStoreCode
        );
    }
}
