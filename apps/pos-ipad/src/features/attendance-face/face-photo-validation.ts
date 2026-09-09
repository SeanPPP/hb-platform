/** gateway 总体限制更大，但每张脸必须匹配 worker 的 2MiB JPEG 上限。 */
export function assertFaceJpegSize(value: string): void {
  if (value.length < 8 || !value.startsWith("/9j/") || value.length % 4 !== 0 || !/^(?:[A-Za-z0-9+/]{4})*(?:[A-Za-z0-9+/]{2}==|[A-Za-z0-9+/]{3}=)?$/u.test(value)) throw new Error("FACE_PHOTO_INVALID");
  const padding = value.endsWith("==") ? 2 : value.endsWith("=") ? 1 : 0;
  if (value.length / 4 * 3 - padding > 2 * 1024 * 1024) throw new Error("FACE_PHOTO_TOO_LARGE");
}
