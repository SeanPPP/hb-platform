const asciiWhitespacePattern = /[\u0009-\u000d\u0020]/gu;
const nonAsciiPattern = /[^\u0000-\u007f]/u;
const deviceActivationCodePattern =
  /^HBDEV1-[0-9A-HJKMNP-TV-Z]{26}-[0-9A-HJKMNP-TV-Z]{26}$/u;

/** 只做协议允许的规范化；非 ASCII 空白及 Crockford 歧义字符继续拒绝。 */
export function normalizeDeviceActivationCode(value: string): string {
  return value.replace(asciiWhitespacePattern, "").toUpperCase();
}

export function parseDeviceActivationCode(value: string): string | null {
  // 必须先拒绝 Unicode；否则 toUpperCase 可能把 ſ 等字符折叠成合法 ASCII。
  if (nonAsciiPattern.test(value)) return null;
  const normalized = normalizeDeviceActivationCode(value);
  return deviceActivationCodePattern.test(normalized) ? normalized : null;
}
