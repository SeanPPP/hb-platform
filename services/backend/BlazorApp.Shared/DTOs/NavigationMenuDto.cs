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
        /// <summary>
        /// 精确权限节点标记。为 true 时仅按精确权限代码判定可见性，不展开别名。
        /// </summary>
        public bool RequireExactPermission { get; set; }
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
