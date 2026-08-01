import assert from "node:assert/strict";
import test from "node:test";

import {
  UPDATE_TRANSITION_IN_PROGRESS,
  UpdateTransitionLeaseCoordinator,
} from "../../features/app-updates/update-transition-lease-coordinator";
import type { AuditEventDraft } from "../contracts/order";
import type { AuditRepositoryPort, OutboxLease, OutboxRepositoryPort } from "../contracts/repositories";
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

  public constructor(private readonly batches: readonly (readonly OutboxLease[])[]) {}

  public async enqueue(): Promise<void> {}

  public async leaseReady(): Promise<readonly OutboxLease[]> {
    return this.batches[this.leaseCalls++] ?? [];
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

  assert.equal(outbox.leaseCalls, 1);
  assert.deepEqual(outbox.succeeded, ["one"]);
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
  assert.doesNotReject(coordinator.requestDrain());
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
