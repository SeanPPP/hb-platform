export type FaceCameraCacheEntry = Readonly<{ uri: string; delete(): void }>;

/** 当前 iPad 只有人脸功能调用 takePictureAsync；Expo 17 将拍照缓存写为 Camera/<UUID>.jpg。 */
export function cleanupFaceCameraFiles(cacheRoot: string, list: () => readonly FaceCameraCacheEntry[]): boolean {
  let clean = true;
  try {
    for (const item of list()) {
      const relative = item.uri.startsWith(cacheRoot) ? item.uri.slice(cacheRoot.length) : "";
      if (!/^Camera\/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\.jpg$/iu.test(relative)) continue;
      try { item.delete(); } catch { clean = false; }
    }
  } catch { clean = false; }
  return clean;
}
