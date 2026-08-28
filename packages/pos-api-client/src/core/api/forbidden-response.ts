const deviceRevocationCodes = new Set([
  "DEVICE_AUTH_REVOKED",
  "DEVICE_DISABLED",
  "POS_DEVICE_DISABLED",
]);

/** 只有服务端明确标注的设备撤销码才允许持久锁定整台 POS 设备。 */
export function isDeviceRevocationCode(
  code: string | null | undefined,
): boolean {
  return typeof code === "string" && deviceRevocationCodes.has(code);
}
