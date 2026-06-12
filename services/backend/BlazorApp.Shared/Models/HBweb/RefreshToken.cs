using SqlSugar;

namespace BlazorApp.Shared.Models
{
    /// <summary>
    /// 刷新令牌实体类，用于JWT令牌的刷新机制
    /// </summary>
    public class RefreshToken : BaseEntity
    {
        /// <summary>
        /// 刷新令牌全局唯一标识符
        /// </summary>
        [SugarColumn(IsPrimaryKey = true, IsNullable = false)]
        public string RefreshTokenGUID { get; set; } = Guid.NewGuid().ToString();
        
        /// <summary>
        /// 用户GUID（外键）
        /// </summary>
        [SugarColumn(IsNullable = false)]
        public string UserGUID { get; set; } = string.Empty;
        
        /// <summary>
        /// 令牌值
        /// </summary>
        [SugarColumn(IsNullable = false, Length = 255)]
        public string Token { get; set; } = string.Empty;
        
        /// <summary>
        /// 过期时间（默认14天）
        /// </summary>
        [SugarColumn(IsNullable = false)]
        public DateTime ExpiresAt { get; set; }
        
        /// <summary>
        /// 是否已撤销
        /// </summary>
        [SugarColumn(IsNullable = false)]
        public bool IsRevoked { get; set; }
        
        /// <summary>
        /// IP地址
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 50)]
        public string? IpAddress { get; set; }
        
        /// <summary>
        /// 用户代理信息
        /// </summary>
        [SugarColumn(IsNullable = true, Length = 500)]
        public string? UserAgent { get; set; }
        
        /// <summary>
        /// 关联用户（一对一导航属性）
        /// </summary>
        [Navigate(NavigateType.OneToOne, nameof(UserGUID), nameof(User.UserGUID))]
        public User User { get; set; } = null!;
    }
}