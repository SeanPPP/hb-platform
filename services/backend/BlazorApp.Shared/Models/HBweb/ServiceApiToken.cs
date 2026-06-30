using BlazorApp.Shared.Models;
using SqlSugar;

namespace BlazorApp.Shared.Models.HBweb
{
    /// <summary>
    /// 后台自动化专用 API Token。明文只在创建时返回一次，数据库只保存哈希。
    /// </summary>
    [SugarTable("ServiceApiToken")]
    public class ServiceApiToken : BaseEntity
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = false)]
        public Guid Id { get; set; } = Guid.NewGuid();

        [SugarColumn(Length = 120, IsNullable = false)]
        public string Name { get; set; } = string.Empty;

        [SugarColumn(Length = 64, IsNullable = false)]
        public string TokenHash { get; set; } = string.Empty;

        [SugarColumn(Length = 32, IsNullable = false)]
        public string TokenPrefix { get; set; } = string.Empty;

        [SugarColumn(Length = 500, IsNullable = false)]
        public string Scopes { get; set; } = string.Empty;

        [SugarColumn(IsNullable = true)]
        public DateTime? ExpiresAt { get; set; }

        [SugarColumn(IsNullable = true)]
        public DateTime? RevokedAt { get; set; }

        [SugarColumn(Length = 120, IsNullable = true)]
        public string? RevokedBy { get; set; }

        [SugarColumn(IsNullable = true)]
        public DateTime? LastUsedAt { get; set; }

        [SugarColumn(Length = 64, IsNullable = true)]
        public string? LastUsedIp { get; set; }
    }
}
