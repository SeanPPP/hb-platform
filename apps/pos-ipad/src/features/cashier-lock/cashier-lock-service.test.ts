import assert from "node:assert/strict";
import test from "node:test";

import { CashierLockService } from "./cashier-lock-service";

import type { AuditEventDraft } from "@/core/contracts";

const identity = {
  storeCode: "S1",
  deviceCode: "IPAD-1",
  cashierId: "C1",
  cashierName: "Alice",
  userGuid: "U1",
} as const;

test("手动锁屏撤销临时授权和活动票据，写 WPF 等价审计后通知重新登录", async () => {
  const trace: string[] = [];
  const audits: AuditEventDraft[] = [];
  let nextId = 0;
  const service = new CashierLockService({
    temporaryAuthorizations: {
      revokeAll() {
        trace.push("revoke-temporary");
      },
    },
    authorization: {
      async clear() {
        trace.push("clear-active-ticket");
      },
    },
    audit: {
      async append(events) {
        trace.push("audit");
        audits.push(...events);
      },
    },
    onLocked() {
      trace.push("notify-login");
    },
    createId: () =>
      `00000000-0000-4000-8000-${String(++nextId).padStart(12, "0")}`,
    nowIso: () => "2026-07-28T06:00:00.000Z",
  });

  await service.lock(identity);

  assert.deepEqual(trace, [
    "revoke-temporary",
    "clear-active-ticket",
    "notify-login",
    "audit",
  ]);
  assert.deepEqual(audits[0], {
    eventId: "00000000-0000-4000-8000-000000000001",
    eventType: "CASHIER_LOGOUT",
    occurredAtIso: "2026-07-28T06:00:00.000Z",
    orderGuid: null,
    correlationId: "00000000-0000-4000-8000-000000000002",
    payload: {
      outcome: "Succeeded",
      reason: "MANUAL_LOCK",
      source: "ipad-pos",
      requestingCashierId: "C1",
      requestingCashierName: "Alice",
      requestingUserGuid: "U1",
      action: "lock-terminal",
      screen: "pos-terminal",
    },
  });
});

test("审计失败不恢复票据，重复点击共享同一次锁屏动作", async () => {
  let clears = 0;
  let notifications = 0;
  let release!: () => void;
  const auditPending = new Promise<void>((resolve) => {
    release = resolve;
  });
  const service = new CashierLockService({
    authorization: {
      async clear() {
        clears += 1;
      },
    },
    audit: {
      async append() {
        await auditPending;
        throw new Error("disk full");
      },
    },
    onLocked() {
      notifications += 1;
    },
    createId: (() => {
      let value = 0;
      return () =>
        `00000000-0000-4000-8000-${String(++value).padStart(12, "0")}`;
    })(),
    nowIso: () => "2026-07-28T06:00:00.000Z",
  });

  const first = service.lock(identity);
  const second = service.lock(identity);
  assert.equal(first, second);
  await Promise.resolve();
  assert.equal(clears, 1);
  assert.equal(notifications, 1);
  release();
  await first;
  assert.equal(clears, 1);
});

test("Keychain 清理失败时不宣称已锁定，也不写成功审计", async () => {
  let notifications = 0;
  let audits = 0;
  const service = new CashierLockService({
    authorization: {
      async clear() {
        throw new Error("keychain unavailable");
      },
    },
    audit: {
      async append() {
        audits += 1;
      },
    },
    onLocked() {
      notifications += 1;
    },
    createId: () => "00000000-0000-4000-8000-000000000001",
    nowIso: () => "2026-07-28T06:00:00.000Z",
  });

  await assert.rejects(() => service.lock(identity), /keychain unavailable/);
  assert.equal(notifications, 0);
  assert.equal(audits, 0);
});
