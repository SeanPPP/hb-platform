using System.ComponentModel.DataAnnotations;

namespace BlazorApp.Shared.DTOs
{
    /// <summary>
    /// 购物车转订单请求DTO
    /// </summary>
    public class CartToOrderRequestDto
    {
        /// <summary>
        /// 购物车GUID
        /// </summary>
        [Required(ErrorMessage = "购物车GUID不能为空")]
        public string CartGUID { get; set; } = string.Empty;

        /// <summary>
        /// 订单日期（可选，默认为当前日期）
        /// </summary>
        public DateTime? OrderDate { get; set; }

        /// <summary>
        /// 备注信息
        /// </summary>
        public string? Remarks { get; set; }
    }

    /// <summary>
    /// 购物车转订单响应DTO
    /// </summary>
    public class CartToOrderResponseDto
    {
        /// <summary>
        /// 是否成功
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// 订单GUID
        /// </summary>
        public string? OrderGUID { get; set; }

        /// <summary>
        /// 订单号
        /// </summary>
        public string? OrderNo { get; set; }

        /// <summary>
        /// 是否创建了新订单（false表示添加到现有订单）
        /// </summary>
        public bool IsNewOrder { get; set; }

        /// <summary>
        /// 消息
        /// </summary>
        public string? Message { get; set; }
    }
}
