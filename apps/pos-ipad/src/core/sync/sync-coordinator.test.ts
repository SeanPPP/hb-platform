import assert from "node:assert/strict";
import test from "node:test";

import {
  UPDATE_TRANSITION_IN_PROGRESS,
  UpdateTransitionLeaseCoordinator,
} from "../../features/app-updates/update-transition-lease-coordinator";
import type { AuditEventDraft } from "../contracts/order";
import type {
  AuditRepositoryPort,
  OperationAuditDeliveryEvent,
  OperationAuditDeliveryPort,
  OutboxLease,
  OutboxRepositoryPort,
} from "../contracts/repositories";
import type { OrderSyncPort, SyncOrderResult } from "../contracts/sync";

import {
  PosSyncCoordinator,
  SyncLifecycleController,
  type AuditBatchUploadPort,
  type SyncSecurityPort,
} from "./sync-coordinator";

function lease(messageId: string, attemptCount = 0): OutboxLease {
  return {
    messageId,
    leaseId: `lease-${messageId}`,
    aggregateId: `order-${messageId}`,
    kind: "order-sync",
    payloadJson: "{}",
    attemptCount,
  };
}

class FakeOutbox implements OutboxRepositoryPort {
  public readonly succeeded: string[] = [];
  public readonly retries: { messageId: string; nextAttemptAtIso: string; errorCode: string }[] = [];
  public readonly blocked: { messageId: string; errorCode: string }[] = [];
  public readonly rejected: { messageId: string; errorCode: string }[] = [];
  public leaseCalls = 0;
  public nextReadyAtCalls = 0;
  private readonly nextReadyAtValues: (string | null)[];

  public constructor(
    private readonly batches: readonly (readonly OutboxLease[])[],
    nextReadyAtValues: readonly (string | null)[] = [],
  ) {
    this.nextReadyAtValues = [...nextReadyAtValues];
  }

  public async enqueue(): Promise<void> {}

  public async leaseReady(): Promise<readonly OutboxLease[]> {
    return this.batches[this.leaseCalls++] ?? [];
  }

  public async nextReadyAtIso(): Promise<string | null> {
    this.nextReadyAtCalls += 1;
    return this.nextReadyAtValues.shift() ?? null;
  }

  public async markSucceeded(item: OutboxLease): Promise<void> {
    this.succeeded.push(item.messageId);
  }

  public async releaseRetry(item: OutboxLease, nextAttemptAtIso: string, errorCode: string): Promise<void> {
    this.retries.push({ messageId: item.messageId, nextAttemptAtIso, errorCode });
  }

  public async markBlocked403(item: OutboxLease, errorCode: string): Promise<void> {
    this.blocked.push({ messageId: item.messageId, errorCode });
  }

  public async markRejected(item: OutboxLease, errorCode: string): Promise<void> {
    this.rejected.push({ messageId: item.messageId, errorCode });
  }
}

class HandoffOutbox implements OutboxRepositoryPort {
  public readonly succeeded: string[] = [];
  public readonly retries: { messageId: string; nextAttemptAtIso: string; errorCode: string }[] = [];
  public leaseCalls = 0;
  private initialLeased = false;
  private readonly ready: OutboxLease[] = [];

  public constructor(private readonly initial: readonly OutboxLease[]) {}

  public async enqueue(): Promise<void> {}

  public async leaseReady(limit: number): Promise<readonly OutboxLease[]> {
    this.leaseCalls += 1;
    if (!this.initialLeased) {
      this.initialLeased = true;
      return this.initial.slice(0, limit);
    }
    return this.ready.splice(0, limit).map((item, index) => ({
      ...item,
      leaseId: `handoff-${this.leaseCalls}-${index}`,
    }));
  }

  public async nextReadyAtIso(): Promise<string | null> {
    return null;
  }

  public async markSucceeded(item: OutboxLease): Promise<void> {
    this.succeeded.push(item.messageId);
  }

  public async releaseRetry(item: OutboxLease, nextAttemptAtIso: string, errorCode: string): Promise<void> {
    this.retries.push({ messageId: item.messageId, nextAttemptAtIso, errorCode });
    this.ready.push({ ...item, attemptCount: item.attemptCount + 1 });
  }

  public async markBlocked403(): Promise<void> {}

  public async markRejected(): Promise<void> {}
}

class FakeAuditRepository implements AuditRepositoryPort {
  public uploaded: string[] = [];

  public constructor(private readonly pending: AuditEventDraft[] = []) {}

  public async append(): Promise<void> {}

  public async listPending(limit: number): Promise<readonly AuditEventDraft[]> {
    return this.pending.slice(0, limit);
  }

  public async markUploaded(eventIds: readonly string[]): Promise<void> {
    this.uploaded.push(...eventIds);
    this.pending.splice(0, eventIds.length);
  }
}

class FakeAuditDelivery implements OperationAuditDeliveryPort {
  public readonly uploaded: string[] = [];
  public readonly rejected: { eventId: string; code: string }[] = [];
  public readonly retries: { eventIds: readonly string[]; nextAttemptAtIso: string; errorCode: string }[] = [];
  private readonly retrying = new Set<string>();

  public constructor(private readonly pending: OperationAuditDeliveryEvent[]) {}

  public async listReady(limit: number): Promise<readonly OperationAuditDeliveryEvent[]> {
    return this.pending.filter((event) => !this.retrying.has(event.eventId)).slice(0, limit);
  }

  public async markUploaded(eventIds: readonly string[]): Promise<void> {
    this.uploaded.push(...eventIds);
    this.remove(eventIds);
  }

  public async markRejected(entries: readonly Readonly<{ eventId: string; code: string }>[]): Promise<void> {
    this.rejected.push(...entries);
    this.remove(entries.map((entry) => entry.eventId));
  }

  public async releaseRetry(eventIds: readonly string[], nextAttemptAtIso: string, errorCode: string): Promise<void> {
    this.retries.push({ eventIds, nextAttemptAtIso, errorCode });
    eventIds.forEach((eventId) => this.retrying.add(eventId));
  }

  public async nextReadyAtIso(): Promise<string | null> {
    return this.retrying.size ? this.retries.at(-1)?.nextAttemptAtIso ?? null : null;
  }

  public makeRetriesReady(): void {
    this.retrying.clear();
  }

  private remove(eventIds: readonly string[]): void {
    const ids = new Set(eventIds);
    eventIds.forEach((eventId) => this.retrying.delete(eventId));
    for (let index = this.pending.length - 1; index >= 0; index -= 1) {
      if (ids.has(this.pending[index]!.eventId)) this.pending.splice(index, 1);
    }
  }
}

function audit(eventId: string): AuditEventDraft {
  return { eventId, eventType: "cash-sale", occurredAtIso: "2026-07-28T00:00:00.000Z", orderGuid: null, correlationId: eventId, payload: {} };
}

function createCoordinator(
  outbox: FakeOutbox,
  results: Readonly<Record<string, SyncOrderResult>>,
  auditRepository = new FakeAuditRepository(),
  auditUploader: AuditBatchUploadPort = { async upload() { return { kind: "uploaded" }; } },
  security: SyncSecurityPort = { async lockDevice() {} },
): PosSyncCoordinator {
  const orderSync: OrderSyncPort = {
    async sync(orderGuid) {
      return results[orderGuid] ?? { kind: "synced", alreadySynced: false };
    },
  };
  return new PosSyncCoordinator({
    outbox,
    auditRepository,
    orderSync,
    auditUploader,
    security,
    now: () => new Date("2026-07-28T00:00:00.000Z"),
    random: () => 0.5,
  });
}

test("AlreadySynced 等同成功；retry/403/业务拒绝保持各自的 outbox 状态", async () => {
  const outbox = new FakeOutbox([[lease("already"), lease("retry", 2), lease("blocked"), lease("rejected")]]);
  const locked: string[] = [];
  const coordinator = createCoordinator(outbox, {
    "order-already": { kind: "synced", alreadySynced: true },
    "order-retry": { kind: "retry", failure: "unauthorized" },
    "order-blocked": { kind: "blocked", failure: "forbidden", code: "DEVICE_DISABLED" },
    "order-rejected": { kind: "rejected", failure: "business-rejection", code: "ORDER_REJECTED" },
  }, new FakeAuditRepository(), undefined, {
    async lockDevice(reason) {
      locked.push(reason);
    },
  });

  await coordinator.requestDrain();

  assert.deepEqual(outbox.succeeded, ["already"]);
  assert.deepEqual(outbox.retries, [{ messageId: "retry", nextAttemptAtIso: "2026-07-28T00:00:04.000Z", errorCode: "SYNC_UNAUTHORIZED" }]);
  assert.deepEqual(outbox.blocked, [{ messageId: "blocked", errorCode: "DEVICE_DISABLED" }]);
  assert.deepEqual(outbox.rejected, [{ messageId: "rejected", errorCode: "ORDER_REJECTED" }]);
  assert.deepEqual(locked, ["DEVICE_DISABLED"]);
});

test("启动、前台、联网恢复共享同一个单飞 drain，不会重复补传同一 OrderGuid", async () => {
  const outbox = new FakeOutbox([[lease("one")]]);
  let calls = 0;
  let releaseSync: (() => void) | undefined;
  const orderSync: OrderSyncPort = {
    async sync() {
      calls += 1;
      await new Promise<void>((resolve) => {
        releaseSync = resolve;
      });
      return { kind: "synced", alreadySynced: false };
    },
  };
  const coordinator = new PosSyncCoordinator({
    outbox,
    auditRepository: new FakeAuditRepository(),
    orderSync,
    auditUploader: { async upload() { return { kind: "uploaded" }; } },
    security: { async lockDevice() {} },
    now: () => new Date("2026-07-28T00:00:00.000Z"),
    random: () => 0.5,
  });
  const lifecycle = new SyncLifecycleController(coordinator);

  const started = lifecycle.onApplicationStarted();
  const foreground = lifecycle.onForeground();
  const network = lifecycle.onNetworkChanged(true);
  await Promise.resolve();
  assert.equal(calls, 1);
  releaseSync?.();
  await Promise.all([started, foreground, network]);

  assert.ok(outbox.leaseCalls >= 2);
  assert.deepEqual(outbox.succeeded, ["one"]);
});

test("一次触发会连续租赁所有 ready 批次，而不是处理 25 笔后停住", async () => {
  const first = Array.from({ length: 25 }, (_, index) =>
    lease(`first-${index}`),
  );
  const second = [lease("second-0"), lease("second-1")];
  const outbox = new FakeOutbox([first, second, []]);
  const coordinator = createCoordinator(outbox, {});

  const report = await coordinator.requestDrain();

  assert.equal(report.leased, 27);
  assert.equal(report.orderSucceeded, 27);
  assert.equal(outbox.leaseCalls, 3);
  assert.equal(outbox.succeeded.length, 27);
});

test("空队列读取期间到达的新唤醒会锁存第二轮 drain，不会并入旧 Promise 后遗漏", async () => {
  const emptyReadStarted = deferred<void>();
  const releaseEmptyRead = deferred<void>();
  let leaseCalls = 0;
  const succeeded: string[] = [];
  const outbox: OutboxRepositoryPort = {
    async enqueue() {},
    async leaseReady() {
      leaseCalls += 1;
      if (leaseCalls === 1) {
        emptyReadStarted.resolve();
        await releaseEmptyRead.promise;
        return [];
      }
      if (leaseCalls === 2) return [lease("late-order")];
      return [];
    },
    async nextReadyAtIso() {
      return null;
    },
    async markSucceeded(item) {
      succeeded.push(item.messageId);
    },
    async releaseRetry() {},
    async markBlocked403() {},
    async markRejected() {},
  };
  const coordinator = new PosSyncCoordinator({
    outbox,
    auditRepository: new FakeAuditRepository(),
    orderSync: {
      async sync() {
        return { kind: "synced", alreadySynced: false };
      },
    },
    auditUploader: {
      async upload() {
        return { kind: "uploaded" };
      },
    },
    security: { async lockDevice() {} },
    now: () => new Date("2026-07-28T00:00:00.000Z"),
  });

  const firstDrain = coordinator.requestDrain();
  await emptyReadStarted.promise;
  const overlappingWake = coordinator.requestDrain();
  assert.equal(overlappingWake, firstDrain);
  releaseEmptyRead.resolve();
  await firstDrain;

  assert.deepEqual(succeeded, ["late-order"]);
  assert.equal(leaseCalls, 3);
});

test("计算下一唤醒时间期间到达的新唤醒也会锁存第二轮 drain", async () => {
  const nextReadyReadStarted = deferred<void>();
  const releaseNextReadyRead = deferred<void>();
  let leaseCalls = 0;
  let nextReadyCalls = 0;
  const succeeded: string[] = [];
  const outbox: OutboxRepositoryPort = {
    async enqueue() {},
    async leaseReady() {
      leaseCalls += 1;
      if (leaseCalls === 1) return [];
      if (leaseCalls === 2) return [lease("late-during-schedule")];
      return [];
    },
    async nextReadyAtIso() {
      nextReadyCalls += 1;
      if (nextReadyCalls === 1) {
        nextReadyReadStarted.resolve();
        await releaseNextReadyRead.promise;
      }
      return null;
    },
    async markSucceeded(item) {
      succeeded.push(item.messageId);
    },
    async releaseRetry() {},
    async markBlocked403() {},
    async markRejected() {},
  };
  const coordinator = new PosSyncCoordinator({
    outbox,
    auditRepository: new FakeAuditRepository(),
    orderSync: {
      async sync() {
        return { kind: "synced", alreadySynced: false };
      },
    },
    auditUploader: {
      async upload() {
        return { kind: "uploaded" };
      },
    },
    security: { async lockDevice() {} },
    now: () => new Date("2026-07-28T00:00:00.000Z"),
  });

  const firstDrain = coordinator.requestDrain();
  await nextReadyReadStarted.promise;
  const overlappingWake = coordinator.requestDrain();
  assert.equal(overlappingWake, firstDrain);
  releaseNextReadyRead.resolve();
  await firstDrain;

  assert.deepEqual(succeeded, ["late-during-schedule"]);
  assert.equal(leaseCalls, 3);
  assert.equal(nextReadyCalls, 2);
});

test("未来重试到期后由单个定时唤醒自动继续 drain", async () => {
  let now = new Date("2026-07-28T00:00:00.000Z");
  const scheduled: {
    callback: () => void;
    delayMs: number;
  }[] = [];
  const outbox = new FakeOutbox(
    [[lease("retry")], [], [lease("after-retry")], []],
    ["2026-07-28T00:00:01.000Z", null],
  );
  const coordinator = new PosSyncCoordinator({
    outbox,
    auditRepository: new FakeAuditRepository(),
    orderSync: {
      async sync(orderGuid) {
        return orderGuid === "order-retry"
          ? { kind: "retry", failure: "network" }
          : { kind: "synced", alreadySynced: false };
      },
    },
    auditUploader: {
      async upload() {
        return { kind: "uploaded" };
      },
    },
    security: { async lockDevice() {} },
    now: () => now,
    random: () => 0.5,
    timer: {
      set(delayMs, callback) {
        scheduled.push({ callback, delayMs });
        return scheduled.length;
      },
      clear() {},
    },
  });

  await coordinator.requestDrain();
  assert.deepEqual(scheduled.map((entry) => entry.delayMs), [1_000]);

  now = new Date("2026-07-28T00:00:01.000Z");
  scheduled[0]?.callback();
  await waitUntil(() => outbox.succeeded.includes("after-retry"));

  assert.deepEqual(outbox.succeeded, ["after-retry"]);
  assert.equal(scheduled.length, 1);
});

test("关闭同步协调器会清理待唤醒 timer，且关闭后不再访问持久队列", async () => {
  const scheduled: { delayMs: number; callback: () => void; handle: symbol }[] = [];
  const cleared: symbol[] = [];
  const outbox = new FakeOutbox(
    [[]],
    ["2026-07-28T00:01:00.000Z"],
  );
  const coordinator = new PosSyncCoordinator({
    outbox,
    auditRepository: new FakeAuditRepository(),
    orderSync: { async sync() { return { kind: "synced", alreadySynced: false }; } },
    auditUploader: { async upload() { return { kind: "uploaded" }; } },
    security: { async lockDevice() {} },
    now: () => new Date("2026-07-28T00:00:00.000Z"),
    timer: {
      set(delayMs, callback) {
        const handle = Symbol("retry-timer");
        scheduled.push({ delayMs, callback, handle });
        return handle;
      },
      clear(handle) {
        cleared.push(handle as symbol);
      },
    },
  });

  await coordinator.requestDrain();
  assert.equal(scheduled[0]?.delayMs, 60_000);

  await coordinator.shutdown();
  await coordinator.shutdown();
  assert.deepEqual(cleared, [scheduled[0]?.handle]);

  scheduled[0]?.callback();
  await Promise.resolve();
  await coordinator.requestDrain();
  assert.equal(outbox.leaseCalls, 1);
  assert.equal(outbox.nextReadyAtCalls, 1);
});

test("关闭会等待当前订单同步持久化终态，再允许关闭数据库且不启动下一项或审计", async () => {
  const syncStarted = deferred<void>();
  const releaseSync = deferred<void>();
  const outbox = new FakeOutbox([[lease("active"), lease("next")]]);
  const synced: string[] = [];
  let auditUploads = 0;
  const coordinator = new PosSyncCoordinator({
    outbox,
    auditRepository: new FakeAuditRepository([audit("must-not-upload")]),
    orderSync: {
      async sync(orderGuid) {
        synced.push(orderGuid);
        syncStarted.resolve(undefined);
        await releaseSync.promise;
        return { kind: "synced", alreadySynced: false };
      },
    },
    auditUploader: {
      async upload() {
        auditUploads += 1;
        return { kind: "uploaded" };
      },
    },
    security: { async lockDevice() {} },
    now: () => new Date("2026-07-28T00:00:00.000Z"),
  });

  const drain = coordinator.requestDrain();
  await syncStarted.promise;
  let databaseClosed = false;
  const closeRuntime = coordinator.shutdown().then(() => {
    databaseClosed = true;
  });
  await Promise.resolve();
  assert.equal(databaseClosed, false);

  releaseSync.resolve(undefined);
  await Promise.all([drain, closeRuntime]);

  assert.equal(databaseClosed, true);
  assert.deepEqual(synced, ["order-active"]);
  assert.deepEqual(outbox.succeeded, ["active"]);
  assert.equal(auditUploads, 0);
});

test("关闭会立即回收同批未开始 leases，新 coordinator 可接管并同步", async () => {
  const syncStarted = deferred<void>();
  const releaseSync = deferred<void>();
  const outbox = new HandoffOutbox([lease("active"), lease("handoff")]);
  const firstCoordinator = new PosSyncCoordinator({
    outbox,
    auditRepository: new FakeAuditRepository(),
    orderSync: {
      async sync() {
        syncStarted.resolve(undefined);
        await releaseSync.promise;
        return { kind: "synced", alreadySynced: false };
      },
    },
    auditUploader: { async upload() { return { kind: "uploaded" }; } },
    security: { async lockDevice() {} },
    now: () => new Date("2026-07-28T00:00:00.000Z"),
  });

  const draining = firstCoordinator.requestDrain();
  await syncStarted.promise;
  const shutdown = firstCoordinator.shutdown();
  releaseSync.resolve(undefined);
  await Promise.all([draining, shutdown]);

  assert.deepEqual(outbox.retries, [{
    messageId: "handoff",
    nextAttemptAtIso: "2026-07-28T00:00:00.000Z",
    errorCode: "SYNC_RUNTIME_SHUTDOWN",
  }]);

  const secondSynced: string[] = [];
  const secondCoordinator = new PosSyncCoordinator({
    outbox,
    auditRepository: new FakeAuditRepository(),
    orderSync: {
      async sync(orderGuid) {
        secondSynced.push(orderGuid);
        return { kind: "synced", alreadySynced: false };
      },
    },
    auditUploader: { async upload() { return { kind: "uploaded" }; } },
    security: { async lockDevice() {} },
    now: () => new Date("2026-07-28T00:00:00.000Z"),
  });

  await secondCoordinator.requestDrain();

  assert.deepEqual(secondSynced, ["order-handoff"]);
  assert.deepEqual(outbox.succeeded, ["active", "handoff"]);
});

test("关闭会等待当前员工审计上传写入终态，但不读取下一批", async () => {
  const uploadStarted = deferred<void>();
  const releaseUpload = deferred<void>();
  const event = { ...audit("audit-active"), attemptCount: 0 };
  const delivery = new FakeAuditDelivery([event]);
  let uploads = 0;
  const coordinator = new PosSyncCoordinator({
    outbox: new FakeOutbox([[]]),
    auditRepository: new FakeAuditRepository(),
    auditDelivery: delivery,
    orderSync: { async sync() { return { kind: "synced", alreadySynced: false }; } },
    auditUploader: {
      async upload() {
        uploads += 1;
        uploadStarted.resolve(undefined);
        await releaseUpload.promise;
        return { kind: "uploaded" };
      },
    },
    security: { async lockDevice() {} },
    now: () => new Date("2026-07-28T00:00:00.000Z"),
  });

  const drain = coordinator.requestDrain();
  await uploadStarted.promise;
  const shutdown = coordinator.shutdown();
  releaseUpload.resolve(undefined);
  await Promise.all([drain, shutdown]);

  assert.equal(uploads, 1);
  assert.deepEqual(delivery.uploaded, [event.eventId]);
});

test("审计上传每批最多8；403 保持未上传并触发同一锁机回调", async () => {
  const events = Array.from({ length: 9 }, (_, index) => audit(`audit-${index}`));
  const auditRepository = new FakeAuditRepository(events);
  const batchSizes: number[] = [];
  const coordinator = createCoordinator(
    new FakeOutbox([[]]),
    {},
    auditRepository,
    {
      async upload(batch) {
        batchSizes.push(batch.length);
        return { kind: "uploaded" };
      },
    },
  );

  await coordinator.requestDrain();
  assert.deepEqual(batchSizes, [8, 1]);
  assert.equal(auditRepository.uploaded.length, 9);

  const locked: string[] = [];
  const blockedAudit = new FakeAuditRepository([audit("audit-blocked")]);
  const blockedCoordinator = createCoordinator(
    new FakeOutbox([[]]),
    {},
    blockedAudit,
    { async upload() { return { kind: "blocked", code: "DEVICE_DISABLED" }; } },
    { async lockDevice(reason) { locked.push(reason); } },
  );
  await blockedCoordinator.requestDrain();
  assert.deepEqual(blockedAudit.uploaded, []);
  assert.deepEqual(locked, ["DEVICE_DISABLED"]);

  const unauthorizedAudit = new FakeAuditRepository([audit("audit-unauthorized")]);
  const unauthorizedCoordinator = createCoordinator(
    new FakeOutbox([[]]),
    {},
    unauthorizedAudit,
    { async upload() { return { kind: "retry", failure: "unauthorized" }; } },
  );
  await unauthorizedCoordinator.requestDrain();
  assert.deepEqual(unauthorizedAudit.uploaded, []);
});

test("员工审计逐项确认后只删除 accepted，rejected 被隔离且不阻塞后续事件", async () => {
  const first = { ...audit("audit-accepted"), attemptCount: 0 };
  const bad = { ...audit("audit-rejected"), attemptCount: 0 };
  const later = { ...audit("audit-later"), attemptCount: 0 };
  const delivery = new FakeAuditDelivery([first, bad, later]);
  const coordinator = new PosSyncCoordinator({
    outbox: new FakeOutbox([[]]),
    auditRepository: new FakeAuditRepository(),
    auditDelivery: delivery,
    orderSync: { async sync() { return { kind: "synced", alreadySynced: false }; } },
    auditUploader: {
      async upload(events) {
        if (events[0]?.eventId === first.eventId) {
          return {
            kind: "acknowledged",
            uploadedEventIds: [first.eventId],
            rejected: [{ eventId: bad.eventId, code: "INVALID_EVENT" }],
          };
        }
        return { kind: "uploaded" };
      },
    },
    security: { async lockDevice() {} },
    now: () => new Date("2026-07-28T00:00:00.000Z"),
    random: () => 0.5,
  });

  const report = await coordinator.requestDrain();

  assert.deepEqual(delivery.uploaded, [first.eventId, later.eventId]);
  assert.deepEqual(delivery.rejected, [{ eventId: bad.eventId, code: "INVALID_EVENT" }]);
  assert.equal(report.auditUploaded, 2);
});

test("员工审计混合回执先推进终态，仅对缺失回执事件 durable retry", async () => {
  const accepted = { ...audit("audit-accepted"), attemptCount: 0 };
  const missing = { ...audit("audit-missing"), attemptCount: 1 };
  const delivery = new FakeAuditDelivery([accepted, missing]);
  const coordinator = new PosSyncCoordinator({
    outbox: new FakeOutbox([[]]),
    auditRepository: new FakeAuditRepository(),
    auditDelivery: delivery,
    orderSync: { async sync() { return { kind: "synced", alreadySynced: false }; } },
    auditUploader: {
      async upload() {
        return {
          kind: "acknowledged",
          uploadedEventIds: [accepted.eventId],
          rejected: [],
          retryEventIds: [missing.eventId],
        };
      },
    },
    security: { async lockDevice() {} },
    now: () => new Date("2026-07-28T00:00:00.000Z"),
    random: () => 0.5,
  });

  await coordinator.requestDrain();

  assert.deepEqual(delivery.uploaded, [accepted.eventId]);
  assert.deepEqual(delivery.retries, [{
    eventIds: [missing.eventId],
    nextAttemptAtIso: "2026-07-28T00:02:07.500Z",
    errorCode: "AUDIT_SERVER",
  }]);
});

test("旧 auditRepository 兼容分支遇到部分回执会结束本轮，不重复读取未确认事件", async () => {
  const first = audit("legacy-accepted");
  const pending = audit("legacy-pending");
  const repository = new FakeAuditRepository([first, pending]);
  let uploads = 0;
  const coordinator = new PosSyncCoordinator({
    outbox: new FakeOutbox([[]]),
    auditRepository: repository,
    orderSync: { async sync() { return { kind: "synced", alreadySynced: false }; } },
    auditUploader: {
      async upload() {
        uploads += 1;
        return {
          kind: "acknowledged",
          uploadedEventIds: [first.eventId],
          rejected: [{ eventId: pending.eventId, code: "LEGACY_INVALID" }],
          retryEventIds: [],
        };
      },
    },
    security: { async lockDevice() {} },
    now: () => new Date("2026-07-28T00:00:00.000Z"),
  });

  await coordinator.requestDrain();

  assert.equal(uploads, 1);
  assert.deepEqual(repository.uploaded, [first.eventId]);
});

test("员工审计网络失败使用分钟级 1/2/5/15/30 退避加抖动，并保留 durable Pending", async () => {
  const event = { ...audit("audit-retry"), attemptCount: 2 };
  const delivery = new FakeAuditDelivery([event]);
  const coordinator = new PosSyncCoordinator({
    outbox: new FakeOutbox([[]]),
    auditRepository: new FakeAuditRepository(),
    auditDelivery: delivery,
    orderSync: { async sync() { return { kind: "synced", alreadySynced: false }; } },
    auditUploader: { async upload() { return { kind: "retry", failure: "network" }; } },
    security: { async lockDevice() {} },
    now: () => new Date("2026-07-28T00:00:00.000Z"),
    random: () => 0.5,
  });

  await coordinator.requestDrain();

  assert.deepEqual(delivery.retries, [{
    eventIds: [event.eventId],
    nextAttemptAtIso: "2026-07-28T00:05:07.500Z",
    errorCode: "AUDIT_NETWORK",
  }]);
});

test("员工审计适配器意外抛错也会持久退避，并由定时器自动继续同步", async () => {
  let now = new Date("2026-07-28T00:00:00.000Z");
  const scheduled: { delayMs: number; callback: () => void }[] = [];
  const event = { ...audit("audit-adapter-throws"), attemptCount: 0 };
  const delivery = new FakeAuditDelivery([event]);
  let calls = 0;
  const coordinator = new PosSyncCoordinator({
    outbox: new FakeOutbox([[]]),
    auditRepository: new FakeAuditRepository(),
    auditDelivery: delivery,
    orderSync: { async sync() { return { kind: "synced", alreadySynced: false }; } },
    auditUploader: {
      async upload() {
        calls += 1;
        if (calls === 1) throw new Error("audit mapper read failed");
        return { kind: "uploaded" as const };
      },
    },
    security: { async lockDevice() {} },
    now: () => now,
    random: () => 0.5,
    timer: {
      set(delayMs, callback) {
        scheduled.push({ delayMs, callback });
        return scheduled.length;
      },
      clear() {},
    },
  });

  await assert.doesNotReject(coordinator.requestDrain());
  assert.deepEqual(delivery.retries, [{
    eventIds: [event.eventId],
    nextAttemptAtIso: "2026-07-28T00:01:07.500Z",
    errorCode: "AUDIT_UPLOAD_EXCEPTION",
  }]);
  assert.equal(scheduled[0]?.delayMs, 67_500);

  now = new Date("2026-07-28T00:01:07.500Z");
  delivery.makeRetriesReady();
  scheduled[0]?.callback();
  await waitUntil(() => calls === 2);
  assert.deepEqual(delivery.uploaded, [event.eventId]);
});

test("只有员工审计待重试时，协调器仍定时唤醒而无需前台或联网事件", async () => {
  let now = new Date("2026-07-28T00:00:00.000Z");
  const scheduled: { delayMs: number; callback: () => void }[] = [];
  const event = { ...audit("audit-timer"), attemptCount: 0 };
  const delivery = new FakeAuditDelivery([event]);
  let uploads = 0;
  const coordinator = new PosSyncCoordinator({
    outbox: new FakeOutbox([[]]),
    auditRepository: new FakeAuditRepository(),
    auditDelivery: delivery,
    orderSync: { async sync() { return { kind: "synced", alreadySynced: false }; } },
    auditUploader: {
      async upload() {
        uploads += 1;
        return uploads === 1
          ? { kind: "retry", failure: "network" as const }
          : { kind: "uploaded" as const };
      },
    },
    security: { async lockDevice() {} },
    now: () => now,
    random: () => 0.5,
    timer: {
      set(delayMs, callback) {
        scheduled.push({ delayMs, callback });
        return scheduled.length;
      },
      clear() {},
    },
  });

  await coordinator.requestDrain();
  assert.equal(uploads, 1);
  assert.equal(scheduled[0]?.delayMs, 67_500);

  now = new Date("2026-07-28T00:01:07.500Z");
  delivery.makeRetriesReady();
  scheduled[0]?.callback();
  await waitUntil(() => uploads === 2);
  assert.deepEqual(delivery.uploaded, [event.eventId]);
});

test("员工审计 blocked 锁机后至少延迟 30 分钟，不产生 0ms 定时自旋", async () => {
  const scheduled: { delayMs: number; callback: () => void }[] = [];
  const delivery = new FakeAuditDelivery([{ ...audit("audit-blocked"), attemptCount: 0 }]);
  const coordinator = new PosSyncCoordinator({
    outbox: new FakeOutbox([[]]),
    auditRepository: new FakeAuditRepository(),
    auditDelivery: delivery,
    orderSync: { async sync() { return { kind: "synced", alreadySynced: false }; } },
    auditUploader: { async upload() { return { kind: "blocked", code: "DEVICE_DISABLED" as const }; } },
    security: { async lockDevice() {} },
    now: () => new Date("2026-07-28T00:00:00.000Z"),
    timer: {
      set(delayMs, callback) {
        scheduled.push({ delayMs, callback });
        return scheduled.length;
      },
      clear() {},
    },
  });

  await coordinator.requestDrain();

  assert.deepEqual(delivery.retries, [{
    eventIds: ["audit-blocked"],
    nextAttemptAtIso: "2026-07-28T00:30:00.000Z",
    errorCode: "AUDIT_BLOCKED_DEVICE_DISABLED",
  }]);
  assert.equal(scheduled[0]?.delayMs, 1_800_000);
});

test("订单同步适配器意外抛错时保留 pending 并按退避重试", async () => {
  const outbox = new FakeOutbox([[lease("throws")]]);
  const coordinator = new PosSyncCoordinator({
    outbox,
    auditRepository: new FakeAuditRepository(),
    orderSync: { async sync() { throw new Error("socket closed"); } },
    auditUploader: { async upload() { return { kind: "uploaded" }; } },
    security: { async lockDevice() {} },
    now: () => new Date("2026-07-28T00:00:00.000Z"),
    random: () => 0.5,
  });

  await coordinator.requestDrain();

  assert.deepEqual(outbox.retries, [{ messageId: "throws", nextAttemptAtIso: "2026-07-28T00:00:01.000Z", errorCode: "SYNC_TRANSPORT_EXCEPTION" }]);
});

test("同步与审计 drain 从排队到完成期间公开只读 in-flight 状态", async () => {
  const outbox = new FakeOutbox([[lease("one")]]);
  let releaseSync: (() => void) | undefined;
  const coordinator = new PosSyncCoordinator({
    outbox,
    auditRepository: new FakeAuditRepository(),
    orderSync: {
      async sync() {
        await new Promise<void>((resolve) => {
          releaseSync = resolve;
        });
        return { kind: "synced", alreadySynced: false };
      },
    },
    auditUploader: { async upload() { return { kind: "uploaded" }; } },
    security: { async lockDevice() {} },
    now: () => new Date("2026-07-28T00:00:00.000Z"),
  });
  assert.equal(coordinator.isDraining(), false);
  const drain = coordinator.requestDrain();
  assert.equal(coordinator.isDraining(), true);
  await Promise.resolve();
  assert.equal(coordinator.isDraining(), true);

  releaseSync?.();
  await drain;
  assert.equal(coordinator.isDraining(), false);
});

test("更新 transition 等待在途同步，并拒绝 transition 期间的新 drain", async () => {
  const syncRelease = deferred<void>();
  const transitionRelease = deferred<void>();
  const transition = new UpdateTransitionLeaseCoordinator();
  transition.bindTransitionBarrier((operation) => operation());
  const coordinator = new PosSyncCoordinator({
    outbox: new FakeOutbox([[lease("one")], []]),
    auditRepository: new FakeAuditRepository(),
    orderSync: {
      async sync() {
        await syncRelease.promise;
        return { kind: "synced", alreadySynced: false };
      },
    },
    auditUploader: { async upload() { return { kind: "uploaded" }; } },
    security: { async lockDevice() {} },
    now: () => new Date("2026-07-28T00:00:00.000Z"),
    operationLease: transition,
  });

  const drain = coordinator.requestDrain();
  await Promise.resolve();
  let transitionStarted = false;
  const update = transition.runTransition(async () => {
    transitionStarted = true;
    await transitionRelease.promise;
  });
  await Promise.resolve();
  assert.equal(transitionStarted, false);

  syncRelease.resolve();
  await drain;
  await Promise.resolve();
  assert.equal(transitionStarted, true);
  await assert.rejects(
    coordinator.requestDrain(),
    (error: unknown) =>
      error instanceof Error &&
      (error as Error & { code?: string }).code ===
        UPDATE_TRANSITION_IN_PROGRESS,
  );

  transitionRelease.resolve();
  await update;
  await assert.doesNotReject(coordinator.requestDrain());
});

function deferred<T>(): Readonly<{
  promise: Promise<T>;
  resolve(value: T | PromiseLike<T>): void;
}> {
  let resolve!: (value: T | PromiseLike<T>) => void;
  const promise = new Promise<T>((accept) => {
    resolve = accept;
  });
  return { promise, resolve };
}

async function waitUntil(predicate: () => boolean): Promise<void> {
  for (let attempt = 0; attempt < 50; attempt += 1) {
    if (predicate()) return;
    await new Promise<void>((resolve) => setImmediate(resolve));
  }
  throw new Error("Timed out waiting for sync test condition.");
}
