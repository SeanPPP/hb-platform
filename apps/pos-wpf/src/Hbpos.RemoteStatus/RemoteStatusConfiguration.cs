using System.Security.Cryptography;
using System.Text.Json;

namespace Hbpos.RemoteStatus;

internal sealed record PersistedRemoteStatusConfiguration(
    string AgentVersion,
    string RustDeskId,
    string ClientVersion,
    string HeartbeatUrl,
    string ProtectedMonitorToken);

internal sealed class RemoteStatusConfiguration
{
    public required RemoteStatusAgentOptions Options { get; init; }

    public static RemoteStatusConfiguration Load(string dataDirectory)
    {
        var path = Path.Combine(dataDirectory, "agent.json");
        if (!File.Exists(path))
        {
            throw new InvalidOperationException("HBPOS Remote Status 配置不存在。");
        }

        var persisted = JsonSerializer.Deserialize<PersistedRemoteStatusConfiguration>(File.ReadAllText(path), new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException("HBPOS Remote Status 配置无效。");
        if (!Uri.TryCreate(persisted.HeartbeatUrl, UriKind.Absolute, out var heartbeatUri) ||
            heartbeatUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidDataException("HBPOS Remote Status 心跳地址必须是 HTTPS。");
        }
        var protectedToken = Convert.FromBase64String(persisted.ProtectedMonitorToken);
        var tokenBytes = ProtectedData.Unprotect(protectedToken, optionalEntropy: null, DataProtectionScope.LocalMachine);
        try
        {
            var token = System.Text.Encoding.UTF8.GetString(tokenBytes);
            return new RemoteStatusConfiguration
            {
                Options = new RemoteStatusAgentOptions(
                    persisted.AgentVersion,
                    persisted.RustDeskId,
                    persisted.ClientVersion,
                    persisted.HeartbeatUrl,
                    token,
                    dataDirectory,
                    RemoteStatusAgentOptions.DefaultHeartbeatPeriod)
            };
        }
        finally
        {
            CryptographicOperations.ZeroMemory(tokenBytes);
            CryptographicOperations.ZeroMemory(protectedToken);
        }
    }
}
