export function getIdentityPhotoRefreshDelay(
  expiresAt: string | undefined,
  now = Date.now(),
  refreshLeadMs = 15_000
) {
  if (!expiresAt) {
    return null;
  }
  const expiresAtMs = Date.parse(expiresAt);
  if (!Number.isFinite(expiresAtMs)) {
    return null;
  }
  return Math.max(0, expiresAtMs - now - Math.max(0, refreshLeadMs));
}

export function createIdentityPhotoErrorRefetchGuard() {
  const attemptedUrls = new Set<string>();
  return {
    shouldRefetch(url: string) {
      const normalized = url.trim();
      if (!normalized || attemptedUrls.has(normalized)) {
        return false;
      }
      attemptedUrls.add(normalized);
      return true;
    },
  };
}
