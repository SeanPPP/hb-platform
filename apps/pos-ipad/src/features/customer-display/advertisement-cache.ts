import type { CustomerDisplayAdvertisementItem } from "./advertisement-api";
import type {
  CachedCustomerDisplayAdvertisement,
  CustomerDisplayAdvertisementCachePort,
} from "./advertisement-playback";

import { normalizeAdvertisementCacheRootUri } from "@/core/peripherals/customer-display/local-advertisement-uri";

const ALLOWED_EXTENSIONS = new Set([
  ".jpg",
  ".jpeg",
  ".png",
  ".webp",
  ".gif",
  ".mp4",
  ".webm",
  ".mov",
]);

const CONTENT_TYPE_EXTENSIONS: Readonly<Record<string, string>> = {
  "image/jpeg": ".jpg",
  "image/png": ".png",
  "image/webp": ".webp",
  "image/gif": ".gif",
  "video/mp4": ".mp4",
  "video/webm": ".webm",
  "video/quicktime": ".mov",
};

export interface AdvertisementCacheFileSystemPort {
  ensureDirectory(uri: string): Promise<void>;
  getSize(uri: string): Promise<number | null>;
  download(remoteUrl: string, destinationUri: string): Promise<void>;
  move(sourceUri: string, destinationUri: string): Promise<void>;
  deleteIfExists(uri: string): Promise<void>;
  listFiles(rootUri: string): Promise<readonly string[]>;
}

export type CustomerDisplayAdvertisementCacheOptions = Readonly<{
  rootUri: string;
  files: AdvertisementCacheFileSystemPort;
  sha256Hex(material: string): Promise<string>;
}>;

/**
 * 下载只写入 `.download` 临时文件，精确大小校验通过后才移动为正式素材。
 * 单个坏素材会被跳过；当前有效集合之外的文件以 best-effort 清理。
 */
export class CustomerDisplayAdvertisementCache
  implements CustomerDisplayAdvertisementCachePort
{
  private readonly rootUri: string;

  public constructor(
    private readonly options: CustomerDisplayAdvertisementCacheOptions,
  ) {
    const normalized = normalizeAdvertisementCacheRootUri(
      options.rootUri,
    );
    this.rootUri = normalized.endsWith("/")
      ? normalized
      : `${normalized}/`;
  }

  public async cache(
    items: readonly CustomerDisplayAdvertisementItem[],
  ): Promise<readonly CachedCustomerDisplayAdvertisement[]> {
    await this.options.files.ensureDirectory(this.rootUri);
    const cached: CachedCustomerDisplayAdvertisement[] = [];
    const retained = new Set<string>();
    for (const item of items) {
      const result = await this.cacheOne(item);
      if (!result) continue;
      cached.push(result);
      retained.add(result.localUri);
    }
    await this.cleanup(retained);
    return Object.freeze(cached);
  }

  private async cacheOne(
    item: CustomerDisplayAdvertisementItem,
  ): Promise<CachedCustomerDisplayAdvertisement | null> {
    const hash = await this.options.sha256Hex(item.remoteUrl);
    if (!/^[a-f0-9]{64}$/iu.test(hash)) {
      return null;
    }
    const fileName = `${hash.toLowerCase()}${resolveExtension(item)}`;
    const finalUri = new URL(fileName, this.rootUri).toString();
    const tempUri = `${finalUri}.download`;
    try {
      const existingSize = await this.options.files.getSize(finalUri);
      if (existingSize === item.fileSize) {
        return Object.freeze({ ...item, localUri: finalUri });
      }
      if (existingSize !== null) {
        await this.options.files.deleteIfExists(finalUri);
      }
      await this.options.files.deleteIfExists(tempUri);
      await this.options.files.download(item.remoteUrl, tempUri);
      if (
        (await this.options.files.getSize(tempUri)) !== item.fileSize
      ) {
        await this.options.files.deleteIfExists(tempUri);
        return null;
      }
      await this.options.files.move(tempUri, finalUri);
      if (
        (await this.options.files.getSize(finalUri)) !== item.fileSize
      ) {
        await this.options.files.deleteIfExists(finalUri);
        return null;
      }
      return Object.freeze({ ...item, localUri: finalUri });
    } catch {
      try {
        await this.options.files.deleteIfExists(tempUri);
      } catch {
        // 单项清理失败不应阻断其他素材进入本地缓存。
      }
      return null;
    }
  }

  private async cleanup(retained: ReadonlySet<string>): Promise<void> {
    let current: readonly string[];
    try {
      current = await this.options.files.listFiles(this.rootUri);
    } catch {
      return;
    }
    for (const uri of current) {
      if (retained.has(uri)) continue;
      try {
        await this.options.files.deleteIfExists(uri);
      } catch {
        // 过期文件清理是 best-effort，不能撤销已经验证的当前素材快照。
      }
    }
  }
}

function resolveExtension(
  item: CustomerDisplayAdvertisementItem,
): string {
  const candidates = [
    extensionOf(item.objectKey),
    extensionOf(item.originalFileName),
    extensionOf(new URL(item.remoteUrl).pathname),
  ];
  for (const candidate of candidates) {
    if (ALLOWED_EXTENSIONS.has(candidate)) {
      return candidate === ".jpeg" ? ".jpg" : candidate;
    }
  }
  return CONTENT_TYPE_EXTENSIONS[item.contentType.toLowerCase()] ?? ".bin";
}

function extensionOf(value: string): string {
  const fileName = value.split("/").at(-1) ?? "";
  const dot = fileName.lastIndexOf(".");
  return dot < 0 ? "" : fileName.slice(dot).toLowerCase();
}
