/**
 * 员工操作审计必须在动作发生时冻结完整 actor；上传期不能回读当前登录会话。
 * 三个字段沿用既有 audit_events.payload_json，避免为同步 DTO 新增本地列。
 */
export type AuditActorSnapshot = Readonly<{
  cashierId: string;
  cashierName: string | null;
  userGuid: string | null;
}>;

export type AuditActorInput = Readonly<{
  cashierId: string;
  cashierName: string | null;
  userGuid: string | null;
}>;

export type AuditActorPayload = Readonly<{
  requestingCashierId: string;
  requestingCashierName: string | null;
  requestingUserGuid: string | null;
}>;

/** 将可信会话投影为审计载荷；可空值仍显式保留键，供同步端原子识别快照。 */
export function auditActorPayload(input: AuditActorInput): AuditActorPayload {
  const actor = normalizeAuditActor(input);
  return Object.freeze({
    requestingCashierId: actor.cashierId,
    requestingCashierName: actor.cashierName,
    requestingUserGuid: actor.userGuid,
  });
}

/**
 * 只接受同一事件中完整写入的三字段。旧数据缺字段时调用方必须整体回退，
 * 禁止从载荷和订单记录分别取字段混搭成一个员工身份。
 */
export function auditActorSnapshotFromPayload(
  payload: Readonly<Record<string, unknown>>,
): AuditActorSnapshot | null {
  if (
    !Object.hasOwn(payload, "requestingCashierId") ||
    !Object.hasOwn(payload, "requestingCashierName") ||
    !Object.hasOwn(payload, "requestingUserGuid")
  ) {
    return null;
  }
  const cashierId = readRequiredPayloadText(
    payload.requestingCashierId,
  );
  const cashierName = readOptionalPayloadText(
    payload.requestingCashierName,
  );
  const userGuid = readOptionalPayloadText(payload.requestingUserGuid);
  if (cashierId === null || cashierName === undefined || userGuid === undefined) {
    return null;
  }
  return Object.freeze({ cashierId, cashierName, userGuid });
}

function normalizeAuditActor(input: AuditActorInput): AuditActorSnapshot {
  return Object.freeze({
    cashierId: requiredText(input.cashierId, "Audit cashier id"),
    cashierName: optionalInputText(input.cashierName, "Audit cashier name"),
    userGuid: optionalInputText(input.userGuid, "Audit user guid"),
  });
}

function readRequiredPayloadText(value: unknown): string | null {
  return typeof value === "string" ? normalizedText(value) : null;
}

function readOptionalPayloadText(value: unknown): string | null | undefined {
  if (value === null) return null;
  return typeof value === "string" ? normalizedText(value) : undefined;
}

function optionalInputText(
  value: string | null,
  label: string,
): string | null {
  if (value === null) return null;
  return requiredText(value, label);
}

function requiredText(value: string, label: string): string {
  const normalized = normalizedText(value);
  if (normalized === null) {
    throw new TypeError(`${label} is required.`);
  }
  return normalized;
}

function normalizedText(value: string): string | null {
  const normalized = value.trim();
  return normalized && normalized.length <= 256 ? normalized : null;
}
