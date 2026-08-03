import type { UpdateOperationLeasePort } from "../../features/app-updates/update-transition-lease-coordinator";
import type { AuditEventDraft } from "../contracts/order";
import type {
  AuditRepositoryPort,
  OperationAuditDeliveryPort,
  OutboxLease,
  OutboxRepositoryPort,
} from "../contracts/repositories";
import type { OrderSyncPort, SyncOrderResult } from "../contracts/sync";

export interface SyncSecurityPort {
  lockDevice(reason: string): Promise<void>;
}

export type AuditUploadResult =
  | Readonly<{ kind: "uploaded" }>
  | Readonly<{
      /** 后端针对同一批的逐项终态回执；rejected 不得阻塞其他事件。 */
      kind: "acknowledged";
      uploadedEventIds: readonly string[];
      rejected: readonly Readonly<{ eventId: string; code: string }>[];
      /** 缺失/未知回执仅重试相应事件，不能撤回已经确认的终态。 */
      retryEventIds?: readonly string[];
    }>
  | Readonly<{ kind: "retry"; failure: "network" | "server" | "unauthorized" }>
  | Readonly<{ kind: "blocked"; code: string }>
  | Readonly<{ kind: "rejected"; code: string }>;

/**
 * 审计远端上传契约尚未冻结在 core/contracts；暂由 sync 组合根注入。
 */
export interface AuditBatchUploadPort {
  upload(events: readonly AuditEventDraft[]): Promise<AuditUploadResult>;
  uploadOutbox?(payloadJson: string): Promise<AuditUploadResult>;
}

export interface SyncRetryTimerPort {
  set(delayMs: number, callback: () => void): unknown;
  clear(handle: unknown): void;
}

export type SyncDrainReport = Readonly<{
  leased: number;
  orderSucceeded: number;
  orderRetried: number;
  orderBlocked: number;
  orderRejected: number;
  auditUploaded: number;
}>;

type MutableSyncDrainReport = {
  leased: number;
  orderSucceeded: number;
  orderRetried: number;
  orderBlocked: number;
  orderRejected: number;
  auditUploaded: number;
};

export type PosSyncCoordinatorOptions = Readonly<{
  outbox: OutboxRepositoryPort;
  auditRepository: AuditRepositoryPort;
  /** 新版 iPad 使用独立的投递状态表；旧测试/适配器仍可只注入 auditRepository。 */
  auditDelivery?: OperationAuditDeliveryPort;
  orderSync: OrderSyncPort;
  auditUploader: AuditBatchUploadPort;
  security: SyncSecurityPort;
  now: () => Date;
  random?: () => number;
  outboxBatchSize?: number;
  leaseSeconds?: number;
  retryBaseMs?: number;
  retryMaxMs?: number;
  retryJitterRatio?: number;
  operationLease?: UpdateOperationLeasePort;
  timer?: SyncRetryTimerPort;
}>;

const auditBatchSize = 8;
const maxTimerDelayMs = 2_147_483_647;
const defaultTimer: SyncRetryTimerPort = {
  set: (delayMs, callback) => {
    const handle = setTimeout(callback, delayMs);
    // Node 测试环境的长退避不应仅因计时器而挂住进程；React Native 返回 number，不受影响。
    if (
      typeof handle === "object" &&
      handle !== null &&
      "unref" in handle &&
      typeof (handle as { unref?: unknown }).unref === "function"
    ) {
      (handle as { unref(): void }).unref();
    }
    return handle;
  },
  clear: (handle) =>
    clearTimeout(handle as ReturnType<typeof setTimeout>),
};
const emptyReport = (): MutableSyncDrainReport => ({
  leased: 0,
  orderSucceeded: 0,
  orderRetried: 0,
  orderBlocked: 0,
  orderRejected: 0,
  auditUploaded: 0,
});

/** 单实例同步协调器：所有触发源共用同一 Promise，避免同一 OrderGuid 并发补传。 */
export class PosSyncCoordinator {
  private readonly random: () => number;
  private readonly outboxBatchSize: number;
  private readonly leaseSeconds: number;
  private readonly retryBaseMs: number;
  private readonly retryMaxMs: number;
  private readonly retryJitterRatio: number;
  private readonly timer: SyncRetryTimerPort;
  private inFlight: Promise<SyncDrainReport> | undefined;
  private rerunRequested = false;
  private scheduledDrain: unknown | undefined;
  private stopped = false;
  private shutdownPromise: Promise<void> | undefined;

  public constructor(private readonly options: PosSyncCoordinatorOptions) {
    this.random = options.random ?? Math.random;
    this.outboxBatchSize = options.outboxBatchSize ?? 25;
    this.leaseSeconds = options.leaseSeconds ?? 60;
    this.retryBaseMs = options.retryBaseMs ?? 1_000;
    this.retryMaxMs = options.retryMaxMs ?? 5 * 60_000;
    this.retryJitterRatio = options.retryJitterRatio ?? 0.2;
    this.timer = options.timer ?? defaultTimer;
  }

  /** 仅暴露当前单飞 drain 是否仍在执行，不泄漏队列内容或改变重试状态。 */
  public isDraining(): boolean {
    return this.inFlight !== undefined;
  }

  /** runtime 关闭前释放延迟重试，避免旧数据库连接被 timer 在关闭后再次访问。 */
  public shutdown(): Promise<void> {
    if (this.shutdownPromise) return this.shutdownPromise;
    this.stopped = true;
    this.rerunRequested = false;
    if (this.scheduledDrain !== undefined) {
      this.timer.clear(this.scheduledDrain);
      this.scheduledDrain = undefined;
    }
    // 停止后不再租用新项目；已在网络中的当前项目仍要完成对应终态写入，
    // 否则 database.close 会把订单/审计留在半处理状态。
    this.shutdownPromise = (this.inFlight ?? Promise.resolve())
      .catch(() => undefined)
      .then(() => undefined);
    return this.shutdownPromise;
  }

  public requestDrain(): Promise<SyncDrainReport> {
    if (this.stopped) return Promise.resolve(emptyReport());
    if (this.inFlight) {
      // 中文注释：提交可能刚好发生在旧 drain 读到空队列之后；锁存第二轮，
      // 避免新 outbox 只“加入旧 Promise”却没有真正再扫描。
      this.rerunRequested = true;
      return this.inFlight;
    }
    const drain = () => this.drainUntilQuiescent();
    this.inFlight = (
      this.options.operationLease
        ? this.options.operationLease.runOperation(drain)
        : drain()
    ).finally(() => {
      this.inFlight = undefined;
    });
    return this.inFlight;
  }

  private async drainUntilQuiescent(): Promise<SyncDrainReport> {
    const report = emptyReport();
    do {
      this.rerunRequested = false;
      await this.drainInternal(report);
      // 中文注释：计算下一次定时唤醒本身也会跨越异步数据库读取；
      // 该窗口内的新提交必须参与同一重跑判断，不能在返回前被吞掉。
      await this.scheduleNextReadyDrain();
    } while (!this.stopped && this.rerunRequested);
    return report;
  }

  private async drainInternal(report: MutableSyncDrainReport): Promise<void> {
    while (true) {
      if (this.stopped) return;
      const leases = await this.options.outbox.leaseReady(
        this.outboxBatchSize,
        this.leaseSeconds,
      );
      if (!leases.length) break;
      report.leased += leases.length;
      for (let index = 0; index < leases.length; index += 1) {
        const item = leases[index]!;
        // 已取到的后续租约由下次 runtime 恢复处理；只让正在执行的一项完成终态。
        if (this.stopped) {
          await this.releaseUnstartedLeases(leases.slice(index));
          return;
        }
        await this.syncOutboxItem(item, report);
      }
    }
    if (this.stopped) return;
    await this.syncPendingAudits(report);
  }

  private async syncOutboxItem(item: OutboxLease, report: {
    orderSucceeded: number;
    orderRetried: number;
    orderBlocked: number;
    orderRejected: number;
  }): Promise<void> {
    if (item.kind === "audit-batch") {
      await this.syncAuditOutboxItem(item, report);
      return;
    }
    try {
      const result = await this.options.orderSync.sync(item.aggregateId, item.payloadJson);
      await this.applyOrderResult(item, result, report);
    } catch {
      // 适配器意外抛错时宁可保留 Pending 并退避，也不能丢失本地现金订单。
      await this.retry(item, "SYNC_TRANSPORT_EXCEPTION");
      report.orderRetried += 1;
    }
  }

  private async syncAuditOutboxItem(item: OutboxLease, report: {
    orderSucceeded: number;
    orderRetried: number;
    orderBlocked: number;
    orderRejected: number;
  }): Promise<void> {
    if (!this.options.auditUploader.uploadOutbox) {
      await this.retry(item, "SYNC_AUDIT_OUTBOX_UNSUPPORTED");
      report.orderRetried += 1;
      return;
    }
    const result = await this.options.auditUploader.uploadOutbox(item.payloadJson);
    if (result.kind === "uploaded") {
      await this.options.outbox.markSucceeded(item);
      report.orderSucceeded += 1;
      return;
    }
    if (result.kind === "blocked") {
      await this.options.security.lockDevice(result.code);
    }
    // 审计批次的失败没有对应持久化分类 Port；一律保留 pending，403 额外锁机。
    const errorCode = result.kind === "retry"
      ? `SYNC_AUDIT_${result.failure.toUpperCase()}`
      : result.kind === "acknowledged"
        ? "SYNC_AUDIT_PARTIAL_ACK"
        : `SYNC_AUDIT_${result.code}`;
    await this.retry(item, errorCode);
    report.orderRetried += 1;
  }

  private async applyOrderResult(item: OutboxLease, result: SyncOrderResult, report: {
    orderSucceeded: number;
    orderRetried: number;
    orderBlocked: number;
    orderRejected: number;
  }): Promise<void> {
    if (result.kind === "synced") {
      // 后端 AlreadySynced 表明同一 OrderGuid 已完成；本地应视作成功而不是重试。
      await this.options.outbox.markSucceeded(item);
      report.orderSucceeded += 1;
      return;
    }
    if (result.kind === "retry") {
      await this.retry(item, `SYNC_${result.failure.toUpperCase()}`);
      report.orderRetried += 1;
      return;
    }
    if (result.kind === "blocked") {
      await this.options.outbox.markBlocked403(item, result.code);
      await this.options.security.lockDevice(result.code);
      report.orderBlocked += 1;
      return;
    }
    await this.options.outbox.markRejected(item, result.code);
    report.orderRejected += 1;
  }

  private async syncPendingAudits(report: { auditUploaded: number }): Promise<void> {
    while (true) {
      if (this.stopped) return;
      const events = this.options.auditDelivery
        ? await this.options.auditDelivery.listReady(auditBatchSize)
        : await this.options.auditRepository.listPending(auditBatchSize);
      if (this.stopped) return;
      if (!events.length) {
        return;
      }
      let result: AuditUploadResult;
      try {
        result = await this.options.auditUploader.upload(events);
      } catch {
        if (this.options.auditDelivery) {
          // 映射订单或 adapter 意外抛错时，已选批次必须耐久退避，等待后续生命周期触发重试。
          await this.options.auditDelivery.releaseRetry(
            events.map((event) => event.eventId),
            this.nextAuditRetryAt(events),
            "AUDIT_UPLOAD_EXCEPTION",
          );
        }
        return;
      }
      if (result.kind === "uploaded") {
        await this.markAuditsUploaded(events.map((event) => event.eventId));
        report.auditUploaded += events.length;
        if (this.stopped) return;
        continue;
      }
      if (result.kind === "acknowledged") {
        await this.markAuditsUploaded(result.uploadedEventIds);
        if (this.options.auditDelivery) {
          await this.options.auditDelivery.markRejected(result.rejected);
          const retryIds = new Set(result.retryEventIds ?? []);
          const retryEvents = events.filter((event) => retryIds.has(event.eventId));
          if (retryEvents.length) {
            await this.options.auditDelivery.releaseRetry(
              retryEvents.map((event) => event.eventId),
              this.nextAuditRetryAt(retryEvents),
              "AUDIT_SERVER",
            );
          }
        } else {
          // 旧 AuditRepositoryPort 无法持久化 rejected/retry 分类；本轮到此为止，
          // 不能把未确认事件立即重新读出形成无穷循环。
          report.auditUploaded += result.uploadedEventIds.length;
          return;
        }
        report.auditUploaded += result.uploadedEventIds.length;
        if (this.stopped) return;
        // 就算本批只有 rejected，也要继续读取后续 ready 事件，避免坏记录堵住队头。
        continue;
      }
      if (result.kind === "blocked") {
        await this.options.security.lockDevice(result.code);
        if (this.options.auditDelivery) {
          // 锁机审计保留但不维持到期 pending，防止定时器 0ms 自旋。
          await this.options.auditDelivery.releaseRetry(
            events.map((event) => event.eventId),
            this.nextAuditBlockedRetryAt(),
            `AUDIT_BLOCKED_${result.code}`,
          );
        }
        return;
      }
      if (result.kind === "retry" && this.options.auditDelivery) {
        await this.options.auditDelivery.releaseRetry(
          events.map((event) => event.eventId),
          this.nextAuditRetryAt(events),
          `AUDIT_${result.failure.toUpperCase()}`,
        );
      } else if (result.kind === "rejected" && this.options.auditDelivery) {
        await this.options.auditDelivery.markRejected(
          events.map((event) => ({ eventId: event.eventId, code: result.code })),
        );
      }
      return;
    }
  }

  private markAuditsUploaded(eventIds: readonly string[]): Promise<void> {
    if (!eventIds.length) return Promise.resolve();
    return this.options.auditDelivery
      ? this.options.auditDelivery.markUploaded(eventIds)
      : this.options.auditRepository.markUploaded(eventIds);
  }

  private nextAuditRetryAt(
    events: readonly (AuditEventDraft & Readonly<{ attemptCount?: number }>)[],
  ): string {
    const attempt = Math.max(0, ...events.map((event) => event.attemptCount ?? 0));
    const delayMs = [60_000, 120_000, 300_000, 900_000, 1_800_000][
      Math.min(attempt, 4)
    ]!;
    // 短抖动避免多台离线 iPad 在同一秒集中重连。
    const jitterMs = Math.round(this.random() * 15_000);
    return new Date(this.options.now().getTime() + delayMs + jitterMs).toISOString();
  }

  private nextAuditBlockedRetryAt(): string {
    return new Date(this.options.now().getTime() + 30 * 60_000).toISOString();
  }

  private async retry(item: OutboxLease, errorCode: string): Promise<void> {
    await this.options.outbox.releaseRetry(item, this.nextRetryAt(item.attemptCount), errorCode);
  }

  private async releaseUnstartedLeases(
    leases: readonly OutboxLease[],
  ): Promise<void> {
    for (const lease of leases) {
      // 关闭期间未开始的 lease 必须立即归还；否则订单保持 Syncing 直到原 lease 超时。
      await this.options.outbox.releaseRetry(
        lease,
        this.options.now().toISOString(),
        "SYNC_RUNTIME_SHUTDOWN",
      );
    }
  }

  private nextRetryAt(attemptCount: number): string {
    const exponent = Math.min(Math.max(attemptCount, 0), 12);
    const baseDelay = Math.min(this.retryMaxMs, this.retryBaseMs * 2 ** exponent);
    const jitter = (this.random() * 2 - 1) * this.retryJitterRatio;
    const delay = Math.max(0, Math.round(baseDelay * (1 + jitter)));
    return new Date(this.options.now().getTime() + delay).toISOString();
  }

  private async scheduleNextReadyDrain(): Promise<void> {
    if (this.stopped) return;
    const candidates = await Promise.all([
      this.options.outbox.nextReadyAtIso?.(),
      this.options.auditDelivery?.nextReadyAtIso(),
    ]);
    if (this.stopped) return;
    const nextReadyAt = candidates
      .filter((value): value is string =>
        typeof value === "string" && Number.isFinite(Date.parse(value)))
      .sort((left, right) => Date.parse(left) - Date.parse(right))[0];
    if (this.scheduledDrain !== undefined) {
      this.timer.clear(this.scheduledDrain);
      this.scheduledDrain = undefined;
    }
    if (!nextReadyAt) return;
    const readyAtMs = Date.parse(nextReadyAt);
    if (!Number.isFinite(readyAtMs)) return;
    const delayMs = Math.min(
      maxTimerDelayMs,
      Math.max(0, readyAtMs - this.options.now().getTime()),
    );
    this.scheduledDrain = this.timer.set(delayMs, () => {
      this.scheduledDrain = undefined;
      if (this.stopped) return;
      void this.requestDrain().catch(() => undefined);
    });
  }
}

/** 启动、回到前台和联网恢复都走同一单飞入口。 */
export class SyncLifecycleController {
  public constructor(private readonly coordinator: PosSyncCoordinator) {}

  public onApplicationStarted(): Promise<SyncDrainReport> {
    return this.coordinator.requestDrain();
  }

  public onForeground(): Promise<SyncDrainReport> {
    return this.coordinator.requestDrain();
  }

  public onNetworkChanged(isOnline: boolean): Promise<SyncDrainReport> {
    return isOnline ? this.coordinator.requestDrain() : Promise.resolve(emptyReport());
  }

  public shutdown(): Promise<void> {
    return this.coordinator.shutdown();
  }
}
