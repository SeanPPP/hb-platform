import assert from "node:assert/strict";
import test from "node:test";

import {
  SqliteFulfilmentStore,
  type FulfilmentAuditEvent,
  type FulfilmentInitialAuthorization,
  type PersistedDrawerEventInput,
  type PersistedPrintJobInput,
} from "./sqlite-fulfilment-store";
import type { SqliteConnectionPort, SqlRunResult, SqlValue } from "./types";

type PrintRow = Record<string, SqlValue>;
type DrawerRow = Record<string, SqlValue>;

class FulfilmentConnection implements SqliteConnectionPort {
  public readonly printJobs = new Map<string, PrintRow>();
  public readonly drawerEvents = new Map<string, DrawerRow>();
  public readonly auditEvents = new Map<string, Record<string, SqlValue>>();
  public readonly runs: { sql: string; parameters: readonly SqlValue[] }[] = [];
  public transactions = 0;

  public async exec(): Promise<void> {}

  public async run(sql: string, parameters: readonly SqlValue[] = []): Promise<SqlRunResult> {
    this.runs.push({ sql, parameters });
    if (sql.includes("INSERT INTO audit_events")) {
      const eventId = parameter(parameters, 0);
      this.auditEvents.set(string(eventId), {
        event_id: eventId,
        event_type: parameter(parameters, 1),
        occurred_at_iso: parameter(parameters, 2),
        order_guid: parameter(parameters, 3),
        correlation_id: parameter(parameters, 4),
        payload_json: parameter(parameters, 5),
      });
      return { changes: 1, lastInsertRowId: 0 };
    }
    if (sql.includes("INSERT INTO print_jobs")) {
      const jobId = parameter(parameters, 0); const orderGuid = parameter(parameters, 1); const printerId = parameter(parameters, 2);
      const ciphertext = parameter(parameters, 3); const isReprint = parameter(parameters, 4); const createdAt = parameter(parameters, 5);
      this.printJobs.set(string(jobId), {
        job_id: jobId, order_guid: orderGuid, state: "Queued", printer_id: printerId, receipt_ciphertext: ciphertext,
        is_reprint: isReprint, retry_count: 0, last_error_code: null, created_at_iso: createdAt, updated_at_iso: createdAt,
      });
      return { changes: 1, lastInsertRowId: 0 };
    }
    if (
      sql.includes("INSERT INTO drawer_events") &&
      sql.includes("'Requested'")
    ) {
      const eventId = parameter(parameters, 0);
      const printerId = parameter(parameters, 1);
      const requestedAt = parameter(parameters, 2);
      const createdAt = parameter(parameters, 3);
      const updatedAt = parameter(parameters, 4);
      this.drawerEvents.set(string(eventId), {
        event_id: eventId,
        order_guid: null,
        printer_id: printerId,
        print_job_id: null,
        state: "Requested",
        reason: "MANUAL",
        retry_count: 0,
        requested_at_iso: requestedAt,
        completed_at_iso: null,
        last_error_code: null,
        created_at_iso: createdAt,
        updated_at_iso: updatedAt,
      });
      return { changes: 1, lastInsertRowId: 0 };
    }
    if (sql.includes("INSERT INTO drawer_events")) {
      const eventId = parameter(parameters, 0); const orderGuid = parameter(parameters, 1); const printerId = parameter(parameters, 2);
      const printJobId = parameter(parameters, 3); const reason = parameter(parameters, 4); const createdAt = parameter(parameters, 5);
      this.drawerEvents.set(string(eventId), {
        event_id: eventId, order_guid: orderGuid, printer_id: printerId, print_job_id: printJobId, state: "Required", reason,
        retry_count: 0, requested_at_iso: null, completed_at_iso: null, last_error_code: null,
        created_at_iso: createdAt, updated_at_iso: createdAt,
      });
      return { changes: 1, lastInsertRowId: 0 };
    }
    if (sql.includes("UPDATE print_jobs SET state = 'Sending', retry_count")) {
      const updatedAt = parameter(parameters, 0); const jobId = parameter(parameters, 1);
      const row = this.printJobs.get(string(jobId));
      if (!row || row.state !== "Failed") return { changes: 0, lastInsertRowId: 0 };
      row.state = "Sending"; row.retry_count = Number(row.retry_count) + 1; row.updated_at_iso = updatedAt;
      return { changes: 1, lastInsertRowId: 0 };
    }
    if (sql.includes("UPDATE print_jobs SET state = 'Sending'")) {
      const updatedAt = parameter(parameters, 0); const jobId = parameter(parameters, 1); const expected = parameter(parameters, 2);
      const row = this.printJobs.get(string(jobId));
      if (!row || row.state !== expected) return { changes: 0, lastInsertRowId: 0 };
      row.state = "Sending"; row.updated_at_iso = updatedAt;
      return { changes: 1, lastInsertRowId: 0 };
    }
    if (sql.includes("UPDATE print_jobs SET state = ?")) {
      const state = parameter(parameters, 0); const errorCode = parameter(parameters, 1); const updatedAt = parameter(parameters, 2);
      const jobId = parameter(parameters, 3); const expected = parameter(parameters, 4);
      const row = this.printJobs.get(string(jobId));
      if (!row || row.state !== expected) return { changes: 0, lastInsertRowId: 0 };
      row.state = state; row.last_error_code = errorCode; row.updated_at_iso = updatedAt;
      return { changes: 1, lastInsertRowId: 0 };
    }
    if (sql.includes("UPDATE drawer_events SET state = 'Requested', retry_count")) {
      const requestedAt = parameter(parameters, 0); const updatedAt = parameter(parameters, 1); const eventId = parameter(parameters, 2);
      const row = this.drawerEvents.get(string(eventId));
      if (!row || row.state !== "Failed") return { changes: 0, lastInsertRowId: 0 };
      row.state = "Requested"; row.retry_count = Number(row.retry_count) + 1; row.requested_at_iso = requestedAt; row.updated_at_iso = updatedAt;
      return { changes: 1, lastInsertRowId: 0 };
    }
    if (sql.includes("UPDATE drawer_events SET state = 'Requested'")) {
      const requestedAt = parameter(parameters, 0); const updatedAt = parameter(parameters, 1); const eventId = parameter(parameters, 2); const expected = parameter(parameters, 3);
      const row = this.drawerEvents.get(string(eventId));
      if (!row || row.state !== expected) return { changes: 0, lastInsertRowId: 0 };
      row.state = "Requested"; row.requested_at_iso = requestedAt; row.updated_at_iso = updatedAt;
      return { changes: 1, lastInsertRowId: 0 };
    }
    if (sql.includes("UPDATE drawer_events SET state = ?")) {
      const state = parameter(parameters, 0); const errorCode = parameter(parameters, 1); const completedAt = parameter(parameters, 2);
      const updatedAt = parameter(parameters, 3); const eventId = parameter(parameters, 4); const expected = parameter(parameters, 5);
      const row = this.drawerEvents.get(string(eventId));
      if (!row || row.state !== expected) return { changes: 0, lastInsertRowId: 0 };
      row.state = state; row.last_error_code = errorCode; row.completed_at_iso = completedAt; row.updated_at_iso = updatedAt;
      return { changes: 1, lastInsertRowId: 0 };
    }
    return { changes: 1, lastInsertRowId: 0 };
  }

  public async getFirst<T extends object>(sql: string, parameters: readonly SqlValue[] = []): Promise<T | null> {
    if (sql.includes("FROM local_orders")) return { state: "PendingSync" } as T;
    if (sql.includes("FROM print_jobs") && sql.includes("job_id = ?")) return (this.printJobs.get(string(parameter(parameters, 0))) ?? null) as T | null;
    if (sql.includes("FROM drawer_events") && sql.includes("event_id = ?")) return (this.drawerEvents.get(string(parameter(parameters, 0))) ?? null) as T | null;
    if (sql.includes("FROM print_jobs") && sql.includes("state = 'Printed'")) {
      return ([...this.printJobs.values()].filter((row) => row.state === "Printed").at(-1) ?? null) as T | null;
    }
    return null;
  }

  public async getAll<T extends object>(
    sql: string,
    parameters: readonly SqlValue[] = [],
  ): Promise<readonly T[]> {
    if (sql.includes("FROM audit_events")) {
      const correlationId = parameter(parameters, 0);
      const eventType = parameter(parameters, 1);
      return [...this.auditEvents.values()].filter(
        (row) =>
          row.correlation_id === correlationId &&
          row.event_type === eventType,
      ) as T[];
    }
    if (sql.includes("FROM print_jobs")) return [...this.printJobs.values()].filter((row) => row.state === "Queued") as T[];
    if (sql.includes("FROM drawer_events")) return [...this.drawerEvents.values()].filter((row) => row.state === "Required") as T[];
    return [];
  }

  public async withExclusiveTransaction<T>(operation: (transaction: SqliteConnectionPort) => Promise<T>): Promise<T> {
    this.transactions += 1;
    return operation(this);
  }

  public async close(): Promise<void> {}
}

const encryptor = {
  async encrypt(value: string) { return new TextEncoder().encode(`encrypted:${value}`); },
  async decrypt(value: Uint8Array) {
    const plaintext = new TextDecoder().decode(value);
    if (!plaintext.startsWith("encrypted:")) throw new Error("bad cipher");
    return plaintext.slice("encrypted:".length);
  },
};

function createStore(connection = new FulfilmentConnection()) {
  let sequence = 0;
  return {
    connection,
    store: new SqliteFulfilmentStore(connection, {
      encryptor,
      nowIso: () => "2026-07-28T03:00:00.000Z",
      createPrintJobId: () => `reprint-${++sequence}`,
    }),
  };
}

const printInput: PersistedPrintJobInput = {
  jobId: "print-1", orderGuid: "order-1", printerId: "XP-1", receiptBytes: Uint8Array.of(27, 64), isReprint: false,
};
const drawerInput: PersistedDrawerEventInput = {
  eventId: "drawer-1", orderGuid: "order-1", printerId: "XP-1", printJobId: "print-1", reason: "cash-sale",
};
function drawerAudit(
  eventId: string,
  status: "Completed" | "Failed" | "Unknown",
): FulfilmentAuditEvent {
  return {
    eventId,
    eventType: "CASH_DRAWER_OPEN",
    occurredAtIso: "2026-07-28T03:00:00.000Z",
    orderGuid: "order-1",
    correlationId: "drawer-1",
    payload: {
      action: "open",
      status,
      outcome: status === "Completed" ? "Succeeded" : "Failed",
      printerId: "XP-1",
    },
  };
}

function initialAuthorization(
  input: Readonly<{
    actionId: string;
    eventType: "RECEIPT_REPRINT" | "CASH_DRAWER_OPEN";
    orderGuid: string | null;
    printerId: string;
  }>,
): FulfilmentInitialAuthorization {
  const isReprint = input.eventType === "RECEIPT_REPRINT";
  const context = {
    actionId: input.actionId,
    permissionCode: isReprint
      ? "Permissions.PosTerminal.Receipt.PrintLast"
      : "Permissions.PosTerminal.CashDrawer.Open",
    authorizationMode: "online" as const,
    requestingCashierId: "cashier-1",
    requestingCashierName: "Cashier One",
    requestingUserGuid: "user-1",
    authorizingCashierId: "supervisor-1",
  };
  return {
    context,
    audit: {
      eventId: `audit-${input.actionId}`,
      eventType: input.eventType,
      occurredAtIso: "2026-07-28T03:00:00.000Z",
      orderGuid: input.orderGuid,
      correlationId: input.actionId,
      payload: {
        action: isReprint
          ? "reprint-last-receipt"
          : "open-cash-drawer",
        status: "Authorized",
        reason: isReprint ? "last-receipt" : "MANUAL",
        source: "sales",
        outcome: "Succeeded",
        printerId: input.printerId,
        errorCode: null,
        requestingCashierId: context.requestingCashierId,
        requestingCashierName: context.requestingCashierName,
        requestingUserGuid: context.requestingUserGuid,
        authorizingCashierId: context.authorizingCashierId,
        permissionCode: context.permissionCode,
        authorizationMode: context.authorizationMode,
      },
    },
  };
}

test("履约任务在一个独占事务内写入加密小票、is_reprint 与钱箱 retry_count 初始状态", async () => {
  const { connection, store } = createStore();

  await store.enqueueCashFulfilment({ print: printInput, drawer: drawerInput });

  assert.equal(connection.transactions, 1);
  assert.equal(connection.printJobs.get("print-1")?.state, "Queued");
  assert.equal(connection.drawerEvents.get("drawer-1")?.state, "Required");
  assert.equal(connection.printJobs.get("print-1")?.is_reprint, 0);
  assert.equal(connection.drawerEvents.get("drawer-1")?.retry_count, 0);
  assert.equal(connection.printJobs.get("print-1")?.printer_id, "XP-1");
  assert.equal(connection.drawerEvents.get("drawer-1")?.printer_id, "XP-1");
  assert.ok(connection.runs.some((entry) => entry.sql.includes("receipt_ciphertext")));
  assert.equal(new TextDecoder().decode(connection.printJobs.get("print-1")?.receipt_ciphertext as Uint8Array).includes("27,64"), true);
});

test("重启后自动队列仍只返回 Queued/Required，Sending、Ambiguous、Requested 和 Unknown 不会重放", async () => {
  const { connection, store } = createStore();
  await store.enqueueCashFulfilment({ print: printInput, drawer: drawerInput });
  connection.printJobs.get("print-1")!.state = "Sending";
  connection.printJobs.set("print-ambiguous", { ...connection.printJobs.get("print-1")!, job_id: "print-ambiguous", state: "Ambiguous" });
  connection.drawerEvents.get("drawer-1")!.state = "Requested";
  connection.drawerEvents.set("drawer-unknown", { ...connection.drawerEvents.get("drawer-1")!, event_id: "drawer-unknown", state: "Unknown" });
  connection.printJobs.set("print-queued", { ...connection.printJobs.get("print-1")!, job_id: "print-queued", state: "Queued" });
  connection.drawerEvents.set("drawer-required", { ...connection.drawerEvents.get("drawer-1")!, event_id: "drawer-required", state: "Required" });

  const restarted = createStore(connection).store;
  assert.deepEqual(
    (await restarted.listQueuedPrintJobs()).map((job) => [job.jobId, job.printerId]),
    [["print-queued", "XP-1"]],
  );
  assert.deepEqual(
    (await restarted.listRequiredDrawerEvents()).map((event) => [event.eventId, event.printerId]),
    [["drawer-required", "XP-1"]],
  );
});

test("手动开箱事件直接以 Requested 和空订单写入；同 actionId 幂等且不会进入自动队列", async () => {
  const { connection, store } = createStore();
  const authorization = initialAuthorization({
    actionId: "manual-action-1",
    eventType: "CASH_DRAWER_OPEN",
    orderGuid: null,
    printerId: "XP-MANUAL",
  });

  const first = await store.beginManualDrawerOpen({
    eventId: "manual-action-1",
    printerId: "XP-MANUAL",
    reason: "MANUAL",
  }, authorization);
  const replay = await store.beginManualDrawerOpen({
    eventId: "manual-action-1",
    printerId: "XP-MANUAL",
    reason: "MANUAL",
  }, authorization);

  assert.deepEqual(first, {
    kind: "created",
    event: {
      eventId: "manual-action-1",
      orderGuid: null,
      printerId: "XP-MANUAL",
      state: "Requested",
      reason: "MANUAL",
      retryCount: 0,
      authorization: authorization.context,
    },
  });
  assert.deepEqual(replay, {
    kind: "existing",
    event: {
      eventId: "manual-action-1",
      orderGuid: null,
      printerId: "XP-MANUAL",
      state: "Requested",
      reason: "MANUAL",
      retryCount: 0,
      authorization: authorization.context,
    },
  });
  assert.equal(connection.drawerEvents.get("manual-action-1")?.order_guid, null);
  assert.equal(connection.drawerEvents.get("manual-action-1")?.state, "Requested");
  assert.equal(connection.auditEvents.size, 1);
  assert.deepEqual(await store.listRequiredDrawerEvents(), []);

  connection.drawerEvents.get("manual-action-1")!.state = "Failed";
  const retry = await store.beginManualDrawerRetry("manual-action-1");
  assert.deepEqual(retry?.authorization, authorization.context);
});

test("手动开箱同 actionId 改绑打印机或碰撞订单事件时必须 fail-closed", async () => {
  const { store } = createStore();
  await store.beginManualDrawerOpen({
    eventId: "manual-action-conflict",
    printerId: "XP-FIRST",
    reason: "MANUAL",
  }, initialAuthorization({
    actionId: "manual-action-conflict",
    eventType: "CASH_DRAWER_OPEN",
    orderGuid: null,
    printerId: "XP-FIRST",
  }));

  await assert.rejects(
    store.beginManualDrawerOpen({
      eventId: "manual-action-conflict",
      printerId: "XP-SECOND",
      reason: "MANUAL",
    }, initialAuthorization({
      actionId: "manual-action-conflict",
      eventType: "CASH_DRAWER_OPEN",
      orderGuid: null,
      printerId: "XP-SECOND",
    })),
    /manual drawer action conflict/i,
  );

  await store.enqueueDrawerEvent({
    eventId: "automatic-event",
    orderGuid: "order-1",
    printerId: "XP-FIRST",
    printJobId: null,
    reason: "cash-sale",
  });
  await assert.rejects(
    store.beginManualDrawerOpen({
      eventId: "automatic-event",
      printerId: "XP-FIRST",
      reason: "MANUAL",
    }, initialAuthorization({
      actionId: "automatic-event",
      eventType: "CASH_DRAWER_OPEN",
      orderGuid: null,
      printerId: "XP-FIRST",
    })),
    /manual drawer action conflict/i,
  );
});

test("首份履约授权审计必须整套匹配员工三字段，不能混搭身份", async () => {
  const { store } = createStore();
  const authorization = initialAuthorization({
    actionId: "manual-actor-mismatch",
    eventType: "CASH_DRAWER_OPEN",
    orderGuid: null,
    printerId: "XP-MANUAL",
  });

  await assert.rejects(
    async () => store.beginManualDrawerOpen(
      {
        eventId: authorization.context.actionId,
        printerId: "XP-MANUAL",
        reason: "MANUAL",
      },
      {
        ...authorization,
        audit: {
          ...authorization.audit,
          payload: {
            ...authorization.audit.payload,
            requestingUserGuid: "other-user",
          },
        },
      },
    ),
    /authorization audit does not match/i,
  );
});

test("领取与完成均比较期望状态；Failed 只能人工重试且两个 retry_count 都递增", async () => {
  const { connection, store } = createStore();
  await store.enqueueCashFulfilment({ print: printInput, drawer: drawerInput });

  const print = await store.claimQueuedPrintJob("print-1");
  assert.equal(print?.state, "Sending");
  assert.equal(print?.printerId, "XP-1");
  assert.equal(await store.claimQueuedPrintJob("print-1"), null);
  assert.equal(await store.finishPrintJob("print-1", "Sending", "Failed", "BLE_LOST", null), true);
  assert.equal((await store.beginManualPrintRetry("print-1"))?.retryCount, 1);
  assert.equal(await store.finishPrintJob("print-1", "Sending", "Ambiguous", "TIMEOUT", null), true);
  assert.equal(await store.beginManualPrintRetry("print-1"), null);

  const drawer = await store.claimRequiredDrawerEvent("drawer-1");
  assert.equal(drawer?.state, "Requested");
  assert.equal(drawer?.printerId, "XP-1");
  assert.equal(await store.finishDrawerEvent("drawer-1", "Requested", "Failed", "OFFLINE", drawerAudit("audit-drawer-failed", "Failed")), true);
  assert.equal((await store.beginManualDrawerRetry("drawer-1"))?.retryCount, 1);
  assert.equal(await store.finishDrawerEvent("drawer-1", "Requested", "Unknown", "PULSE_TIMEOUT", drawerAudit("audit-drawer-unknown", "Unknown")), true);
  assert.equal(await store.beginManualDrawerRetry("drawer-1"), null);
  assert.match(String(connection.printJobs.get("print-1")?.last_error_code), /TIMEOUT/);
  assert.match(String(connection.drawerEvents.get("drawer-1")?.last_error_code), /PULSE_TIMEOUT/);
});

test("重打必须传入已带重打标记的预渲染字节，源作业不被复制或改写", async () => {
  const { connection, store } = createStore();
  await store.enqueuePrintJob(printInput);
  connection.printJobs.get("print-1")!.state = "Printed";

  const reprint = await store.createLastReceiptReprint({
    orderGuid: "order-1",
    receiptBytes: Uint8Array.of(29, 33),
    printerId: "XP-1",
  });

  assert.equal(reprint?.isReprint, true);
  assert.equal(reprint?.state, "Queued");
  assert.equal(reprint?.printerId, "XP-1");
  assert.equal(connection.printJobs.get("print-1")?.state, "Printed");
  assert.deepEqual(reprint?.bytes, Uint8Array.of(29, 33));
});

test("新履约任务拒绝空 printerId，并拒绝钱箱绑定到另一打印任务的外设", async () => {
  const { connection, store } = createStore();

  await assert.rejects(
    store.enqueuePrintJob({ ...printInput, printerId: "  " }),
    /printer id is required/i,
  );
  assert.equal(connection.printJobs.size, 0);

  await store.enqueuePrintJob(printInput);
  await assert.rejects(
    store.enqueueDrawerEvent({
      ...drawerInput,
      eventId: "drawer-wrong",
      printerId: "XP-2",
    }),
    /printer does not match/i,
  );
  assert.equal(connection.drawerEvents.size, 0);

  await assert.rejects(
    store.enqueueDrawerEvent({
      ...drawerInput,
      eventId: "drawer-empty",
      printerId: "",
      printJobId: null,
    }),
    /printer id is required/i,
  );
  assert.equal(connection.drawerEvents.size, 0);
});

function string(value: SqlValue | undefined): string {
  if (typeof value !== "string") throw new Error("Expected text.");
  return value;
}

function parameter(parameters: readonly SqlValue[], index: number): SqlValue {
  const value = parameters[index];
  if (value === undefined) throw new Error("Missing SQL parameter.");
  return value;
}
