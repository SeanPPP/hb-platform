import type { AuditEventDraft } from "../contracts/order";
import type { AuditRepositoryPort, OutboxLease, OutboxRepositoryPort } from "../contracts/repositories";
import type { OrderSyncPort, SyncOrderResult } from "../contracts/sync";

export interface SyncSecurityPort {
  lockDevice(reason: string): Promise<void>;
}

export type AuditUploadResult =
  | Readonly<{ kind: "uploaded" }>
  | Readonly<{ kind: "retry"; failure: "network" | "server" | "unauthorized" }>
  | Readonly<{ kind: "blocked"; code: string }>
  | Readonly<{ kind: "rejected"; code: string }>;

/**
 * 审计远端上传契约尚未冻结在 core/contracts；暂由 sync 组合根注入。
 * AuditRepositoryPort 没有 blocked/rejected 的持久化状态，非成功结果必须保留未上传记录。
 */
export interface AuditBatchUploadPort {
  upload(events: readonly AuditEventDraft[]): Promise<AuditUploadResult>;
  uploadOutbox?(payloadJson: string): Promise<AuditUploadResult>;
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
}>;

const auditBatchSize = 100;
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
  private inFlight: Promise<SyncDrainReport> | undefined;

  public constructor(private readonly options: PosSyncCoordinatorOptions) {
    this.random = options.random ?? Math.random;
    this.outboxBatchSize = options.outboxBatchSize ?? 25;
    this.leaseSeconds = options.leaseSeconds ?? 60;
    this.retryBaseMs = options.retryBaseMs ?? 1_000;
    this.retryMaxMs = options.retryMaxMs ?? 5 * 60_000;
    this.retryJitterRatio = options.retryJitterRatio ?? 0.2;
  }

  public requestDrain(): Promise<SyncDrainReport> {
    if (this.inFlight) {
      return this.inFlight;
    }
    this.inFlight = this.drainInternal().finally(() => {
      this.inFlight = undefined;
    });
    return this.inFlight;
  }

  private async drainInternal(): Promise<SyncDrainReport> {
    const report = emptyReport();
    const leases = await this.options.outbox.leaseReady(this.outboxBatchSize, this.leaseSeconds);
    report.leased = leases.length;

    for (const item of leases) {
      await this.syncOutboxItem(item, report);
    }
    await this.syncPendingAudits(report);
    return report;
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
      const events = await this.options.auditRepository.listPending(auditBatchSize);
      if (!events.length) {
        return;
      }
      const result = await this.options.auditUploader.upload(events);
      if (result.kind === "uploaded") {
        await this.options.auditRepository.markUploaded(events.map((event) => event.eventId));
        report.auditUploaded += events.length;
        continue;
      }
      if (result.kind === "blocked") {
        await this.options.security.lockDevice(result.code);
      }
      // AuditRepositoryPort 尚无失败状态列；network/5xx/401/403/业务拒绝均保持未上传。
      return;
    }
  }

  private async retry(item: OutboxLease, errorCode: string): Promise<void> {
    await this.options.outbox.releaseRetry(item, this.nextRetryAt(item.attemptCount), errorCode);
  }

  private nextRetryAt(attemptCount: number): string {
    const exponent = Math.min(Math.max(attemptCount, 0), 12);
    const baseDelay = Math.min(this.retryMaxMs, this.retryBaseMs * 2 ** exponent);
    const jitter = (this.random() * 2 - 1) * this.retryJitterRatio;
    const delay = Math.max(0, Math.round(baseDelay * (1 + jitter)));
    return new Date(this.options.now().getTime() + delay).toISOString();
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
}
