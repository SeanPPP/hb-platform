using SqlSugar;

namespace BlazorApp.Shared.Models
{
    /// <summary>
    /// 门店实体类，表示系统中的门店信息
    /// </summary>
    public class Store : BaseEntity
    {
        /// <summary>
        /// 门店全局唯一标识符
        /// </summary>
        [SugarColumn(IsPrimaryKey = true, IsNullable = false)]
        public string StoreGUID { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// 门店名称
        /// </summary>
        [SugarColumn(IsNullable = false, Length = 100)]
        public string StoreName { get; set; } = string.Empty;

        /// <summary>
        /// 门店代码
        /// </summary>
        [SugarColumn(IsNullable = false, Length = 50)]
        public string StoreCode { get; set; } = string.Empty;

        /// <summary>
        /// 门店地址
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 500)]
        public string? Address { get; set; }

        /// <summary>
        /// 联系邮箱
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 100)]
        public string? ContactEmail { get; set; }

        /// <summary>
        /// 澳大利亚商业号码
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 20)]
        public string? ABN { get; set; }

        /// <summary>
        /// 品牌名称
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 100)]
        public string? BrandName { get; set; }

        /// <summary>
        /// 联系电话
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 200)]
        public string? Phone { get; set; }

        /// <summary>
        /// 是否激活状态
        /// </summary>
        [SugarColumn(IsNullable = false)]
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// 门店关联的用户列表（多对多导航属性）
        /// </summary>
        [Navigate(typeof(UserStore), nameof(UserStore.StoreGUID), nameof(UserStore.UserGUID))]
        public List<User>? Users { get; set; }
    }
}
