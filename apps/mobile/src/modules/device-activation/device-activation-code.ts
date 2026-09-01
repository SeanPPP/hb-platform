const asciiWhitespacePattern = /[\u0009-\u000d\u0020]/gu;
const nonAsciiPattern = /[^\u0000-\u007f]/u;
const deviceActivationCodePattern =
  /^HBDEV1-[0-9A-HJKMNP-TV-Z]{26}-[0-9A-HJKMNP-TV-Z]{26}$/u;

/** 只执行协议允许的规范化；非 ASCII 字符必须在大小写转换前被拒绝。 */
export function normalizeDeviceActivationCode(value: string): string {
  return value.replace(asciiWhitespacePattern, "").toUpperCase();
}

export function parseDeviceActivationCode(value: string): string | null {
  if (nonAsciiPattern.test(value)) {
    return null;
  }

  const normalized = normalizeDeviceActivationCode(value);
  return deviceActivationCodePattern.test(normalized) ? normalized : null;
}
