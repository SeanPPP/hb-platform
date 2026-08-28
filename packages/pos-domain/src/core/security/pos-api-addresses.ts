export const DEFAULT_LOCAL_HBPOS_API_BASE_URL =
  "http://192.168.31.246:5003";
export const LEGACY_LOCAL_HBPOS_API_BASE_URL =
  "http://192.168.31.246:5159";
export const DEFAULT_REMOTE_HBPOS_API_BASE_URL =
  "https://hotbargain.vip/pos-api";

export function isTrustedLocalHbposApiOrigin(origin: string): boolean {
  return (
    origin === DEFAULT_LOCAL_HBPOS_API_BASE_URL ||
    origin === LEGACY_LOCAL_HBPOS_API_BASE_URL
  );
}
