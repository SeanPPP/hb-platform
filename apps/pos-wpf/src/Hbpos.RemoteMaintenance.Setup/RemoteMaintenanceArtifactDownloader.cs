using System.Security.Cryptography;

namespace Hbpos.RemoteMaintenance.Setup;

public sealed class RemoteMaintenanceArtifactDownloader(
    IRemoteMaintenanceApiClient apiClient) : IRemoteMaintenanceArtifactDownloader
{
    public async Task<string> DownloadAndVerifyAsync(
        RemoteMaintenanceArtifact artifact,
        string destinationDirectory,
        CancellationToken cancellationToken = default)
    {
        ValidateArtifact(artifact);
        Directory.CreateDirectory(destinationDirectory);
        var destination = Path.Combine(destinationDirectory, Path.GetFileName(artifact.FileName));
        var temporary = destination + ".partial-" + Guid.NewGuid().ToString("N");
        try
        {
            await using var source = await apiClient.DownloadArtifactAsync(artifact.DownloadUrl, cancellationToken);
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[64 * 1024];
                long total = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    total = checked(total + read);
                    if (total > artifact.SizeBytes)
                    {
                        throw new InvalidDataException("远程维护文件大小超出清单。");
                    }

                    hash.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }

                await output.FlushAsync(cancellationToken);
                if (total != artifact.SizeBytes ||
                    !CryptographicOperations.FixedTimeEquals(
                        hash.GetHashAndReset(),
                        Convert.FromHexString(artifact.Sha256)))
                {
                    throw new InvalidDataException("远程维护文件校验失败。");
                }
            }

            File.Move(temporary, destination, overwrite: true);
            return destination;
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static void ValidateArtifact(RemoteMaintenanceArtifact artifact)
    {
        if (artifact.SizeBytes <= 0 || string.IsNullOrWhiteSpace(artifact.FileName) ||
            Path.GetFileName(artifact.FileName) != artifact.FileName ||
            artifact.FileName.Contains(Path.DirectorySeparatorChar) ||
            artifact.FileName.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new InvalidDataException("远程维护文件清单无效。");
        }

        if (artifact.Sha256.Length != 64 || artifact.Sha256.Any(c => !Uri.IsHexDigit(c)))
        {
            throw new InvalidDataException("远程维护文件哈希无效。");
        }
    }
}
