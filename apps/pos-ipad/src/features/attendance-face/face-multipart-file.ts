import { Directory, File, Paths } from "expo-file-system";

import { cleanupFaceMultipartFiles, type FaceCacheFile } from "./face-multipart-cleanup";

export type FaceMultipartFile = Readonly<{ uri: string; name: string; type: "image/jpeg" }>;
export type FaceMultipartFilePort = Readonly<{
  create(photoBase64: string): Promise<Readonly<{ file: FaceMultipartFile; release(): Promise<void> }>>;
}>;

/** React Native Axios 只把 uri/name/type 识别为 multipart 文件；Blob 会被错误字符串化。 */
export class ExpoFaceMultipartFilePort implements FaceMultipartFilePort {
  public constructor(private readonly listCacheFiles: () => readonly FaceCacheFile[] = () => {
    const cache = new Directory(Paths.cache);
    if (!cache.exists) return [];
    return cache.list().filter((item): item is File => item instanceof File);
  }) {}

  /** 仅回收本模块固定命名前缀的崩溃遗留 JPEG，绝不扫描或清理其他 camera/cache 内容。 */
  public async cleanupStale(): Promise<void> {
    cleanupFaceMultipartFiles(Paths.cache.uri, this.listCacheFiles);
  }

  public async create(photoBase64: string): Promise<Readonly<{ file: FaceMultipartFile; release(): Promise<void> }>> {
    const name = `face-${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}.jpg`;
    const file = new File(Paths.cache, name);
    try {
      // 新 File API 写入真实 JPEG bytes，避免 legacy FileSystem 类型问题和把 base64 当文本落盘。
      file.write(decodeBase64(photoBase64));
    } catch (error) {
      try { if (file.exists) file.delete(); } catch { /* 原始照片不能因清理失败暴露为成功。 */ }
      throw error;
    }
    let released = false;
    return Object.freeze({
      file: Object.freeze({ uri: file.uri, name, type: "image/jpeg" }),
      release: async () => {
        if (released) return;
        released = true;
        // 只删除本模块刚创建的 cache 文件，上传成功、失败和取消均清理。
        if (file.exists) file.delete();
      },
    });
  }
}

function decodeBase64(value: string): Uint8Array {
  if (typeof globalThis.atob !== "function") throw new Error("FACE_PHOTO_DECODE_UNAVAILABLE");
  const binary = globalThis.atob(value);
  const bytes = new Uint8Array(binary.length);
  for (let index = 0; index < binary.length; index += 1) bytes[index] = binary.charCodeAt(index);
  return bytes;
}
