using SqlSugar;

namespace BlazorApp.Shared.Models
{
    public class UserStore : BaseEntity
    {
        [SugarColumn(IsPrimaryKey = true, IsNullable = false)]
        public string UserStoreGUID { get; set; } = Guid.NewGuid().ToString();
        
        [SugarColumn(IsNullable = false)]
        public string UserGUID { get; set; } = string.Empty;
        
        [SugarColumn(IsNullable = false)]
        public string StoreGUID { get; set; } = string.Empty;
        
        [SugarColumn(IsNullable = false)]
        public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
        
        [SugarColumn(IsNullable = false)]
        public bool IsPrimary { get; set; } = false;
        
        [SugarColumn(IsNullable = true)]
        public string? AssignedByGUID { get; set; }
        
        // SqlSugar 导航属性
        [Navigate(NavigateType.OneToOne, nameof(UserGUID), nameof(User.UserGUID))]
        public User User { get; set; } = null!;
        
        [Navigate(NavigateType.OneToOne, nameof(StoreGUID), nameof(Store.StoreGUID))]
        public Store Store { get; set; } = null!;
        
        [Navigate(NavigateType.OneToOne, nameof(AssignedByGUID), nameof(User.UserGUID))]
        public User? AssignedBy { get; set; }
    }
} 