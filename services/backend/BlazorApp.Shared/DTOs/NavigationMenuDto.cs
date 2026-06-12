namespace BlazorApp.Shared.DTOs
{
    /// <summary>
    /// 导航菜单节点
    /// </summary>
    public class NavigationMenuDto
    {
        public string Path { get; set; } = string.Empty;
        public string TitleKey { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;
        public string? Permission { get; set; }
        public bool RequireAdmin { get; set; }
        public List<NavigationMenuDto>? Children { get; set; }
    }

    /// <summary>
    /// Expo app 底部导航节点
    /// </summary>
    public class AppNavigationMenuDto
    {
        public string RouteName { get; set; } = string.Empty;
        public string TitleKey { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;
        public string? Permission { get; set; }
        public int Order { get; set; }
    }
}
