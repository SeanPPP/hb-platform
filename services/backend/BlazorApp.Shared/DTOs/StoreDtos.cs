using System.ComponentModel.DataAnnotations;

namespace BlazorApp.Shared.DTOs
{
    /// <summary>
    /// 分店查询DTO
    /// </summary>
    public class StoreQueryDto
    {
        /// <summary>
        /// 页码，从1开始
        /// </summary>
        public int Page { get; set; } = 1;

        /// <summary>
        /// 每页大小
        /// </summary>
        public int PageSize { get; set; } = 50;

        /// <summary>
        /// 搜索关键字
        /// </summary>
        public string? Search { get; set; }

        /// <summary>
        /// 是否启用状态过滤
        /// </summary>
        public bool? IsActive { get; set; }

        /// <summary>
        /// 品牌名称精确筛选
        /// </summary>
        public string? BrandName { get; set; }

        /// <summary>
        /// 用户GUID过滤
        /// </summary>
        public string? UserGUID { get; set; }

        /// <summary>
        /// 排序字段（StoreName, StoreCode, CreatedAt等）
        /// </summary>
        public string? SortField { get; set; }

        /// <summary>
        /// 排序方向（asc, desc）
        /// </summary>
        public string? SortOrder { get; set; } = "asc";
    }

    /// <summary>
    /// 创建分店DTO
    /// </summary>
    public class CreateStoreDto
    {
        /// <summary>
        /// 分店名称
        /// </summary>
        [Required(ErrorMessage = "分店名称不能为空")]
        [StringLength(100, ErrorMessage = "分店名称长度不能超过100个字符")]
        public string StoreName { get; set; } = string.Empty;

        /// <summary>
        /// 分店代码
        /// </summary>
        [Required(ErrorMessage = "分店代码不能为空")]
        [StringLength(20, ErrorMessage = "分店代码长度不能超过20个字符")]
        public string StoreCode { get; set; } = string.Empty;

        /// <summary>
        /// 分店描述
        /// </summary>
        [StringLength(500, ErrorMessage = "描述长度不能超过500个字符")]
        public string? Description { get; set; }

        /// <summary>
        /// 分店地址
        /// </summary>
        [StringLength(200, ErrorMessage = "地址长度不能超过200个字符")]
        public string? Address { get; set; }

        /// <summary>
        /// 联系电话
        /// </summary>
        [StringLength(20, ErrorMessage = "联系电话长度不能超过20个字符")]
        public string? ContactPhone { get; set; }

        /// <summary>
        /// 联系邮箱
        /// </summary>
        [EmailAddress(ErrorMessage = "联系邮箱格式不正确")]
        [StringLength(100, ErrorMessage = "联系邮箱长度不能超过100个字符")]
        public string? ContactEmail { get; set; }

        /// <summary>
        /// 澳大利亚商业号码
        /// </summary>
        [StringLength(20, ErrorMessage = "ABN长度不能超过20个字符")]
        public string? ABN { get; set; }

        /// <summary>
        /// 品牌名称
        /// </summary>
        [StringLength(100, ErrorMessage = "品牌名称长度不能超过100个字符")]
        public string? BrandName { get; set; }

        /// <summary>
        /// 是否启用收银系统；新建分店默认不启用，避免误纳入 POS 相关流程。
        /// </summary>
        public bool IsActive { get; set; } = false;
    }

    /// <summary>
    /// 更新分店DTO
    /// </summary>
    public class UpdateStoreDto
    {
        /// <summary>
        /// 分店名称
        /// </summary>
        [Required(ErrorMessage = "分店名称不能为空")]
        [StringLength(100, ErrorMessage = "分店名称长度不能超过100个字符")]
        public string StoreName { get; set; } = string.Empty;

        /// <summary>
        /// 分店代码
        /// </summary>
        [Required(ErrorMessage = "分店代码不能为空")]
        [StringLength(20, ErrorMessage = "分店代码长度不能超过20个字符")]
        public string StoreCode { get; set; } = string.Empty;

        /// <summary>
        /// 分店描述
        /// </summary>
        [StringLength(500, ErrorMessage = "描述长度不能超过500个字符")]
        public string? Description { get; set; }

        /// <summary>
        /// 分店地址
        /// </summary>
        [StringLength(200, ErrorMessage = "地址长度不能超过200个字符")]
        public string? Address { get; set; }

        /// <summary>
        /// 联系电话
        /// </summary>
        [StringLength(20, ErrorMessage = "联系电话长度不能超过20个字符")]
        public string? ContactPhone { get; set; }

        /// <summary>
        /// 联系邮箱
        /// </summary>
        [EmailAddress(ErrorMessage = "联系邮箱格式不正确")]
        [StringLength(100, ErrorMessage = "联系邮箱长度不能超过100个字符")]
        public string? ContactEmail { get; set; }

        /// <summary>
        /// 澳大利亚商业号码
        /// </summary>
        [StringLength(20, ErrorMessage = "ABN长度不能超过20个字符")]
        public string? ABN { get; set; }

        /// <summary>
        /// 品牌名称
        /// </summary>
        [StringLength(100, ErrorMessage = "品牌名称长度不能超过100个字符")]
        public string? BrandName { get; set; }

        /// <summary>
        /// 是否启用状态
        /// </summary>
        public bool IsActive { get; set; } = true;
    }

    /// <summary>
    /// 分店数据传输对象
    /// </summary>
    public class StoreDto
    {
        /// <summary>
        /// 分店名称
        /// </summary>
        [Required(ErrorMessage = "分店名称不能为空")]
        [StringLength(100, ErrorMessage = "分店名称长度不能超过100个字符")]
        public string StoreName { get; set; } = string.Empty;

        /// <summary>
        /// 分店代码
        /// </summary>
        [Required(ErrorMessage = "分店代码不能为空")]
        [StringLength(20, ErrorMessage = "分店代码长度不能超过20个字符")]
        public string StoreCode { get; set; } = string.Empty;

        /// <summary>
        /// 分店描述
        /// </summary>
        [StringLength(500, ErrorMessage = "描述长度不能超过500个字符")]
        public string? Description { get; set; }

        /// <summary>
        /// 分店地址
        /// </summary>
        [StringLength(200, ErrorMessage = "地址长度不能超过200个字符")]
        public string? Address { get; set; }

        /// <summary>
        /// 联系电话
        /// </summary>
        [StringLength(20, ErrorMessage = "联系电话长度不能超过20个字符")]
        public string? ContactPhone { get; set; }

        /// <summary>
        /// 联系邮箱
        /// </summary>
        [EmailAddress(ErrorMessage = "联系邮箱格式不正确")]
        [StringLength(100, ErrorMessage = "联系邮箱长度不能超过100个字符")]
        public string? ContactEmail { get; set; }

        /// <summary>
        /// 澳大利亚商业号码
        /// </summary>
        [StringLength(20, ErrorMessage = "ABN长度不能超过20个字符")]
        public string? ABN { get; set; }

        /// <summary>
        /// 品牌名称
        /// </summary>
        [StringLength(100, ErrorMessage = "品牌名称长度不能超过100个字符")]
        public string? BrandName { get; set; }

        /// <summary>
        /// 是否启用
        /// </summary>
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// 分店唯一标识符
        /// </summary>
        public string StoreGUID { get; set; } = string.Empty;

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// 更新时间
        /// </summary>
        public DateTime UpdatedAt { get; set; }

        /// <summary>
        /// 总用户数
        /// </summary>
        public int TotalUsers { get; set; }

        /// <summary>
        /// 活跃用户数
        /// </summary>
        public int ActiveUsers { get; set; }
    }

    /// <summary>
    /// 更新分店状态DTO
    /// </summary>
    public class UpdateStoreStatusDto
    {
        /// <summary>
        /// 是否启用状态
        /// </summary>
        public bool IsActive { get; set; }
    }

    /// <summary>
    /// 分店详情DTO
    /// </summary>
    public class StoreDetailDto
    {
        /// <summary>
        /// 分店名称
        /// </summary>
        [Required(ErrorMessage = "分店名称不能为空")]
        [StringLength(100, ErrorMessage = "分店名称长度不能超过100个字符")]
        public string StoreName { get; set; } = string.Empty;

        /// <summary>
        /// 分店代码
        /// </summary>
        [Required(ErrorMessage = "分店代码不能为空")]
        [StringLength(20, ErrorMessage = "分店代码长度不能超过20个字符")]
        public string StoreCode { get; set; } = string.Empty;

        /// <summary>
        /// 分店描述
        /// </summary>
        [StringLength(500, ErrorMessage = "描述长度不能超过500个字符")]
        public string? Description { get; set; }

        /// <summary>
        /// 分店地址
        /// </summary>
        [StringLength(200, ErrorMessage = "地址长度不能超过200个字符")]
        public string? Address { get; set; }

        /// <summary>
        /// 联系电话
        /// </summary>
        [StringLength(20, ErrorMessage = "联系电话长度不能超过20个字符")]
        public string? ContactPhone { get; set; }

        /// <summary>
        /// 联系邮箱
        /// </summary>
        [EmailAddress(ErrorMessage = "联系邮箱格式不正确")]
        [StringLength(100, ErrorMessage = "联系邮箱长度不能超过100个字符")]
        public string? ContactEmail { get; set; }

        /// <summary>
        /// 澳大利亚商业号码
        /// </summary>
        [StringLength(20, ErrorMessage = "ABN长度不能超过20个字符")]
        public string? ABN { get; set; }

        /// <summary>
        /// 品牌名称
        /// </summary>
        [StringLength(100, ErrorMessage = "品牌名称长度不能超过100个字符")]
        public string? BrandName { get; set; }

        /// <summary>
        /// 是否启用
        /// </summary>
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// 分店唯一标识符
        /// </summary>
        public string StoreGUID { get; set; } = string.Empty;

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// 更新时间
        /// </summary>
        public DateTime UpdatedAt { get; set; }

        /// <summary>
        /// 总用户数
        /// </summary>
        public int TotalUsers { get; set; }

        /// <summary>
        /// 活跃用户数
        /// </summary>
        public int ActiveUsers { get; set; }

        /// <summary>
        /// 分店用户列表
        /// </summary>
        public List<StoreUserDto> Users { get; set; } = new();
    }

    /// <summary>
    /// 分店用户DTO
    /// </summary>
    public class StoreUserDto
    {
        /// <summary>
        /// 用户GUID
        /// </summary>
        public string UserGUID { get; set; } = string.Empty;

        /// <summary>
        /// 用户名
        /// </summary>
        public string Username { get; set; } = string.Empty;

        /// <summary>
        /// 真实姓名
        /// </summary>
        public string RealName { get; set; } = string.Empty;

        /// <summary>
        /// 邮箱
        /// </summary>
        public string Email { get; set; } = string.Empty;

        /// <summary>
        /// 手机号
        /// </summary>
        public string? Phone { get; set; }

        /// <summary>
        /// 角色列表
        /// </summary>
        public List<string> Roles { get; set; } = new();

        /// <summary>
        /// 是否允许该用户管理此分店
        /// </summary>
        public bool IsPrimary { get; set; }

        /// <summary>
        /// 用户状态
        /// </summary>
        public bool IsActive { get; set; }

        /// <summary>
        /// 关联创建时间
        /// </summary>
        public DateTime AssignedAt { get; set; }
    }

    /// <summary>
    /// 添加用户到分店DTO
    /// </summary>
    public class AddUserToStoreDto
    {
        /// <summary>
        /// 用户GUID
        /// </summary>
        [Required(ErrorMessage = "用户GUID不能为空")]
        public string UserGUID { get; set; } = string.Empty;

        /// <summary>
        /// 是否允许该用户管理此分店
        /// </summary>
        public bool IsPrimary { get; set; } = false;
    }
}
