using System.ComponentModel.DataAnnotations;

namespace BlazorApp.Shared.DTOs;

public sealed class PosTerminalAssignablePermissionDto
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Group { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public sealed class UserStorePosTerminalPermissionsResponse
{
    public string Mode { get; set; } = "Inherited";
    public List<PosTerminalAssignablePermissionDto> AssignablePermissions { get; set; } = new();
    public List<string> InheritedPermissionCodes { get; set; } = new();
    public List<string> OverriddenPermissionCodes { get; set; } = new();
    public List<string> GrantedPermissionCodes { get; set; } = new();
    public List<string> EffectivePermissionCodes { get; set; } = new();
}

public sealed class UpdateUserStorePosTerminalPermissionsRequest
{
    [Required(ErrorMessage = "授权权限列表不能为空")]
    public List<string> GrantedPermissionCodes { get; set; } = new();
}
