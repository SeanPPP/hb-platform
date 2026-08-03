export function resolveExpoUpdateRuntimeVersion(
  runtimeVersion: string | null | undefined,
  appVersion: string,
): string {
  // expo-updates 禁用时 iOS 原生模块会导出空字符串，不能让它绕过 appVersion 回退。
  const normalizedRuntimeVersion = runtimeVersion?.trim();
  const selected = normalizedRuntimeVersion || appVersion.trim();
  if (
    selected.length === 0 ||
    selected.length > 120 ||
    !/^[A-Za-z0-9][A-Za-z0-9._/-]*$/u.test(selected)
  ) {
    throw new TypeError("iPad OTA update runtimeVersion is invalid.");
  }
  return selected;
}
