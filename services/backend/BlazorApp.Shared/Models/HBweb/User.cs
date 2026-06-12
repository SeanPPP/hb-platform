using SqlSugar;

namespace BlazorApp.Shared.Models
{
    /// <summary>
    /// 用户实体类，表示系统中的用户信息
    /// </summary>
    public class User : BaseEntity
    {
        /// <summary>
        /// 用户全局唯一标识符
        /// </summary>
        [SugarColumn(IsPrimaryKey = true, IsNullable = false)]
        public string UserGUID { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// 用户名
        /// </summary>
        [SugarColumn(IsNullable = false)]
        public string Username { get; set; } = string.Empty;

        /// <summary>
        /// 邮箱地址
        /// </summary>
        [SugarColumn(IsNullable = false)]
        public string Email { get; set; } = string.Empty;

        /// <summary>
        /// 密码哈希值
        /// </summary>
        [SugarColumn(IsNullable = false)]
        public string PasswordHash { get; set; } = string.Empty;

        /// <summary>
        /// 用户全名
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public string? FullName { get; set; }

        /// <summary>
        /// 最后登录时间
        /// </summary>
        [SugarColumn(IsNullable = true)]
        public DateTime? LastLoginAt { get; set; }

        /// <summary>
        /// 是否激活状态
        /// </summary>
        [SugarColumn(IsNullable = false)]
        public bool IsActive { get; set; } = true;

        ///// <summary>
        ///// 微信OpenId - 用于微信关联登录
        ///// </summary>
        ///// <remarks>
        ///// 微信用户唯一标识，用于实现微信登录和账号绑定功能
        ///// - 可为空：用户可以选择不绑定微信
        ///// - 唯一索引：一个OpenId只能对应一个用户账号
        ///// - 长度：通常为28位字符串
        ///// </remarks>
        //[SugarColumn(IsNullable = true, Length = 50)]
        //public string? WeChatOpenId { get; set; }

        ///// <summary>
        ///// 微信UnionId - 用于同一开放平台下的应用间用户身份统一
        ///// </summary>
        ///// <remarks>
        ///// 微信开放平台统一标识，当用户在同一开放平台下的不同应用中都有账号时，UnionId相同
        ///// - 可为空：只有在开放平台应用中才会有UnionId
        ///// - 用于跨应用的用户身份识别
        ///// </remarks>
        //[SugarColumn(IsNullable = true, Length = 50)]
        //public string? WeChatUnionId { get; set; }

        ///// <summary>
        ///// 微信绑定时间
        ///// </summary>
        //[SugarColumn(IsNullable = true)]
        //public DateTime? WeChatBindTime { get; set; }

        /// <summary>
        /// 刷新令牌列表（一对多导航属性）
        /// </summary>
        [Navigate(NavigateType.OneToMany, nameof(RefreshToken.UserGUID), nameof(UserGUID))]
        public List<RefreshToken> RefreshTokens { get; set; } = new();

        /// <summary>
        /// 用户角色列表（多对多导航属性）
        /// </summary>
        [Navigate(typeof(UserRole), nameof(UserRole.UserGUID), nameof(UserRole.RoleGUID))]
        public List<Role>? Roles { get; set; }

        /// <summary>
        /// 用户门店列表（多对多导航属性）
        /// </summary>
        [Navigate(typeof(UserStore), nameof(UserStore.UserGUID), nameof(UserStore.StoreGUID))]
        public List<Store>? Stores { get; set; }
    }
}
