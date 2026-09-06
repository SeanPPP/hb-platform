using System.Security.Cryptography;
using Hbpos.RemoteMaintenance.Setup;

namespace Hbpos.RemoteStatus.Tests;

public sealed class RemoteMaintenanceArtifactDownloaderTests
{
    [Fact]
    public async Task DownloadAndVerify_rejects_size_or_hash_mismatch_and_leaves_no_partial_file()
    {
        var bytes = new byte[] { 1, 2, 3, 4 };
        var root = Path.Combine(Path.GetTempPath(), "hbpos-remote-test-" + Guid.NewGuid().ToString("N"));
        var api = new FakeApiClient(bytes);
        try
        {
            var downloader = new RemoteMaintenanceArtifactDownloader(api);
            var artifact = new RemoteMaintenanceArtifact("1", "agent.exe", "/artifact", "00".PadLeft(64, '0'), bytes.Length + 1);

            await Assert.ThrowsAsync<InvalidDataException>(() => downloader.DownloadAndVerifyAsync(artifact, root));
            Assert.Empty(Directory.Exists(root) ? Directory.EnumerateFiles(root) : []);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadAndVerify_accepts_exact_size_and_sha256()
    {
        var bytes = new byte[] { 5, 6, 7 };
        var root = Path.Combine(Path.GetTempPath(), "hbpos-remote-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            var downloader = new RemoteMaintenanceArtifactDownloader(new FakeApiClient(bytes));
            var path = await downloader.DownloadAndVerifyAsync(
                new RemoteMaintenanceArtifact("1", "agent.exe", "/artifact", hash, bytes.Length), root);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private sealed class FakeApiClient(byte[] bytes) : IRemoteMaintenanceApiClient
    {
        public Task<RemoteMaintenancePrepareResponse> PrepareAsync(RemoteMaintenancePrepareRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RemoteMaintenanceCommitResponse> CommitAsync(RemoteMaintenanceCommitRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Stream> DownloadArtifactAsync(string downloadUrl, CancellationToken cancellationToken = default) => Task.FromResult<Stream>(new MemoryStream(bytes, writable: false));
    }
}
