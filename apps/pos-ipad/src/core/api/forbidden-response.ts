const deviceRevocationCodes = new Set([
  "DEVICE_AUTH_REVOKED",
  "DEVICE_DISABLED",
  "POS_DEVICE_DISABLED",
]);

/**
 * Hbpos.Api 会用 403 表示收银员权限、门店 scope 和新交易门禁。
 * 只有服务端明确标注的设备撤销码才允许持久锁定整台 iPad。
 */
export function isDeviceRevocationCode(
  code: string | null | undefined,
): boolean {
  return typeof code === "string" && deviceRevocationCodes.has(code);
}
