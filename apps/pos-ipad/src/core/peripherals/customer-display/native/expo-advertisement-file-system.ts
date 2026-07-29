import { Directory, File } from "expo-file-system";

import type { AdvertisementCacheFileSystemPort } from "@/features/customer-display";

export class ExpoAdvertisementFileSystem
  implements AdvertisementCacheFileSystemPort
{
  public async ensureDirectory(uri: string): Promise<void> {
    new Directory(uri).create({
      idempotent: true,
      intermediates: true,
    });
  }

  public async getSize(uri: string): Promise<number | null> {
    const file = new File(uri);
    return file.exists ? file.size : null;
  }

  public async download(
    remoteUrl: string,
    destinationUri: string,
  ): Promise<void> {
    await File.downloadFileAsync(
      remoteUrl,
      new File(destinationUri),
      { idempotent: true },
    );
  }

  public async move(
    sourceUri: string,
    destinationUri: string,
  ): Promise<void> {
    const destination = new File(destinationUri);
    if (destination.exists) destination.delete();
    new File(sourceUri).move(destination);
  }

  public async deleteIfExists(uri: string): Promise<void> {
    const file = new File(uri);
    if (file.exists) file.delete();
  }

  public async listFiles(rootUri: string): Promise<readonly string[]> {
    const directory = new Directory(rootUri);
    if (!directory.exists) return [];
    return Object.freeze(
      directory
        .list()
        .filter((entry): entry is File => entry instanceof File)
        .map((file) => file.uri),
    );
  }
}
