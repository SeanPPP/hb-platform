using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Hbpos.RemoteMaintenance.Setup;

public enum RemoteMaintenanceOperationState
{
    Prepared,
    InstalledPendingCommit,
    Committed,
    Failed
}

public sealed record RemoteMaintenanceJournalState(
    Guid OperationId,
    Guid DeviceId,
    RemoteMaintenanceOperationState State,
    string RustdeskId,
    string ClientVersion,
    string ProtectedPassword,
    string? ProtectedMonitorToken,
    string? HeartbeatUrl,
    DateTimeOffset UpdatedAtUtc,
    RemoteMaintenanceConfig? Config = null,
    string? RustDeskArtifactPath = null,
    string? StatusAgentArtifactPath = null,
    string? DataDirectory = null,
    RemoteMaintenanceArtifactManifest? ArtifactManifest = null);

public interface IRemoteMaintenanceSecretProtector
{
    string Protect(string plaintext);

    string? Unprotect(string protectedValue);
}

public sealed class DpapiRemoteMaintenanceSecretProtector : IRemoteMaintenanceSecretProtector
{
    public string Protect(string plaintext)
    {
        var bytes = Encoding.UTF8.GetBytes(plaintext);
        try
        {
            return Convert.ToBase64String(ProtectedData.Protect(bytes, null, DataProtectionScope.LocalMachine));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    public string? Unprotect(string protectedValue)
    {
        try
        {
            var protectedBytes = Convert.FromBase64String(protectedValue);
            try
            {
                var bytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.LocalMachine);
                try { return Encoding.UTF8.GetString(bytes); }
                finally { CryptographicOperations.ZeroMemory(bytes); }
            }
            finally { CryptographicOperations.ZeroMemory(protectedBytes); }
        }
        catch (FormatException) { return null; }
        catch (CryptographicException) { return null; }
    }
}

public sealed class RemoteMaintenanceJournal(
    string filePath,
    IRemoteMaintenanceSecretProtector protector)
{
    public string FilePath { get; } = Path.GetFullPath(filePath);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<RemoteMaintenanceJournalState?> ReadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(FilePath)) return null;
            var state = JsonSerializer.Deserialize<RemoteMaintenanceJournalState>(
                await File.ReadAllTextAsync(FilePath, cancellationToken), JsonOptions);
            return state is null || protector.Unprotect(state.ProtectedPassword) is null ||
                (state.ProtectedMonitorToken is not null && protector.Unprotect(state.ProtectedMonitorToken) is null)
                ? null
                : state;
        }
        finally { _gate.Release(); }
    }

    public async Task WriteAsync(
        RemoteMaintenanceJournalState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(state.ProtectedPassword);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath) ?? ".");
            var temporary = FilePath + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                var json = JsonSerializer.Serialize(state, JsonOptions);
                await File.WriteAllTextAsync(temporary, json, cancellationToken);
                File.Move(temporary, FilePath, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        finally { _gate.Release(); }
    }
}

public static class RemoteMaintenancePasswordGenerator
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";

    public static string Create(int length = 20)
    {
        if (length < 12) throw new ArgumentOutOfRangeException(nameof(length));
        Span<byte> bytes = stackalloc byte[length];
        RandomNumberGenerator.Fill(bytes);
        Span<char> chars = stackalloc char[length];
        for (var index = 0; index < chars.Length; index++) chars[index] = Alphabet[bytes[index] % Alphabet.Length];
        CryptographicOperations.ZeroMemory(bytes);
        return new string(chars);
    }
}
