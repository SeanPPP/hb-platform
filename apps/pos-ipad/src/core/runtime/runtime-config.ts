export const DEFAULT_HBPOS_API_URL = "https://hotbargain.vip/pos-api";

export function resolveHbposApiUrl(configuredUrl: string | undefined): string {
  const source = configuredUrl?.trim() || DEFAULT_HBPOS_API_URL;
  let parsed: URL;
  try {
    parsed = new URL(source);
  } catch {
    throw new Error("HBPOS API address must be an absolute HTTP URL.");
  }

  if (parsed.protocol !== "https:" && parsed.protocol !== "http:") {
    throw new Error("HBPOS API address must use HTTP or HTTPS.");
  }
  if (parsed.protocol === "http:" && !isLoopbackHostname(parsed.hostname)) {
    throw new Error("Remote HBPOS API address requires HTTPS.");
  }
  if (parsed.username || parsed.password) {
    throw new Error("HBPOS API address must not contain credentials.");
  }
  if (parsed.search || parsed.hash) {
    throw new Error("HBPOS API address must not contain query or fragment data.");
  }

  const path = parsed.pathname.replace(/\/+$/, "");
  return `${parsed.origin}${path}`;
}

function isLoopbackHostname(hostname: string): boolean {
  const normalized = hostname.toLowerCase().replace(/^\[|\]$/gu, "");
  return (
    normalized === "localhost" ||
    normalized === "127.0.0.1" ||
    normalized === "::1"
  );
}
