using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces.React;

public interface IContainerDetailCollaborationService
{
    Task<ContainerDetailPresenceDto> HeartbeatAsync(
        string containerGuid,
        ContainerDetailPresenceHeartbeatDto request
    );
    Task<ContainerDetailPresenceDto> GetActiveUsersAsync(string containerGuid);
    Task LeaveAsync(string containerGuid, string clientSessionId);
}
