import type { CameraView } from "expo-camera";
import { Directory, File, Paths } from "expo-file-system";

import { cleanupFaceCameraFiles } from "./face-camera-cleanup";

/** 启动时恢复清理相机写盘到JS返回之间崩溃的UUID照片；不影响POS启动或其他缓存。 */
export function cleanupStaleFaceCameraFiles(): boolean {
  return cleanupFaceCameraFiles(Paths.cache.uri, () => {
    const directory = new Directory(Paths.cache, "Camera");
    return directory.exists ? directory.list().filter((item): item is File => item instanceof File) : [];
  });
}

export async function captureFacePhoto(camera: Pick<CameraView, "takePictureAsync">,
  captureTime: () => Readonly<{ capturedAtUtc: string; capturedUptimeMilliseconds: number }>) {
  if (!cleanupStaleFaceCameraFiles()) throw new Error("FACE_CAMERA_CLEANUP_FAILED");
  const timestamp = captureTime();
  const picture = await camera.takePictureAsync({ base64: true, quality: 0.55, skipProcessing: false });
  if (!picture?.uri.startsWith(Paths.cache.uri)) throw new Error("FACE_CAMERA_CACHE_SCOPE_INVALID");
  // 尽早销毁原生临时JPEG，只将内存中的压缩照片交给SQLCipher事务；失败时绝不显示已记录。
  try {
    const file = new File(picture.uri);
    if (file.exists) file.delete();
  } catch { throw new Error("FACE_CAMERA_CLEANUP_FAILED"); }
  if (!picture.base64) throw new Error("FACE_INVALID_PHOTO");
  return { photoBase64: picture.base64, ...timestamp, release: async () => undefined };
}
