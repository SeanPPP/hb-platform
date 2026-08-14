import assert from "node:assert/strict";
import test from "node:test";

import { LinklyOperatorRuntime } from "./linkly-operator-runtime";
import { PAYMENT_PERMISSION } from "./payment-checkout-runtime";

import type { PaymentAttempt } from "@/core/contracts";
import type { LinklyCloudBackendSession } from "@/features/payments/linkly/linkly-cloud-backend";

test("Unknown attempt 禁止 sendkey，公开命令/结果均无 sessionId", async () => {
  const api = new RecordingLinklyApi();
  const runtime = createRuntime(
    attempt({ state: "Unknown" }),
    api,
  );

  const result = await runtime.sendKey({
    attemptId: "attempt-linkly",
    key: "ok-cancel",
  });

  assert.equal(result.status, "recovery-required");
  assert.equal(result.errorCode, "LINKLY_UNKNOWN_REQUIRES_RECOVERY");
  assert.equal(api.calls.length, 0);
  assert.equal(JSON.stringify(result).includes("session-internal"), false);
});

test("安全 key 先按既有 session flags 校验，再只发送官方数字键；不具备 create 能力", async () => {
  const api = new RecordingLinklyApi();
  api.current = session({ acceptYesKeyFlag: true });
  const permissions: string[] = [];
  const runtime = createRuntime(attempt(), api, {
    permission(code) {
      permissions.push(code);
    },
  });

  const result = await runtime.sendKey({
    attemptId: "attempt-linkly",
    key: "yes",
  });

  assert.deepEqual(api.calls, [
    {
      operation: "status",
      environment: "Sandbox",
      sessionId: "session-internal",
    },
    {
      operation: "sendKey",
      environment: "Sandbox",
      sessionId: "session-internal",
      key: "1",
      data: null,
    },
  ]);
  assert.equal(result.attemptId, "attempt-linkly");
  assert.equal(JSON.stringify(result).includes("session-internal"), false);
  assert.ok(permissions.includes(PAYMENT_PERMISSION.view));
  assert.ok(permissions.includes(PAYMENT_PERMISSION.takeCard));
  assert.ok(permissions.includes(PAYMENT_PERMISSION.confirm));
  assert.equal("create" in api, false);
});

test("当前 session 未声明的 operator key fail closed，不发送 sendkey", async () => {
  const api = new RecordingLinklyApi();
  api.current = session({ okKeyFlag: true });
  const runtime = createRuntime(attempt(), api);

  const result = await runtime.sendKey({
    attemptId: "attempt-linkly",
    key: "authorise",
  });

  assert.equal(result.errorCode, "LINKLY_OPERATOR_KEY_NOT_ALLOWED");
  assert.equal(api.calls.filter((call) => call.operation === "sendKey").length, 0);
});

test("receiptPrinted/ack 只从 attempt 内取 session；异步后旧会话失效拒绝伪成功", async () => {
  const api = new RecordingLinklyApi();
  let active = true;
  api.afterReceipt = () => {
    active = false;
  };
  const runtime = createRuntime(attempt({ state: "Approved" }), api, {
    session() {
      if (!active) throw new Error("CURRENT_CASHIER_REQUIRED");
    },
  });

  await assert.rejects(
    () => runtime.markReceiptPrinted("attempt-linkly"),
    /CURRENT_CASHIER_REQUIRED/,
  );
  assert.deepEqual(api.calls.at(-1), {
    operation: "receipt",
    environment: "Sandbox",
    sessionId: "session-internal",
  });
});

function createRuntime(
  value: PaymentAttempt,
  api: RecordingLinklyApi,
  hooks: {
    permission?: (code: string) => void;
    session?: () => void;
  } = {},
): LinklyOperatorRuntime {
  return new LinklyOperatorRuntime({
    attempts: {
      async getAttempt(attemptId) {
        return attemptId === value.attemptId ? value : null;
      },
    },
    api,
    configuration: { environment: "Sandbox" },
    trustedSession: {
      assertActive() {
        hooks.session?.();
      },
    },
    permissions: {
      assert(code) {
        hooks.permission?.(code);
      },
    },
  });
}

type LinklyCall =
  | Readonly<{
      operation: "status" | "receipt" | "ack";
      environment: string;
      sessionId: string;
    }>
  | Readonly<{
      operation: "sendKey";
      environment: string;
      sessionId: string;
      key: string;
      data: string | null;
    }>;

class RecordingLinklyApi {
  public calls: LinklyCall[] = [];
  public current = session();
  public afterReceipt: (() => void) | null = null;

  public async status(
    environment: string,
    sessionId: string,
  ): Promise<LinklyCloudBackendSession> {
    this.calls.push({ operation: "status", environment, sessionId });
    return this.current;
  }

  public async sendKey(
    environment: string,
    sessionId: string,
    key: string,
    data: string | null,
  ): Promise<LinklyCloudBackendSession> {
    this.calls.push({
      operation: "sendKey",
      environment,
      sessionId,
      key,
      data,
    });
    return this.current;
  }

  public async markReceiptPrinted(
    environment: string,
    sessionId: string,
  ): Promise<LinklyCloudBackendSession> {
    this.calls.push({ operation: "receipt", environment, sessionId });
    this.afterReceipt?.();
    return this.current;
  }

  public async acknowledge(
    environment: string,
    sessionId: string,
  ): Promise<LinklyCloudBackendSession> {
    this.calls.push({ operation: "ack", environment, sessionId });
    return this.current;
  }
}

function session(
  overrides: Partial<LinklyCloudBackendSession> = {},
): LinklyCloudBackendSession {
  return {
    environment: "Sandbox",
    storeCode: "S1",
    deviceCode: "IPAD1",
    sessionId: "session-internal",
    status: "InProgress",
    txnRef: null,
    responseCode: null,
    responseText: null,
    recoveryAction: null,
    displayText: null,
    cancelKeyFlag: false,
    okKeyFlag: false,
    acceptYesKeyFlag: false,
    declineNoKeyFlag: false,
    authoriseKeyFlag: false,
    inputType: null,
    graphicCode: null,
    displayLines: [],
    receiptText: "INTERNAL RECEIPT",
    recoveryCount: 0,
    receiptPrintedAt: null,
    clientAcknowledgedAt: null,
    lastHttpStatus: 200,
    notifications: [],
    transactionSuccess: null,
    ...overrides,
  };
}

function attempt(overrides: Partial<PaymentAttempt> = {}): PaymentAttempt {
  return {
    attemptId: "attempt-linkly",
    idempotencyKey: "idempotency-linkly",
    orderGuid: "order-linkly",
    provider: "linkly-cloud",
    operation: "purchase",
    amount: { currency: "AUD", cents: 1_000 },
    state: "Pending",
    references: {
      checkoutId: null,
      paymentId: null,
      sessionId: "session-internal",
      txnRef: "txn-internal",
      rfn: "rfn-internal",
      voucherReservationToken: null,
    },
    createdAtIso: "2026-07-28T00:00:00.000Z",
    updatedAtIso: "2026-07-28T00:01:00.000Z",
    lastErrorCode: null,
    ...overrides,
  };
}
