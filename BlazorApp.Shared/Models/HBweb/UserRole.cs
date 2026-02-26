using SqlSugar;

namespace BlazorApp.Shared.Models
{
    public class UserRole : BaseEntity
    {
        [SugarColumn(IsPrimaryKey = true, IsNullable = false)]
        public string UserRoleGUID { get; set; } = Guid.NewGuid().ToString();

        [SugarColumn(IsNullable = false)]
        public string UserGUID { get; set; } = string.Empty;

        [SugarColumn(IsNullable = false)]
        public string RoleGUID { get; set; } = string.Empty;

        [SugarColumn(IsNullable = false)]
        public DateTime AssignedAt { get; set; } = DateTime.UtcNow;

        [SugarColumn(IsNullable = true)]
        public string? AssignedByGUID { get; set; }
        [SugarColumn(IsNullable = true)]
        public User? AssignedBy { get; set; }
        
         // SqlSugar 导航属性
        [Navigate(NavigateType.OneToOne, nameof(UserGUID), nameof(User.UserGUID))]
        public User? User { get; set; }
        
        [Navigate(NavigateType.OneToOne, nameof(RoleGUID), nameof(Role.RoleGUID))]
        public Role? Role { get; set; }
       
    }
} 