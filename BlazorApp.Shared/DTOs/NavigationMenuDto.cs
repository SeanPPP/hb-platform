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
}
