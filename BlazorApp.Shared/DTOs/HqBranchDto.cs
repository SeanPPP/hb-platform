using System.ComponentModel.DataAnnotations;

namespace BlazorApp.Shared.DTOs
{
    /// <summary>
    /// HQ分店DTO
    /// </summary>
    public class HqBranchDto
    {
        public int Id { get; set; }

        /// <summary>
        /// 分店代码
        /// </summary>
        [Required(ErrorMessage = "分店代码不能为空")]
        [StringLength(50, ErrorMessage = "分店代码不能超过50个字符")]
        public string BranchCode { get; set; } = string.Empty;

        /// <summary>
        /// 分店名称
        /// </summary>
        [Required(ErrorMessage = "分店名称不能为空")]
        [StringLength(200, ErrorMessage = "分店名称不能超过200个字符")]
        public string BranchName { get; set; } = string.Empty;

        /// <summary>
        /// 商业编号
        /// </summary>
        [StringLength(100, ErrorMessage = "商业编号不能超过100个字符")]
        public string? BusinessNumber { get; set; }

        /// <summary>
        /// 分店电话
        /// </summary>
        [StringLength(50, ErrorMessage = "电话号码不能超过50个字符")]
        [Phone(ErrorMessage = "请输入有效的电话号码")]
        public string? Phone { get; set; }

        /// <summary>
        /// 店经理姓名
        /// </summary>
        [StringLength(100, ErrorMessage = "店经理姓名不能超过100个字符")]
        public string? ManagerName { get; set; }

        /// <summary>
        /// 店经理电话
        /// </summary>
        [StringLength(50, ErrorMessage = "电话号码不能超过50个字符")]
        [Phone(ErrorMessage = "请输入有效的电话号码")]
        public string? ManagerPhone { get; set; }

        /// <summary>
        /// 分店地址
        /// </summary>
        [StringLength(500, ErrorMessage = "地址不能超过500个字符")]
        public string? Address { get; set; }

        /// <summary>
        /// 是否激活
        /// </summary>
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// 备注
        /// </summary>
        [StringLength(1000, ErrorMessage = "备注不能超过1000个字符")]
        public string? Remarks { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// 更新时间
        /// </summary>
        public DateTime? UpdatedAt { get; set; }

        /// <summary>
        /// 创建者
        /// </summary>
        public string? CreatedBy { get; set; }

        /// <summary>
        /// 更新者
        /// </summary>
        public string? UpdatedBy { get; set; }
    }

    /// <summary>
    /// 创建分店请求DTO
    /// </summary>
    public class CreateHqBranchDto
    {
        /// <summary>
        /// 分店代码
        /// </summary>
        [Required(ErrorMessage = "分店代码不能为空")]
        [StringLength(50, ErrorMessage = "分店代码不能超过50个字符")]
        public string BranchCode { get; set; } = string.Empty;

        /// <summary>
        /// 分店名称
        /// </summary>
        [Required(ErrorMessage = "分店名称不能为空")]
        [StringLength(200, ErrorMessage = "分店名称不能超过200个字符")]
        public string BranchName { get; set; } = string.Empty;

        /// <summary>
        /// 商业编号
        /// </summary>
        [StringLength(100, ErrorMessage = "商业编号不能超过100个字符")]
        public string? BusinessNumber { get; set; }

        /// <summary>
        /// 分店电话
        /// </summary>
        [StringLength(50, ErrorMessage = "电话号码不能超过50个字符")]
        public string? Phone { get; set; }

        /// <summary>
        /// 店经理姓名
        /// </summary>
        [StringLength(100, ErrorMessage = "店经理姓名不能超过100个字符")]
        public string? ManagerName { get; set; }

        /// <summary>
        /// 店经理电话
        /// </summary>
        [StringLength(50, ErrorMessage = "电话号码不能超过50个字符")]
        public string? ManagerPhone { get; set; }

        /// <summary>
        /// 分店地址
        /// </summary>
        [StringLength(500, ErrorMessage = "地址不能超过500个字符")]
        public string? Address { get; set; }

        /// <summary>
        /// 是否激活
        /// </summary>
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// 备注
        /// </summary>
        [StringLength(1000, ErrorMessage = "备注不能超过1000个字符")]
        public string? Remarks { get; set; }
    }

    /// <summary>
    /// 更新分店请求DTO
    /// </summary>
    public class UpdateHqBranchDto : CreateHqBranchDto
    {
        public int Id { get; set; }
    }

    /// <summary>
    /// 分店搜索请求DTO
    /// </summary>
    public class SearchHqBranchDto
    {
        /// <summary>
        /// 搜索关键词
        /// </summary>
        public string? Keyword { get; set; }

        /// <summary>
        /// 是否只显示活跃的分店
        /// </summary>
        public bool? ActiveOnly { get; set; }

        /// <summary>
        /// 页码
        /// </summary>
        public int Page { get; set; } = 1;

        /// <summary>
        /// 每页大小
        /// </summary>
        public int PageSize { get; set; } = 10;
    }
}