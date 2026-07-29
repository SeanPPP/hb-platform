import assert from "node:assert/strict";
import test from "node:test";

import {
  FulfilmentService,
  type FulfilmentAuthorizationContext,
  type FulfilmentAuditEvent,
  type FulfilmentInitialAuthorization,
  type FulfilmentStore,
  type PreparedManualDrawerOpen,
  type PreparedLastReceiptReprint,
} from "./fulfilment-service";

type PreparedReprintHasRequiredOrderGuid =
  PreparedLastReceiptReprint extends Readonly<{ orderGuid: string }> ? true : false;
const preparedReprintHasRequiredOrderGuid: PreparedReprintHasRequiredOrderGuid = true;

class MemoryStore implements FulfilmentStore {
  public readonly printJobs = new Map<string, { jobId: string; orderGuid: string; printerId: string; isReprint: boolean; bytes: Uint8Array; state: "Queued" | "Sending" | "Printed" | "Failed" | "Ambiguous"; retryCount: number; authorization?: FulfilmentAuthorizationContext }>();
  public readonly drawerEvents = new Map<string, { eventId: string; orderGuid: string | null; printerId: string; state: "Required" | "Requested" | "Completed" | "Failed" | "Unknown"; reason: string; retryCount: number }>();
  public readonly reprintInputs: PreparedLastReceiptReprint[] = [];
  public readonly audits: FulfilmentAuditEvent[] = [];
  public readonly printFinishCalls: {
    jobId: string;
    state: "Printed" | "Failed" | "Ambiguous";
    errorCode: string | null;
    audit: FulfilmentAuditEvent | null;
  }[] = [];
  public readonly drawerFinishCalls: {
    eventId: string;
    state: "Completed" | "Failed" | "Unknown";
    errorCode: string | null;
    audit: FulfilmentAuditEvent;
  }[] = [];
  public printFinishSucceeds = true;
  public drawerFinishSucceeds = true;
  public printFinishThrows = false;
  public drawerFinishThrows = false;

  public async listQueuedPrintJobs() { return [...this.printJobs.values()].filter((job) => job.state === "Queued"); }
  public async listRequiredDrawerEvents() { return [...this.drawerEvents.values()].filter((event) => event.state === "Required"); }
  public async claimQueuedPrintJob(jobId: string) { return this.claimPrint(jobId, "Queued", false); }
  public async beginManualPrintRetry(jobId: string) { return this.claimPrint(jobId, "Failed", true); }
  public async finishPrintJob(
    jobId: string,
    expected: "Sending",
    state: "Printed" | "Failed" | "Ambiguous",
    errorCode: string | null,
    audit: FulfilmentAuditEvent | null,
  ) {
    this.printFinishCalls.push({ jobId, state, errorCode, audit });
    if (this.printFinishThrows) throw new Error("atomic print audit persistence failed");
    const job = this.printJobs.get(jobId);
    if (!this.printFinishSucceeds || !job || job.state !== expected) return false;
    job.state = state;
    if (audit) this.audits.push(audit);
    return true;
  }
  public async claimRequiredDrawerEvent(eventId: string) { return this.claimDrawer(eventId, "Required", false); }
  public async beginManualDrawerRetry(eventId: string) { return this.claimDrawer(eventId, "Failed", true); }
  public async beginManualDrawerOpen(input: Readonly<{
    eventId: string;
    printerId: string;
    reason: "MANUAL";
  }>, authorization: FulfilmentInitialAuthorization) {
    const existing = this.drawerEvents.get(input.eventId);
    if (existing) {
      if (
        existing.orderGuid !== null ||
        existing.printerId !== input.printerId ||
        existing.reason !== "MANUAL" ||
        existing.state === "Required"
      ) {
        throw new Error("Manual drawer action conflict.");
      }
      return { kind: "existing" as const, event: existing };
    }
    const event = {
      eventId: input.eventId,
      orderGuid: null,
      printerId: input.printerId,
      state: "Requested" as const,
      reason: "MANUAL",
      retryCount: 0,
    };
    this.drawerEvents.set(input.eventId, event);
    this.audits.push(authorization.audit);
    return { kind: "created" as const, event };
  }
  public async finishDrawerEvent(
    eventId: string,
    expected: "Requested",
    state: "Completed" | "Failed" | "Unknown",
    errorCode: string | null,
    audit: FulfilmentAuditEvent,
  ) {
    this.drawerFinishCalls.push({ eventId, state, errorCode, audit });
    if (this.drawerFinishThrows) throw new Error("atomic drawer audit persistence failed");
    const event = this.drawerEvents.get(eventId);
    if (!this.drawerFinishSucceeds || !event || event.state !== expected) return false;
    event.state = state;
    this.audits.push(audit);
    return true;
  }
  public async createLastReceiptReprint(
    input: PreparedLastReceiptReprint,
    authorization?: FulfilmentInitialAuthorization,
  ) {
    this.reprintInputs.push(input);
    const copy = {
      jobId: authorization?.context.actionId ?? `reprint-${input.orderGuid}`,
      orderGuid: input.orderGuid,
      printerId: input.printerId,
      bytes: input.receiptBytes,
      isReprint: true,
      state: "Queued" as const,
      retryCount: 0,
      ...(authorization
        ? { authorization: authorization.context }
        : {}),
    };
    this.printJobs.set(copy.jobId, copy);
    if (authorization) this.audits.push(authorization.audit);
    return copy;
  }

  private async claimPrint(jobId: string, expected: "Queued" | "Failed", manual: boolean) {
    const job = this.printJobs.get(jobId);
    if (!job || job.state !== expected) return null;
    job.state = "Sending";
    if (manual) job.retryCount += 1;
    return job;
  }
  private async claimDrawer(eventId: string, expected: "Required" | "Failed", manual: boolean) {
    const event = this.drawerEvents.get(eventId);
    if (!event || event.state !== expected) return null;
    event.state = "Requested";
    if (manual) event.retryCount += 1;
    return event;
  }
}

class FakePrinter {
  public calls: string[] = [];
  public connectCalls: string[] = [];
  public byteCalls: Uint8Array[] = [];
  public active = 0;
  public maxActive = 0;
  public hold: Promise<void> | null = null;
  public connectError: Error | null = null;
  public printError: Error | null = null;
  public result: { status: "printed" | "failed" | "ambiguous"; errorCode: string | null } = { status: "printed", errorCode: null };
  public constructor(private readonly trace: string[]) {}
  public async connect(printerId: string) { this.connectCalls.push(printerId); this.trace.push(`connect:${printerId}`); if (this.connectError) throw this.connectError; }
  public async print(jobId: string, bytes: Uint8Array) { this.calls.push(jobId); this.trace.push(`print:${jobId}`); this.byteCalls.push(bytes); this.active += 1; this.maxActive = Math.max(this.maxActive, this.active); try { await this.hold; if (this.printError) throw this.printError; return this.result; } finally { this.active -= 1; } }
}
class FakeDrawer {
  public calls: string[] = [];
  public openError: Error | null = null;
  public result: { status: "completed" | "failed" | "unknown"; errorCode: string | null } = { status: "completed", errorCode: null };
  public constructor(private readonly trace: string[]) {}
  public async open(eventId: string) { this.calls.push(eventId); this.trace.push(`drawer:${eventId}`); if (this.openError) throw this.openError; return this.result; }
}

function setup(overrides: Readonly<{
  prepareLastReceiptReprint?: () => Promise<PreparedLastReceiptReprint | null>;
  prepareReceiptReprint?: (
    orderGuid: string,
  ) => Promise<PreparedLastReceiptReprint | null>;
  prepareManualDrawerOpen?: () => Promise<PreparedManualDrawerOpen | null>;
}> = {}) {
  const store = new MemoryStore();
  const hardwareTrace: string[] = [];
  const printer = new FakePrinter(hardwareTrace);
  const drawer = new FakeDrawer(hardwareTrace);
  const service = new FulfilmentService({
    store,
    printer,
    drawer,
    nowIso: () => "2026-07-28T02:00:00.000Z",
    createAuditId: (() => { let value = 0; return () => `audit-${++value}`; })(),
    createCorrelationId: (() => { let value = 0; return () => `correlation-${++value}`; })(),
    prepareLastReceiptReprint: overrides.prepareLastReceiptReprint ?? (async () => ({
      orderGuid: "ORDER-DEFAULT",
      receiptBytes: Uint8Array.of(29, 33, 82),
      printerId: "XP-REPRINT",
    })),
    prepareManualDrawerOpen:
      overrides.prepareManualDrawerOpen ?? (async () => ({
        printerId: "XP-MANUAL-DRAWER",
      })),
    ...(overrides.prepareReceiptReprint
      ? { prepareReceiptReprint: overrides.prepareReceiptReprint }
      : {}),
  });
  return { store, printer, drawer, audit: store.audits, hardwareTrace, service };
}

const reprintAuthorization: FulfilmentAuthorizationContext = {
  actionId: "action-reprint-last",
  permissionCode: "Permissions.PosTerminal.Receipt.PrintLast",
  authorizationMode: "online",
  requestingCashierId: "cashier-1",
  authorizingCashierId: "supervisor-1",
};

const drawerAuthorization: FulfilmentAuthorizationContext = {
  actionId: "action-open-drawer",
  permissionCode: "Permissions.PosTerminal.CashDrawer.Open",
  authorizationMode: "offline-cache",
  requestingCashierId: "cashier-1",
  authorizingCashierId: "supervisor-1",
};

const activeLease = () => undefined;

test("自动队列只认 Queued/Required；终态、Sending、Ambiguous、Requested、Unknown 一律不重放", async () => {
  assert.equal(preparedReprintHasRequiredOrderGuid, true);
  const { store, printer, drawer, service } = setup();
  for (const state of ["Queued", "Sending", "Printed", "Failed", "Ambiguous"] as const) {
    store.printJobs.set(`print-${state}`, { jobId: `print-${state}`, orderGuid: "O1", printerId: "XP-PRINT", isReprint: false, bytes: Uint8Array.of(1), state, retryCount: 0 });
  }
  for (const state of ["Required", "Requested", "Completed", "Failed", "Unknown"] as const) {
    store.drawerEvents.set(`drawer-${state}`, { eventId: `drawer-${state}`, orderGuid: "O1", printerId: "XP-DRAWER", state, reason: "CASH", retryCount: 0 });
  }

  const result = await service.drainAutomaticQueue();

  assert.deepEqual(printer.calls, ["print-Queued"]);
  assert.deepEqual(printer.connectCalls, ["XP-PRINT", "XP-DRAWER"]);
  assert.deepEqual(drawer.calls, ["drawer-Required"]);
  assert.deepEqual(result, { printed: 1, drawersOpened: 1 });
  assert.equal(store.printJobs.get("print-Queued")?.state, "Printed");
  assert.equal(store.drawerEvents.get("drawer-Required")?.state, "Completed");
  assert.equal(store.printFinishCalls[0]?.audit, null);
  assert.equal(store.drawerFinishCalls[0]?.audit.eventType, "CASH_DRAWER_OPEN");
});

test("打印和钱箱失败只记录状态与审计，绝不回滚已完成订单或自动重试", async () => {
  const { store, printer, drawer, audit, service } = setup();
  store.printJobs.set("print-1", { jobId: "print-1", orderGuid: "completed-order", printerId: "XP-OLD", isReprint: false, bytes: Uint8Array.of(1), state: "Queued", retryCount: 0 });
  store.drawerEvents.set("drawer-1", { eventId: "drawer-1", orderGuid: "completed-order", printerId: "XP-OLD", state: "Required", reason: "CASH", retryCount: 0 });
  printer.result = { status: "failed", errorCode: "BLE_LOST" };
  drawer.result = { status: "failed", errorCode: "DRAWER_OFFLINE" };

  await service.drainAutomaticQueue();
  await service.drainAutomaticQueue();

  assert.equal(store.printJobs.get("print-1")?.state, "Failed");
  assert.equal(store.drawerEvents.get("drawer-1")?.state, "Failed");
  assert.deepEqual(printer.calls, ["print-1"]);
  assert.deepEqual(drawer.calls, ["drawer-1"]);
  assert.deepEqual(
    audit.map((event) => ({ eventType: event.eventType, outcome: event.payload.outcome })),
    [{ eventType: "CASH_DRAWER_OPEN", outcome: "Failed" }],
  );
});

test("Failed 只能人工重试，递增 retryCount 与审计；不接受 Printed、Ambiguous 或 Unknown", async () => {
  const { store, printer, drawer, audit, service } = setup();
  store.printJobs.set("print-failed", { jobId: "print-failed", orderGuid: "O1", printerId: "XP-MANUAL-PRINT", isReprint: false, bytes: Uint8Array.of(1), state: "Failed", retryCount: 0 });
  store.printJobs.set("print-ambiguous", { jobId: "print-ambiguous", orderGuid: "O1", printerId: "XP-IGNORED", isReprint: false, bytes: Uint8Array.of(1), state: "Ambiguous", retryCount: 0 });
  store.printJobs.set("print-printed", { jobId: "print-printed", orderGuid: "O1", printerId: "XP-IGNORED", isReprint: false, bytes: Uint8Array.of(1), state: "Printed", retryCount: 0 });
  store.drawerEvents.set("drawer-failed", { eventId: "drawer-failed", orderGuid: "O1", printerId: "XP-MANUAL-DRAWER", state: "Failed", reason: "CASH", retryCount: 0 });
  store.drawerEvents.set("drawer-unknown", { eventId: "drawer-unknown", orderGuid: "O1", printerId: "XP-IGNORED", state: "Unknown", reason: "CASH", retryCount: 0 });

  assert.equal((await service.retryFailedPrint("print-failed")).state, "Printed");
  assert.equal((await service.retryFailedDrawer("drawer-failed")).state, "Completed");
  assert.equal((await service.retryFailedPrint("print-ambiguous")).state, "not-retryable");
  assert.equal((await service.retryFailedPrint("print-printed")).state, "not-retryable");
  assert.equal((await service.retryFailedDrawer("drawer-unknown")).state, "not-retryable");
  assert.equal(store.printJobs.get("print-failed")?.retryCount, 1);
  assert.equal(store.drawerEvents.get("drawer-failed")?.retryCount, 1);
  assert.deepEqual(
    audit.map((event) => ({ eventType: event.eventType, outcome: event.payload.outcome })),
    [
      { eventType: "RECEIPT_REPRINT", outcome: "Succeeded" },
      { eventType: "CASH_DRAWER_OPEN", outcome: "Succeeded" },
    ],
  );
  assert.deepEqual(printer.calls, ["print-failed"]);
  assert.deepEqual(printer.connectCalls, ["XP-MANUAL-PRINT", "XP-MANUAL-DRAWER"]);
  assert.deepEqual(drawer.calls, ["drawer-failed"]);
  assert.deepEqual(store.reprintInputs, []);
});

test("原生结果不确定或抛错进入 Ambiguous/Unknown，必须由主管处置而非自动重放", async () => {
  const { store, printer, drawer, service } = setup();
  store.printJobs.set("print-1", { jobId: "print-1", orderGuid: "O1", printerId: "XP-1", isReprint: false, bytes: Uint8Array.of(1), state: "Queued", retryCount: 0 });
  store.drawerEvents.set("drawer-1", { eventId: "drawer-1", orderGuid: "O1", printerId: "XP-1", state: "Required", reason: "CASH", retryCount: 0 });
  printer.result = { status: "ambiguous", errorCode: "WRITE_TIMEOUT" };
  drawer.result = { status: "unknown", errorCode: "PULSE_TIMEOUT" };

  await service.drainAutomaticQueue();
  await service.drainAutomaticQueue();

  assert.equal(store.printJobs.get("print-1")?.state, "Ambiguous");
  assert.equal(store.drawerEvents.get("drawer-1")?.state, "Unknown");
  assert.deepEqual(printer.calls, ["print-1"]);
  assert.deepEqual(drawer.calls, ["drawer-1"]);
});

test("最后小票重打必须使用订单账本选定的 orderGuid，不得回退绑定旧打印订单", async () => {
  const prepared = {
    orderGuid: "ORDER-CURRENT",
    receiptBytes: Uint8Array.of(29, 33, 82),
    printerId: "XP-REPRINT",
  };
  const { store, printer, audit, service } = setup({
    prepareLastReceiptReprint: async () => prepared,
  });
  store.printJobs.set("old-source", { jobId: "old-source", orderGuid: "ORDER-OLD", printerId: "XP-SOURCE", isReprint: false, bytes: Uint8Array.of(1), state: "Printed", retryCount: 0 });

  const result = await service.reprintLastReceipt(
    reprintAuthorization,
    activeLease,
  );

  assert.equal(result.state, "Printed");
  assert.equal(store.printJobs.get("old-source")?.state, "Printed");
  assert.equal(store.printJobs.get(reprintAuthorization.actionId)?.state, "Printed");
  assert.equal(store.printJobs.get(reprintAuthorization.actionId)?.orderGuid, "ORDER-CURRENT");
  assert.equal(store.printJobs.get(reprintAuthorization.actionId)?.isReprint, true);
  assert.deepEqual(store.reprintInputs, [prepared]);
  assert.deepEqual(printer.calls, [reprintAuthorization.actionId]);
  assert.deepEqual(printer.connectCalls, ["XP-REPRINT"]);
  assert.deepEqual(printer.byteCalls, [prepared.receiptBytes]);
  assert.deepEqual(
    audit.map((event) => ({ eventType: event.eventType, outcome: event.payload.outcome })),
    [
      { eventType: "RECEIPT_REPRINT", outcome: "Succeeded" },
      { eventType: "RECEIPT_REPRINT", outcome: "Succeeded" },
    ],
  );
});

test("最后小票重打把授权审计与首个任务原子创建，并让终态沿用 actionId 关联", async () => {
  const { store, audit, service } = setup();

  assert.equal(
    (
      await service.reprintLastReceipt(
        reprintAuthorization,
        activeLease,
      )
    ).state,
    "Printed",
  );
  assert.equal(store.printJobs.get(reprintAuthorization.actionId)?.state, "Printed");
  assert.deepEqual(
    audit.map((event) => ({
      status: event.payload.status,
      outcome: event.payload.outcome,
      correlationId: event.correlationId,
      requestingCashierId: event.payload.requestingCashierId,
      authorizingCashierId: event.payload.authorizingCashierId,
      permissionCode: event.payload.permissionCode,
      authorizationMode: event.payload.authorizationMode,
    })),
    [
      {
        status: "Authorized",
        outcome: "Succeeded",
        correlationId: reprintAuthorization.actionId,
        requestingCashierId: "cashier-1",
        authorizingCashierId: "supervisor-1",
        permissionCode: "Permissions.PosTerminal.Receipt.PrintLast",
        authorizationMode: "online",
      },
      {
        status: "Printed",
        outcome: "Succeeded",
        correlationId: reprintAuthorization.actionId,
        requestingCashierId: "cashier-1",
        authorizingCashierId: "supervisor-1",
        permissionCode: "Permissions.PosTerminal.Receipt.PrintLast",
        authorizationMode: "online",
      },
    ],
  );
});

test("手动开箱把授权审计与 Requested 事件原子创建，并与终态审计共用 actionId", async () => {
  const { store, printer, drawer, audit, service } = setup({
    prepareManualDrawerOpen: async () => ({ printerId: "XP-FROZEN-MANUAL" }),
  });

  const result = await service.openDrawerManually(
    drawerAuthorization,
    activeLease,
  );

  assert.deepEqual(result, { state: "Completed", errorCode: null });
  assert.deepEqual(printer.connectCalls, ["XP-FROZEN-MANUAL"]);
  assert.deepEqual(drawer.calls, ["action-open-drawer"]);
  assert.deepEqual(store.drawerEvents.get("action-open-drawer"), {
    eventId: "action-open-drawer",
    orderGuid: null,
    printerId: "XP-FROZEN-MANUAL",
    state: "Completed",
    reason: "MANUAL",
    retryCount: 0,
  });
  assert.deepEqual(
    audit.map((event) => ({
      eventType: event.eventType,
      orderGuid: event.orderGuid,
      status: event.payload.status,
      outcome: event.payload.outcome,
      correlationId: event.correlationId,
      action: event.payload.action,
      source: event.payload.source,
      reason: event.payload.reason,
      requestingCashierId: event.payload.requestingCashierId,
      authorizingCashierId: event.payload.authorizingCashierId,
      permissionCode: event.payload.permissionCode,
      authorizationMode: event.payload.authorizationMode,
    })),
    [
      {
        eventType: "CASH_DRAWER_OPEN",
        orderGuid: null,
        status: "Authorized",
        outcome: "Succeeded",
        correlationId: drawerAuthorization.actionId,
        action: "open-cash-drawer",
        source: "sales",
        reason: "MANUAL",
        requestingCashierId: "cashier-1",
        authorizingCashierId: "supervisor-1",
        permissionCode: "Permissions.PosTerminal.CashDrawer.Open",
        authorizationMode: "offline-cache",
      },
      {
        eventType: "CASH_DRAWER_OPEN",
        orderGuid: null,
        status: "Completed",
        outcome: "Succeeded",
        correlationId: drawerAuthorization.actionId,
        action: "open-cash-drawer",
        source: "sales",
        reason: "MANUAL",
        requestingCashierId: "cashier-1",
        authorizingCashierId: "supervisor-1",
        permissionCode: "Permissions.PosTerminal.CashDrawer.Open",
        authorizationMode: "offline-cache",
      },
    ],
  );
});

test("手动开箱同 actionId 幂等；Completed、Unknown 和未收口 Requested 都不重放", async () => {
  const completed = setup();
  assert.equal(
    (
      await completed.service.openDrawerManually(
        drawerAuthorization,
        activeLease,
      )
    ).state,
    "Completed",
  );
  assert.equal(
    (
      await completed.service.openDrawerManually(
        drawerAuthorization,
        activeLease,
      )
    ).state,
    "Completed",
  );
  assert.deepEqual(completed.drawer.calls, ["action-open-drawer"]);

  const unknown = setup();
  unknown.drawer.result = { status: "unknown", errorCode: "PULSE_TIMEOUT" };
  assert.equal(
    (
      await unknown.service.openDrawerManually(
        drawerAuthorization,
        activeLease,
      )
    ).state,
    "Unknown",
  );
  assert.equal(
    (
      await unknown.service.openDrawerManually(
        drawerAuthorization,
        activeLease,
      )
    ).state,
    "Unknown",
  );
  assert.deepEqual(unknown.drawer.calls, ["action-open-drawer"]);

  const requested = setup();
  requested.store.drawerFinishSucceeds = false;
  assert.equal(
    (
      await requested.service.openDrawerManually(
        drawerAuthorization,
        activeLease,
      )
    ).state,
    "recovery-required",
  );
  assert.equal(
    (
      await requested.service.openDrawerManually(
        drawerAuthorization,
        activeLease,
      )
    ).state,
    "recovery-required",
  );
  assert.deepEqual(requested.drawer.calls, ["action-open-drawer"]);
});

test("手动开箱同 actionId 改绑打印机必须 fail-closed，不能向第二台外设发脉冲", async () => {
  let printerId = "XP-FIRST";
  const { printer, drawer, service } = setup({
    prepareManualDrawerOpen: async () => ({ printerId }),
  });
  assert.equal(
    (
      await service.openDrawerManually(
        drawerAuthorization,
        activeLease,
      )
    ).state,
    "Completed",
  );

  printerId = "XP-SECOND";
  await assert.rejects(
    service.openDrawerManually(drawerAuthorization, activeLease),
    /manual drawer action conflict/i,
  );
  assert.deepEqual(printer.connectCalls, ["XP-FIRST"]);
  assert.deepEqual(drawer.calls, ["action-open-drawer"]);
});

test("手动履约权限上下文不匹配时 fail-closed，不能创建任务或触发硬件", async () => {
  const { store, printer, drawer, service } = setup();

  await assert.rejects(
    service.openDrawerManually(
      {
        ...drawerAuthorization,
        permissionCode: "Permissions.PosTerminal.Receipt.PrintLast",
      },
      activeLease,
    ),
    /permission mismatch/i,
  );
  await assert.rejects(
    service.reprintLastReceipt(
      {
        ...reprintAuthorization,
        permissionCode: "Permissions.PosTerminal.CashDrawer.Open",
      },
      activeLease,
    ),
    /permission mismatch/i,
  );
  assert.equal(store.drawerEvents.size, 0);
  assert.equal(store.printJobs.size, 0);
  assert.deepEqual(printer.connectCalls, []);
  assert.deepEqual(drawer.calls, []);
});

test("最后小票重打与手动开箱共用同一 hardwareTail，打印未完成前不能发钱箱脉冲", async () => {
  const { printer, drawer, hardwareTrace, service } = setup();
  let release!: () => void;
  printer.hold = new Promise<void>((resolve) => { release = resolve; });

  const reprint = service.reprintLastReceipt(
    reprintAuthorization,
    activeLease,
  );
  const openDrawer = service.openDrawerManually(
    drawerAuthorization,
    activeLease,
  );
  await new Promise((resolve) => setImmediate(resolve));
  assert.deepEqual(drawer.calls, []);
  assert.deepEqual(hardwareTrace, [
    "connect:XP-REPRINT",
    `print:${reprintAuthorization.actionId}`,
  ]);

  release();
  await Promise.all([reprint, openDrawer]);
  assert.deepEqual(hardwareTrace, [
    "connect:XP-REPRINT",
    `print:${reprintAuthorization.actionId}`,
    "connect:XP-MANUAL-DRAWER",
    "drawer:action-open-drawer",
  ]);
});

test("授权动作到达硬件队列头时租约已失效，不创建重打或开箱任务也不触发对应硬件", async () => {
  const { store, printer, drawer, service } = setup();
  store.printJobs.set("queue-blocker", {
    jobId: "queue-blocker",
    orderGuid: "ORDER-BLOCKER",
    printerId: "XP-BLOCKER",
    isReprint: false,
    bytes: Uint8Array.of(1),
    state: "Queued",
    retryCount: 0,
  });
  let release!: () => void;
  printer.hold = new Promise<void>((resolve) => {
    release = resolve;
  });
  let active = true;
  const assertActive = () => {
    if (!active) throw new Error("CURRENT_CASHIER_REQUIRED");
  };

  const blocker = service.drainAutomaticQueue();
  await new Promise((resolve) => setImmediate(resolve));
  const reprint = service.reprintLastReceipt(
    reprintAuthorization,
    assertActive,
  );
  const openDrawer = service.openDrawerManually(
    drawerAuthorization,
    assertActive,
  );
  active = false;
  release();

  await blocker;
  const results = await Promise.allSettled([reprint, openDrawer]);
  assert.deepEqual(
    results.map((result) => result.status),
    ["rejected", "rejected"],
  );
  assert.deepEqual(printer.calls, ["queue-blocker"]);
  assert.deepEqual(drawer.calls, []);
  assert.deepEqual(store.reprintInputs, []);
  assert.equal(store.drawerEvents.size, 0);
});

test("没有任何历史 Printed job 时仍可为调用方指定订单创建重打任务", async () => {
  const prepared = {
    orderGuid: "ORDER-CASH-WITHOUT-PRINT-HISTORY",
    receiptBytes: Uint8Array.of(29, 33, 82),
    printerId: "XP-CASH",
  };
  const { store, printer, service } = setup({
    prepareLastReceiptReprint: async () => prepared,
  });

  const result = await service.reprintLastReceipt(
    reprintAuthorization,
    activeLease,
  );

  assert.equal(result.state, "Printed");
  assert.equal(
    store.printJobs.get(reprintAuthorization.actionId)?.orderGuid,
    prepared.orderGuid,
  );
  assert.deepEqual(store.reprintInputs, [prepared]);
  assert.deepEqual(printer.connectCalls, ["XP-CASH"]);
});

test("历史页重打只把所选 orderGuid 交给账本准备器，并沿用耐久打印状态机", async () => {
  const requestedOrderGuids: string[] = [];
  const prepared = {
    orderGuid: "ORDER-HISTORY-1",
    receiptBytes: Uint8Array.of(29, 33, 82),
    printerId: "XP-HISTORY",
  };
  const { store, printer, audit, service } = setup({
    async prepareReceiptReprint(orderGuid) {
      requestedOrderGuids.push(orderGuid);
      return orderGuid === prepared.orderGuid ? prepared : null;
    },
  });

  const result = await service.reprintReceipt("ORDER-HISTORY-1");

  assert.deepEqual(requestedOrderGuids, ["ORDER-HISTORY-1"]);
  assert.equal(result.state, "Printed");
  assert.equal(
    store.printJobs.get("reprint-ORDER-HISTORY-1")?.orderGuid,
    "ORDER-HISTORY-1",
  );
  assert.deepEqual(printer.connectCalls, ["XP-HISTORY"]);
  assert.deepEqual(
    audit.map((event) => ({
      action: event.payload.action,
      source: event.payload.source,
    })),
    [{ action: "reprint-history-receipt", source: "remote-history" }],
  );
});

test("硬件动作后 CAS 持久化冲突必须要求恢复，不能宣称打印或开箱成功", async () => {
  const { store, printer, drawer, audit, service } = setup();
  store.printJobs.set("print-failed", { jobId: "print-failed", orderGuid: "O1", printerId: "XP-1", isReprint: false, bytes: Uint8Array.of(1), state: "Failed", retryCount: 0 });
  store.drawerEvents.set("drawer-failed", { eventId: "drawer-failed", orderGuid: "O1", printerId: "XP-1", state: "Failed", reason: "CASH", retryCount: 0 });
  store.printFinishSucceeds = false;
  store.drawerFinishSucceeds = false;

  const printResult = await service.retryFailedPrint("print-failed");
  const drawerResult = await service.retryFailedDrawer("drawer-failed");

  assert.deepEqual(printResult, { state: "recovery-required", errorCode: "DURABILITY_CONFLICT" });
  assert.deepEqual(drawerResult, { state: "recovery-required", errorCode: "DURABILITY_CONFLICT" });
  assert.deepEqual(printer.calls, ["print-failed"]);
  assert.deepEqual(drawer.calls, ["drawer-failed"]);
  assert.equal(store.printJobs.get("print-failed")?.state, "Sending");
  assert.equal(store.drawerEvents.get("drawer-failed")?.state, "Requested");
  assert.deepEqual(audit, []);
  assert.deepEqual(
    store.printFinishCalls.map((call) => ({
      eventType: call.audit?.eventType,
      outcome: call.audit?.payload.outcome,
      status: call.audit?.payload.status,
    })),
    [{ eventType: "RECEIPT_REPRINT", outcome: "Succeeded", status: "Printed" }],
  );
  assert.deepEqual(
    store.drawerFinishCalls.map((call) => ({
      eventType: call.audit.eventType,
      outcome: call.audit.payload.outcome,
      status: call.audit.payload.status,
    })),
    [{ eventType: "CASH_DRAWER_OPEN", outcome: "Succeeded", status: "Completed" }],
  );
});

test("自动队列与手动动作经过同一 BLE 串行队列，不能并发写入打印机", async () => {
  const { store, printer, service } = setup();
  store.printJobs.set("queued", { jobId: "queued", orderGuid: "O1", printerId: "XP-QUEUE", isReprint: false, bytes: Uint8Array.of(1), state: "Queued", retryCount: 0 });
  store.printJobs.set("failed", { jobId: "failed", orderGuid: "O2", printerId: "XP-MANUAL", isReprint: false, bytes: Uint8Array.of(1), state: "Failed", retryCount: 0 });
  let release!: () => void;
  printer.hold = new Promise<void>((resolve) => { release = resolve; });

  const automatic = service.drainAutomaticQueue();
  const manual = service.retryFailedPrint("failed");
  await new Promise((resolve) => setImmediate(resolve));
  assert.equal(printer.maxActive, 1);
  assert.deepEqual(printer.calls, ["queued"]);
  release();
  await Promise.all([automatic, manual]);
  assert.equal(printer.maxActive, 1);
  assert.deepEqual(printer.calls, ["queued", "failed"]);
});

test("每个动作先连接其持久化 printerId；更换当前设置不会改写旧任务路由", async () => {
  const { store, printer, drawer, hardwareTrace, service } = setup();
  store.printJobs.set("print-old", {
    jobId: "print-old",
    orderGuid: "O1",
    printerId: "XP-FROZEN-PRINT",
    isReprint: false,
    bytes: Uint8Array.of(1),
    state: "Queued",
    retryCount: 0,
  });
  store.drawerEvents.set("drawer-old", {
    eventId: "drawer-old",
    orderGuid: "O1",
    printerId: "XP-FROZEN-DRAWER",
    state: "Required",
    reason: "cash-sale",
    retryCount: 0,
  });

  await service.drainAutomaticQueue();

  assert.deepEqual(hardwareTrace, [
    "connect:XP-FROZEN-PRINT",
    "print:print-old",
    "connect:XP-FROZEN-DRAWER",
    "drawer:drawer-old",
  ]);
  assert.deepEqual(printer.connectCalls, ["XP-FROZEN-PRINT", "XP-FROZEN-DRAWER"]);
  assert.deepEqual(drawer.calls, ["drawer-old"]);
});

test("连接失败安全落为 Failed，既不发送打印字节也不发钱箱脉冲", async () => {
  const { store, printer, drawer, audit, service } = setup();
  const connectionError = new Error("contains-secret-details");
  connectionError.name = "BLE_CONNECT_FAILED";
  printer.connectError = connectionError;
  store.printJobs.set("print-connect-failed", {
    jobId: "print-connect-failed",
    orderGuid: "O1",
    printerId: "XP-PRINT",
    isReprint: false,
    bytes: Uint8Array.of(1),
    state: "Queued",
    retryCount: 0,
  });
  store.drawerEvents.set("drawer-connect-failed", {
    eventId: "drawer-connect-failed",
    orderGuid: "O1",
    printerId: "XP-DRAWER",
    state: "Required",
    reason: "cash-sale",
    retryCount: 0,
  });

  const result = await service.drainAutomaticQueue();

  assert.deepEqual(result, { printed: 0, drawersOpened: 0 });
  assert.deepEqual(printer.connectCalls, ["XP-PRINT", "XP-DRAWER"]);
  assert.deepEqual(printer.calls, []);
  assert.deepEqual(drawer.calls, []);
  assert.equal(store.printJobs.get("print-connect-failed")?.state, "Failed");
  assert.equal(store.drawerEvents.get("drawer-connect-failed")?.state, "Failed");
  assert.equal(audit.every((event) => JSON.stringify(event.payload).includes("contains-secret-details") === false), true);
  assert.deepEqual(
    audit.map((event) => ({
      eventType: event.eventType,
      printerId: event.payload.printerId,
      outcome: event.payload.outcome,
    })),
    [{ eventType: "CASH_DRAWER_OPEN", printerId: "XP-DRAWER", outcome: "Failed" }],
  );
});

test("连接成功后的打印抛错仍为 Ambiguous，钱箱抛错仍为 Unknown，禁止自动重放", async () => {
  const { store, printer, drawer, service } = setup();
  printer.printError = Object.assign(new Error("write result is unknown"), { name: "PRINT_WRITE_EXCEPTION" });
  drawer.openError = Object.assign(new Error("pulse result is unknown"), { name: "DRAWER_PULSE_EXCEPTION" });
  store.printJobs.set("print-throws", {
    jobId: "print-throws",
    orderGuid: "O1",
    printerId: "XP-1",
    isReprint: false,
    bytes: Uint8Array.of(1),
    state: "Queued",
    retryCount: 0,
  });
  store.drawerEvents.set("drawer-throws", {
    eventId: "drawer-throws",
    orderGuid: "O1",
    printerId: "XP-1",
    state: "Required",
    reason: "cash-sale",
    retryCount: 0,
  });

  await service.drainAutomaticQueue();
  await service.drainAutomaticQueue();

  assert.equal(store.printJobs.get("print-throws")?.state, "Ambiguous");
  assert.equal(store.drawerEvents.get("drawer-throws")?.state, "Unknown");
  assert.deepEqual(printer.calls, ["print-throws"]);
  assert.deepEqual(drawer.calls, ["drawer-throws"]);
});

test("硬件失败审计只使用 WPF 白名单类型，并把失败结果明确映射为 Failed outcome", async () => {
  const { store, printer, drawer, audit, service } = setup();
  printer.result = { status: "failed", errorCode: "PRINT_OFFLINE" };
  drawer.result = { status: "unknown", errorCode: "PULSE_UNKNOWN" };
  store.printJobs.set("print-failed-audit", {
    jobId: "print-failed-audit",
    orderGuid: "O1",
    printerId: "XP-1",
    isReprint: false,
    bytes: Uint8Array.of(1),
    state: "Failed",
    retryCount: 0,
  });
  store.drawerEvents.set("drawer-failed-audit", {
    eventId: "drawer-failed-audit",
    orderGuid: "O1",
    printerId: "XP-1",
    state: "Failed",
    reason: "cash-sale",
    retryCount: 0,
  });

  await service.retryFailedPrint("print-failed-audit");
  await service.retryFailedDrawer("drawer-failed-audit");

  assert.deepEqual(
    audit.map((event) => ({
      eventType: event.eventType,
      status: event.payload.status,
      outcome: event.payload.outcome,
      source: event.payload.source,
    })),
    [
      { eventType: "RECEIPT_REPRINT", status: "Failed", outcome: "Failed", source: "manual-retry" },
      { eventType: "CASH_DRAWER_OPEN", status: "Unknown", outcome: "Failed", source: "manual-retry" },
    ],
  );
});

test("Store 原子持久化审计抛错时必须 recovery-required，且不存在 finish 后补写审计", async () => {
  const { store, printer, drawer, audit, service } = setup();
  store.printFinishThrows = true;
  store.drawerFinishThrows = true;
  store.printJobs.set("print-atomic-failure", {
    jobId: "print-atomic-failure",
    orderGuid: "O1",
    printerId: "XP-1",
    isReprint: false,
    bytes: Uint8Array.of(1),
    state: "Failed",
    retryCount: 0,
  });
  store.drawerEvents.set("drawer-atomic-failure", {
    eventId: "drawer-atomic-failure",
    orderGuid: "O1",
    printerId: "XP-1",
    state: "Failed",
    reason: "cash-sale",
    retryCount: 0,
  });

  const printResult = await service.retryFailedPrint("print-atomic-failure");
  const drawerResult = await service.retryFailedDrawer("drawer-atomic-failure");

  assert.deepEqual(printResult, { state: "recovery-required", errorCode: "DURABILITY_CONFLICT" });
  assert.deepEqual(drawerResult, { state: "recovery-required", errorCode: "DURABILITY_CONFLICT" });
  assert.equal(store.printJobs.get("print-atomic-failure")?.state, "Sending");
  assert.equal(store.drawerEvents.get("drawer-atomic-failure")?.state, "Requested");
  assert.equal(store.printFinishCalls[0]?.audit?.eventType, "RECEIPT_REPRINT");
  assert.equal(store.drawerFinishCalls[0]?.audit.eventType, "CASH_DRAWER_OPEN");
  assert.deepEqual(audit, []);
  assert.deepEqual(printer.calls, ["print-atomic-failure"]);
  assert.deepEqual(drawer.calls, ["drawer-atomic-failure"]);
});
