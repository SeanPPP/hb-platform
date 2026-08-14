const MAX_PRODUCT_IMAGE_URI_LENGTH = 2_048;
const CONTROL_CHARACTER_PATTERN = /[\u0000-\u001f\u007f-\u009f]/u;

/**
 * 将目录返回的商品图片地址解析为可安全交给原生图片组件的 HTTP(S) URI。
 *
 * 外部来源必须使用 HTTPS；HTTP 仅保留给 API 同源资源或本机开发服务。
 */
export function resolveTrustedProductImageUri(
  image: string | null | undefined,
  apiBaseUrl: string,
): string | null {
  if (
    typeof image !== "string" ||
    typeof apiBaseUrl !== "string" ||
    CONTROL_CHARACTER_PATTERN.test(image) ||
    CONTROL_CHARACTER_PATTERN.test(apiBaseUrl)
  ) {
    return null;
  }

  const candidate = image.trim();
  const baseCandidate = apiBaseUrl.trim();
  if (
    !candidate ||
    candidate.length > MAX_PRODUCT_IMAGE_URI_LENGTH ||
    !baseCandidate
  ) {
    return null;
  }

  try {
    const base = new URL(baseCandidate);
    if (!isHttpProtocol(base.protocol) || hasCredentials(base)) {
      return null;
    }

    const resolved = new URL(candidate, base);
    if (!isHttpProtocol(resolved.protocol) || hasCredentials(resolved)) {
      return null;
    }

    if (resolved.protocol === "https:") {
      return resolved.href;
    }

    if (
      resolved.origin === base.origin ||
      isLoopbackHostname(resolved.hostname)
    ) {
      return resolved.href;
    }

    return null;
  } catch {
    return null;
  }
}

function isHttpProtocol(protocol: string): boolean {
  return protocol === "http:" || protocol === "https:";
}

function hasCredentials(url: URL): boolean {
  return Boolean(url.username || url.password);
}

function isLoopbackHostname(hostname: string): boolean {
  const normalized = hostname.toLowerCase().replace(/\.$/u, "");
  if (
    normalized === "localhost" ||
    normalized.endsWith(".localhost") ||
    normalized === "::1" ||
    normalized === "[::1]"
  ) {
    return true;
  }

  return /^127(?:\.\d{1,3}){3}$/u.test(normalized);
}
