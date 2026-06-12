using System.ComponentModel.DataAnnotations;

namespace BlazorApp.Shared.DTOs
{
    /// <summary>
    /// 统一API响应格式
    /// </summary>
    /// <typeparam name="T">响应数据类型</typeparam>
    public class ApiResponse<T>
    {
        /// <summary>
        /// 操作是否成功
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// 操作是否成功（兼容性属性）
        /// </summary>
        public bool IsSuccess
        {
            get => Success;
            set => Success = value;
        }

        /// <summary>
        /// 响应消息
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// 响应数据
        /// </summary>
        public T? Data { get; set; }

        /// <summary>
        /// 错误代码
        /// </summary>
        public string? ErrorCode { get; set; }

        /// <summary>
        /// 状态代码（兼容性属性）
        /// </summary>
        public string? Code
        {
            get => ErrorCode;
            set => ErrorCode = value;
        }

        /// <summary>
        /// 错误详情
        /// </summary>
        public object? Details { get; set; }

        /// <summary>
        /// 时间戳
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// 创建成功响应
        /// </summary>
        public static ApiResponse<T> OK(T data, string message = "操作成功")
        {
            return new ApiResponse<T>
            {
                Success = true,
                Message = message,
                Data = data,
                Timestamp = DateTime.UtcNow
            };
        }

        /// <summary>
        /// 创建成功响应（无数据）
        /// </summary>
        public static ApiResponse<object> CreateSuccess(string message = "操作成功")
        {
            return new ApiResponse<object>
            {
                Success = true,
                Message = message,
                Timestamp = DateTime.UtcNow
            };
        }

        /// <summary>
        /// 创建成功响应（无数据，带状态码）
        /// </summary>
        public static ApiResponse<object> CreateSuccess(string message, string code)
        {
            return new ApiResponse<object>
            {
                Success = true,
                Message = message,
                ErrorCode = code,
                Timestamp = DateTime.UtcNow
            };
        }

        /// <summary>
        /// 创建失败响应
        /// </summary>
        public static ApiResponse<T> Error(string message, string? errorCode = null, object? details = null)
        {
            return new ApiResponse<T>
            {
                Success = false,
                Message = message,
                ErrorCode = errorCode,
                Details = details,
                Timestamp = DateTime.UtcNow
            };
        }
    }

    /// <summary>
    /// 分页结果
    /// </summary>
    /// <typeparam name="T">数据类型</typeparam>
    public class PagedResult<T>
    {
        /// <summary>
        /// 数据项列表
        /// </summary>
        public List<T>? Items { get; set; } = new();

        /// <summary>
        /// 总数量
        /// </summary>
        public int Total { get; set; }

        /// <summary>
        /// 当前页码
        /// </summary>
        public int Page { get; set; }

        /// <summary>
        /// 每页数量
        /// </summary>
        public int PageSize { get; set; }

        /// <summary>
        /// 总页数
        /// </summary>
        public int TotalPages => (int)Math.Ceiling((double)Total / PageSize);

        /// <summary>
        /// 总数量（兼容性属性）
        /// </summary>
        public int TotalCount => Total;

        /// <summary>
        /// 页索引（兼容性属性）
        /// </summary>
        public int PageIndex => Page;
    }
}
