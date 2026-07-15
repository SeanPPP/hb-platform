const REFRESH_SAFETY_WINDOW_MS = 30_000;
const MINIMUM_REFETCH_DELAY_MS = 1_000;

export function getIdentityPhotoRefetchDelay(
  expiresAt: string | undefined,
  now = Date.now()
): number | false {
  if (!expiresAt) {
    return false;
  }
  const expiresAtMs = Date.parse(expiresAt);
  if (!Number.isFinite(expiresAtMs)) {
    return false;
  }
  return Math.max(MINIMUM_REFETCH_DELAY_MS, expiresAtMs - now - REFRESH_SAFETY_WINDOW_MS);
}
