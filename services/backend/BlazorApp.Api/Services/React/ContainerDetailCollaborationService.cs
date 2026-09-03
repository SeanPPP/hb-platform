using System.Security.Cryptography;
using System.Text;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.Extensions.Configuration;

namespace BlazorApp.Api.Services.React;

/// <summary>协作状态仅作界面提醒；保存正确性始终由字段令牌和事务校验保证。</summary>
public sealed class ContainerDetailCollaborationService(
    SqlSugarContext context,
    ICurrentUserService currentUserService,
    IConfiguration configuration
) : IContainerDetailCollaborationService
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(90);

    public async Task<ContainerDetailPresenceDto> HeartbeatAsync(
        string containerGuid,
        ContainerDetailPresenceHeartbeatDto request
    )
    {
        if (!IsEnabled()) return new ContainerDetailPresenceDto();
        if (string.IsNullOrWhiteSpace(request.ClientSessionId) || request.ClientSessionId.Trim().Length > 128)
            throw new InvalidOperationException("客户端会话不能为空");
        var state = request.State == "editing" ? "editing" : "viewing";
        var now = DateTime.UtcNow;
        await context.Db.Deleteable<ContainerDetailEditLease>()
            .Where(lease => lease.ExpiresAtUtc < now)
            .ExecuteCommandAsync();
        var userGuid = currentUserService.GetCurrentUserGuid()?.Trim();
        if (string.IsNullOrWhiteSpace(userGuid)) throw new InvalidOperationException("当前用户标识不能为空");
        var key = CreateLeaseKey(containerGuid, userGuid, request.ClientSessionId);
        var lease = await context.Db.Queryable<ContainerDetailEditLease>()
            .SingleAsync(item => item.LeaseKey == key);
        if (lease == null)
        {
            await context.Db.Insertable(new ContainerDetailEditLease
            {
                LeaseKey = key,
                ContainerGuid = containerGuid,
                UserGuid = userGuid,
                UserName = currentUserService.GetCurrentUsername() ?? string.Empty,
                ClientSessionId = request.ClientSessionId.Trim(),
                State = state,
                LastActiveAtUtc = now,
                ExpiresAtUtc = now.Add(LeaseDuration),
            }).ExecuteCommandAsync();
        }
        else
        {
            lease.State = state;
            lease.LastActiveAtUtc = now;
            lease.ExpiresAtUtc = now.Add(LeaseDuration);
            await context.Db.Updateable(lease)
                .UpdateColumns(item => new { item.State, item.LastActiveAtUtc, item.ExpiresAtUtc })
                .WhereColumns(item => item.LeaseKey)
                .ExecuteCommandAsync();
        }
        return await GetActiveUsersAsync(containerGuid);
    }

    public async Task<ContainerDetailPresenceDto> GetActiveUsersAsync(string containerGuid)
    {
        if (!IsEnabled()) return new ContainerDetailPresenceDto();
        var now = DateTime.UtcNow;
        var currentUserGuid = currentUserService.GetCurrentUserGuid() ?? string.Empty;
        var leases = await context.Db.Queryable<ContainerDetailEditLease>()
            .Where(lease => lease.ContainerGuid == containerGuid && lease.ExpiresAtUtc >= now && lease.UserGuid != currentUserGuid)
            .ToListAsync();
        var users = leases.GroupBy(lease => lease.UserGuid, StringComparer.Ordinal)
            .Select(group => new ContainerDetailPresenceUserDto
            {
                UserGuid = group.Key,
                UserName = group.OrderByDescending(lease => lease.LastActiveAtUtc).First().UserName,
                LastActiveAt = group.Max(lease => lease.LastActiveAtUtc),
            }).ToList();
        var editingUsers = leases.Where(lease => lease.State == "editing")
            .Select(lease => lease.UserGuid).ToHashSet(StringComparer.Ordinal);
        return new ContainerDetailPresenceDto
        {
            Editors = users.Where(user => editingUsers.Contains(user.UserGuid)).ToList(),
            Viewers = users.Where(user => !editingUsers.Contains(user.UserGuid)).ToList(),
        };
    }

    public async Task LeaveAsync(string containerGuid, string clientSessionId)
    {
        if (!IsEnabled() || string.IsNullOrWhiteSpace(clientSessionId) || clientSessionId.Trim().Length > 128) return;
        var userGuid = currentUserService.GetCurrentUserGuid()?.Trim();
        if (string.IsNullOrWhiteSpace(userGuid)) return;
        await context.Db.Deleteable<ContainerDetailEditLease>()
            .Where(lease => lease.LeaseKey == CreateLeaseKey(containerGuid, userGuid, clientSessionId.Trim()))
            .ExecuteCommandAsync();
    }

    private bool IsEnabled() => configuration.GetValue<bool>("ContainerDetailCollaboration:PresenceEnabled");

    private static string CreateLeaseKey(string containerGuid, string userGuid, string sessionId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{containerGuid}|{userGuid}|{sessionId}"))).ToLowerInvariant();
}
