import {
  auditActorPayload,
  type AuditEventDraft,
} from "@/core/contracts";
import type { AuditRepositoryPort } from "@/core/contracts/repositories";

export type CashierLockIdentity = Readonly<{
  storeCode: string;
  deviceCode: string;
  cashierId: string;
  cashierName: string;
  userGuid: string | null;
}>;

export type CashierLockServiceOptions = Readonly<{
  authorization: Readonly<{ clear(): Promise<void> }>;
  audit: Pick<AuditRepositoryPort, "append">;
  temporaryAuthorizations?: Readonly<{ revokeAll(): void }> | undefined;
  onLocked(): void;
  createId(): string;
  nowIso(): string;
}>;

/**
 * 手动锁屏只撤销当前活动收银员，不锁设备、不清离线缓存，也不触碰购物车。
 * Keychain 清理成功后才通知 UI；审计失败不能把已经完成的锁屏回滚。
 */
export class CashierLockService {
  private inFlight: Promise<void> | null = null;

  public constructor(private readonly options: CashierLockServiceOptions) {}

  public lock(identity: CashierLockIdentity): Promise<void> {
    if (this.inFlight) return this.inFlight;
    const normalized = validateIdentity(identity);
    const operation = this.lockOnce(normalized).finally(() => {
      if (this.inFlight === operation) {
        this.inFlight = null;
      }
    });
    this.inFlight = operation;
    return operation;
  }

  private async lockOnce(identity: CashierLockIdentity): Promise<void> {
    this.options.temporaryAuthorizations?.revokeAll();
    await this.options.authorization.clear();
    this.options.onLocked();

    try {
      const event = createLogoutAudit(
        identity,
        this.options.createId,
        this.options.nowIso,
      );
      await this.options.audit.append([event]);
    } catch {
      // 锁屏已经完成；审计存储故障不能恢复活动票据或继续暴露收银界面。
    }
  }
}

function createLogoutAudit(
  identity: CashierLockIdentity,
  createId: () => string,
  nowIso: () => string,
): AuditEventDraft {
  const occurredAtIso = nowIso();
  if (!Number.isFinite(Date.parse(occurredAtIso))) {
    throw new TypeError("Cashier lock timestamp must be a valid ISO value.");
  }
  return {
    eventId: requiredText(createId(), "Audit event id"),
    eventType: "CASHIER_LOGOUT",
    occurredAtIso,
    orderGuid: null,
    correlationId: requiredText(createId(), "Audit correlation id"),
    payload: {
      outcome: "Succeeded",
      reason: "MANUAL_LOCK",
      source: "ipad-pos",
      ...auditActorPayload(identity),
      action: "lock-terminal",
      screen: "pos-terminal",
    },
  };
}

function validateIdentity(identity: CashierLockIdentity): CashierLockIdentity {
  return {
    storeCode: requiredText(identity.storeCode, "Store code"),
    deviceCode: requiredText(identity.deviceCode, "Device code"),
    cashierId: requiredText(identity.cashierId, "Cashier id"),
    cashierName: requiredText(identity.cashierName, "Cashier name"),
    userGuid: identity.userGuid,
  };
}

function requiredText(value: string, label: string): string {
  const normalized = value.trim();
  if (!normalized) throw new TypeError(`${label} is required.`);
  return normalized;
}
