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
    key: "ok",
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

test("终端仅允许 OK 时不公开 Cancel，read 仅返回脱敏动态提示", async () => {
  const api = new RecordingLinklyApi();
  api.current = session({
    okKeyFlag: true,
    displayText: "  INSERT CARD  ",
    displayLines: ["  ENTER PIN  ", ""],
    inputType: "Pin",
    graphicCode: "Card",
    recoveryAction: "Retry",
  });
  const runtime = createRuntime(attempt(), api);

  const result = await runtime.read({ attemptId: "attempt-linkly" });

  assert.deepEqual(result.allowedKeys, ["ok"]);
  assert.deepEqual(result.interaction, {
    displayText: "INSERT CARD",
    displayLines: ["ENTER PIN"],
    inputType: "Pin",
    graphicCode: "Card",
    recoveryAction: "Retry",
  });
  assert.equal(JSON.stringify(result).includes("session-internal"), false);
  assert.equal(JSON.stringify(result).includes("INTERNAL RECEIPT"), false);
});

test("签名提示以最新 display 旗标覆盖旧顶层状态", async () => {
  const api = new RecordingLinklyApi();
  api.current = session({
    okKeyFlag: true,
    notifications: [{
      type: "display",
      payloadJson: '{"Response":{"DisplayText":"SIGNATURE OK?","AcceptYesKeyFlag":true,"DeclineNoKeyFlag":true}}',
      receivedAt: "2026-07-28T00:01:00.000Z",
    }],
  });
  const result = await createRuntime(attempt(), api).read({ attemptId: "attempt-linkly" });
  assert.deepEqual(result.allowedKeys, ["yes", "no"]);
});

test("read 透传 signal/deadline，abort 或过期时不发 status；legacy environment 保留可恢复结果", async () => {
  const api = new RecordingLinklyApi();
  const runtime = createRuntime(attempt(), api);
  const liveController = new AbortController();
  await runtime.read({ attemptId: "attempt-linkly", signal: liveController.signal, deadlineAtMs: Date.now() + 180_000 });
  const liveStatus = api.calls.at(-1);
  assert.equal(liveStatus?.operation, "status");
  assert.strictEqual(liveStatus?.signal, liveController.signal);
  assert.ok((liveStatus?.timeoutMs ?? 0) > 0 && (liveStatus?.timeoutMs ?? 0) <= 180_000);
  const callsAfterLiveRead = api.calls.length;
  const controller = new AbortController();
  controller.abort();

  const aborted = await runtime.read({ attemptId: "attempt-linkly", signal: controller.signal, deadlineAtMs: Date.now() + 180_000 });
  assert.equal(aborted.errorCode, "REQUEST_ABORTED");
  assert.equal(api.calls.length, callsAfterLiveRead);

  const expired = await runtime.read({ attemptId: "attempt-linkly", deadlineAtMs: Date.now() - 1 });
  assert.equal(expired.errorCode, "LINKLY_RECOVERY_DEADLINE_EXCEEDED");
  assert.equal(api.calls.length, callsAfterLiveRead);

  const legacyApi = new RecordingLinklyApi();
  const legacy = createRuntime(attempt({ providerEnvironment: null }), legacyApi);
  const legacyResult = await legacy.read({ attemptId: "attempt-linkly" });
  assert.equal(legacyResult.status, "recovery-required");
  assert.equal(legacyResult.errorCode, "LINKLY_ENVIRONMENT_RECONCILIATION_REQUIRED");
  assert.equal(legacyApi.calls.length, 0);
});

test("ACK pending 或 Declined 不伪装为 completed", async () => {
  const pendingRuntime = createRuntime(attempt({ state: "Pending" }), new RecordingLinklyApi(), {
    acknowledgement: async (value) => ({
      attempt: value,
      acknowledged: false,
      pending: true,
      errorCode: "LINKLY_ACKNOWLEDGEMENT_PENDING",
    }),
  });
  const pending = await pendingRuntime.acknowledge("attempt-linkly");
  assert.equal(pending.status, "recovery-required");
  assert.equal(pending.errorCode, "LINKLY_ACKNOWLEDGEMENT_PENDING");

  const declinedRuntime = createRuntime(attempt({ state: "Declined" }), new RecordingLinklyApi(), {
    acknowledgement: async (value) => ({
      attempt: value,
      acknowledged: true,
      pending: false,
      errorCode: null,
    }),
  });
  const declined = await declinedRuntime.acknowledge("attempt-linkly");
  assert.equal(declined.status, "declined");
  assert.equal(declined.errorCode, null);
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
    acknowledgement?: (attempt: PaymentAttempt) => Promise<{
      attempt: PaymentAttempt;
      acknowledged: boolean;
      pending: boolean;
      errorCode: "LINKLY_ACKNOWLEDGEMENT_PENDING" | null;
    }>;
  } = {},
): LinklyOperatorRuntime {
  return new LinklyOperatorRuntime({
    attempts: {
      async getAttempt(attemptId) {
        return attemptId === value.attemptId ? value : null;
      },
    },
    api,
    acknowledgements: {
      async acknowledge(attemptId) {
        if (attemptId !== value.attemptId) throw new Error("ATTEMPT_MISMATCH");
        return hooks.acknowledgement
          ? hooks.acknowledgement(value)
          : {
              attempt: value,
              acknowledged: true,
              pending: false,
              errorCode: null,
            };
      },
    },
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
      signal?: AbortSignal;
      timeoutMs?: number;
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
    signal?: AbortSignal,
    timeoutMs?: number,
  ): Promise<LinklyCloudBackendSession> {
    this.calls.push({
      operation: "status",
      environment,
      sessionId,
      ...(signal ? { signal } : {}),
      ...(timeoutMs === undefined ? {} : { timeoutMs }),
    });
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
    providerEnvironment: "Sandbox",
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


test("终端 display 旗标兼容 WPF 的数字及 true/1/yes 字符串", async () => {
  for (const flag of [true, 1, -1, "true", "1", " YES "]) {
    const api = new RecordingLinklyApi();
    api.current = session({
      okKeyFlag: true, cancelKeyFlag: true,
      notifications: [{ type: "display", receivedAt: "2026-09-09T00:00:00Z", payloadJson: JSON.stringify({ response: { DisplayText: "SIGNATURE OK?", acceptYesKeyFlag: flag, DeclineNoKeyFlag: flag, CancelKeyFlag: "0", OKKeyFlag: 0 } }) }],
    });
    const result = await createRuntime(attempt(), api).read({ attemptId: "attempt-linkly" });
    assert.deepEqual(result.allowedKeys, ["yes", "no"]);
  }
});

test("最新 display 替代旧签名按键，损坏的新通知不能回退到旧授权", async () => {
  const api = new RecordingLinklyApi();
  const previous = { type: "display", receivedAt: "2026-09-09T00:00:00Z", payloadJson: JSON.stringify({ Response: { DisplayText: "SIGNATURE OK?", AcceptYesKeyFlag: true } }) };
  api.current = session({
    okKeyFlag: true, cancelKeyFlag: true,
    notifications: [previous, { type: "display", receivedAt: "2026-09-09T00:00:01Z", payloadJson: JSON.stringify({ Response: { DisplayText: "AUTHORISE", AuthoriseKeyFlag: true } }) }],
  });
  const runtime = createRuntime(attempt(), api);
  assert.deepEqual((await runtime.read({ attemptId: "attempt-linkly" })).allowedKeys, ["authorise"]);
  api.current = { ...api.current, notifications: [previous, { type: "display", receivedAt: "2026-09-09T00:00:02Z", payloadJson: "invalid" }] };
  assert.deepEqual((await runtime.read({ attemptId: "attempt-linkly" })).allowedKeys, []);
  await runtime.sendKey({ attemptId: "attempt-linkly", key: "yes" });
  assert.equal(api.calls.filter((call) => call.operation === "sendKey").length, 0);
});
