export const LOCAL_API_ORIGIN = 'http://localhost:5002';

function parseAllowedOrigin(value) {
  try {
    const url = new URL(value);
    const isLocalHttp = url.protocol === 'http:'
      && (url.hostname === 'localhost' || url.hostname === '127.0.0.1');
    if (url.protocol !== 'https:' && !isLocalHttp) return null;
    if (url.username || url.password || url.pathname !== '/' || url.search || url.hash) return null;
    return url.origin;
  } catch {
    return null;
  }
}

export function normalizeApiOrigin(value, defaultOrigin) {
  const normalizedDefault = parseAllowedOrigin(String(defaultOrigin || '').trim());
  if (!normalizedDefault) return null;
  const input = String(value ?? '').trim();
  if (!input || input === '/') return normalizedDefault;
  return parseAllowedOrigin(input);
}

export function resolveApiOrigin(storedOrigin, defaultOrigin) {
  return normalizeApiOrigin(storedOrigin, defaultOrigin)
    || normalizeApiOrigin('/', defaultOrigin);
}

export function toApiHostPattern(origin) {
  const parsed = parseAllowedOrigin(String(origin || '').trim());
  return parsed ? `${parsed}/*` : null;
}
