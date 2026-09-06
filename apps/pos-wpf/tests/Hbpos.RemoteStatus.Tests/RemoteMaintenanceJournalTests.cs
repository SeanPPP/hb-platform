using Hbpos.RemoteMaintenance.Setup;

namespace Hbpos.RemoteStatus.Tests;

public sealed class RemoteMaintenanceJournalTests
{
    [Fact]
    public async Task Journal_persists_only_protected_values_and_round_trips_operation_state()
    {
        var root = Path.Combine(Path.GetTempPath(), "hbpos-journal-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "journal.json");
        var protector = new PrefixProtector();
        try
        {
            var journal = new RemoteMaintenanceJournal(path, protector);
            var state = new RemoteMaintenanceJournalState(
                Guid.NewGuid(),
                Guid.NewGuid(),
                RemoteMaintenanceOperationState.InstalledPendingCommit,
                "123",
                "1.4.9",
                protector.Protect("password-value"),
                null,
                null,
                DateTimeOffset.UtcNow);
            await journal.WriteAsync(state);

            var json = await File.ReadAllTextAsync(path);
            Assert.DoesNotContain("password-value", json, StringComparison.Ordinal);
            Assert.Equal(state, await journal.ReadAsync());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private sealed class PrefixProtector : IRemoteMaintenanceSecretProtector
    {
        public string Protect(string plaintext) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(plaintext));
        public string? Unprotect(string protectedValue)
        {
            try { return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(protectedValue)); }
            catch (FormatException) { return null; }
        }
    }
}
