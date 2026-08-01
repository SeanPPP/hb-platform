/**
 * 员工操作事实发生时所属的可信终端范围。
 *
 * 它是本地审计持久化身份，不是上传 DTO，也不允许业务 feature 自行猜测或回填。
 */
export type AuditScope = Readonly<{
  storeCode: string;
  deviceCode: string;
}>;

/** 在组合根/仓储边界冻结并校验当前可信终端身份。 */
export function freezeAuditScope(scope: AuditScope): AuditScope {
  return Object.freeze({
    storeCode: requiredScopeText(scope.storeCode, "Audit store code"),
    deviceCode: requiredScopeText(scope.deviceCode, "Audit device code"),
  });
}

function requiredScopeText(value: string, label: string): string {
  const normalized = value.trim();
  if (!normalized || normalized.length > 128) {
    throw new TypeError(`${label} is required.`);
  }
  return normalized;
}
