using System.Globalization;

namespace Hbpos.RemoteStatus;

public sealed class FileRemoteStatusSequenceStore(string filePath) : IRemoteStatusSequenceStore
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<long> AllocateNextAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? ".");
            var current = 0L;
            if (File.Exists(filePath))
            {
                var text = await File.ReadAllTextAsync(filePath, cancellationToken);
                if (!long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out current) || current < 0)
                {
                    throw new InvalidDataException("心跳序号文件无效。");
                }
            }

            checked { current++; }
            var tempPath = filePath + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                await using (var stream = new FileStream(
                    tempPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 128,
                    options: FileOptions.Asynchronous | FileOptions.WriteThrough))
                await using (var writer = new StreamWriter(stream, leaveOpen: true))
                {
                    await writer.WriteAsync(current.ToString(CultureInfo.InvariantCulture).AsMemory(), cancellationToken);
                    await writer.FlushAsync(cancellationToken);
                    await stream.FlushAsync(cancellationToken);
                    stream.Flush(flushToDisk: true);
                }
                File.Move(tempPath, filePath, overwrite: true);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }

            return current;
        }
        finally
        {
            _gate.Release();
        }
    }
}
