export type FaceCacheFile = Readonly<{ uri: string; delete(): void }>;

/** 清理范围严格限于 face-multipart 自身 prefix；枚举故障绝不能阻断 POS 启动。 */
export function cleanupFaceMultipartFiles(cacheRootUri: string, list: () => readonly FaceCacheFile[]): void {
  try {
    for (const item of list()) {
      if (item.uri.startsWith(`${cacheRootUri}face-`) && item.uri.endsWith(".jpg")) {
        try { item.delete(); } catch { /* 单个文件失败不扩散到其他遗留项。 */ }
      }
    }
  } catch { /* cache 不可读时不阻断 POS 启动，下次启动再清理。 */ }
}
