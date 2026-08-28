import type { SqliteConnectionPort } from "@hb/pos-db/core/db/types";
import { sanitizeText } from "../logging/application-log";

export const POS_CLIENT_METRICS = Object.freeze({
  coldStart: "pos.cold_start.duration",
  scanToCart: "pos.scan_to_cart.duration",
  paymentResponse: "pos.payment_response.duration",
} as const);

export type PosClientMetricName =
  (typeof POS_CLIENT_METRICS)[keyof typeof POS_CLIENT_METRICS];
export type ClientMetricBaselineState =
  | "not_started"
  | "observing"
  | "frozen";
export type ClientMetricOutcome =
  | "success"
  | "failure"
  | "rejected"
  | "timeout";
export type ClientMetricDimensionKey =
  | "app"
  | "version"
  | "channel"
  | "store"
  | "environment"
  | "paymentType"
  | "outcome";
export type ClientMetricEnvironment = "Production" | "Development" | "Preview";
export type ClientMetricDimensions = Readonly<
  Partial<Record<ClientMetricDimensionKey, string>>
>;

export type MetricEventV1 = Readonly<{
  eventId: string;
  metric: PosClientMetricName;
  observedAt: string;
  value: number;
  unit: "ms";
  dimensions: ClientMetricDimensions;
}>;

export type MetricBatchV1 = Readonly<{
  schemaVersion: 1;
  events: readonly MetricEventV1[];
}>;

export type ClientMetricDraft = Readonly<{
  metric: PosClientMetricName;
  valueMs: number;
  dimensions?: Readonly<Record<string, string | null | undefined>>;
}>;

export type ClientMetricContext = Readonly<{
  app: string;
  version: string;
  channel: string;
  store: string | null;
  environment: string | null | undefined;
}>;

export type ClientMetricDelivery = Readonly<{
  event: MetricEventV1;
  attemptCount: number;
}>;

export type ClientMetricSamplingRule = Readonly<{
  metric: PosClientMetricName;
  selector: string;
  sampleRate: number;
  slowThreshold: number | null;
}>;

export type ClientMetricSamplingPolicy = Readonly<{
  baselineState: ClientMetricBaselineState;
  defaultSampleRate: number;
  policies: readonly ClientMetricSamplingRule[];
}>;

export interface ClientMetricSamplingPolicyStorePort {
  read(): Promise<ClientMetricSamplingPolicy>;
  save(policy: ClientMetricSamplingPolicy): Promise<void>;
}

export interface ClientMetricOutboxPort {
  enqueue(event: MetricEventV1): Promise<void>;
  listReady(limit: number): Promise<readonly ClientMetricDelivery[]>;
  markAccepted(eventIds: readonly string[]): Promise<void>;
  markRejected(eventIds: readonly string[]): Promise<void>;
  releaseRetry(
    eventIds: readonly string[],
    nextAttemptAtIso: string,
    errorCode: string,
  ): Promise<void>;
}

export type ClientMetricUploadConfig = Readonly<{
  enabled: boolean;
  endpointUrl: string | null;
  writeKey: string | null;
  projectCode: string;
}>;

export type ClientMetricUploadReport = Readonly<{
  uploaded: number;
  rejected: number;
  retried: number;
}>;

const CLIENT_DIMENSION_KEYS = new Set<ClientMetricDimensionKey>([
  "app",
  "version",
  "channel",
  "store",
  "environment",
  "paymentType",
  "outcome",
]);
const POS_CLIENT_METRIC_NAMES = new Set<PosClientMetricName>(
  Object.values(POS_CLIENT_METRICS),
);
const UUID_PATTERN =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/iu;
const MAX_OUTBOX_EVENTS = 10_000;
const MAX_BATCH_EVENTS = 200;
const MAX_SAMPLING_POLICIES = 100;
const MAX_EVENT_AGE_MS = 30 * 24 * 60 * 60 * 1_000;
const MAX_FUTURE_CLOCK_SKEW_MS = 5 * 60 * 1_000;
const RUNTIME_FLUSH_DEBOUNCE_MS = 250;

export const DEFAULT_CLIENT_METRIC_SAMPLING_POLICY: ClientMetricSamplingPolicy =
  Object.freeze({
    baselineState: "not_started",
    defaultSampleRate: 1,
    policies: Object.freeze([]),
  });

export function normalizeClientMetricEnvironment(
  value: unknown,
): ClientMetricEnvironment | null {
  if (typeof value !== "string") return null;
  switch (value.trim().toLowerCase()) {
    case "production":
      return "Production";
    case "development":
      return "Development";
    case "preview":
      return "Preview";
    default:
      return null;
  }
}

export function buildMetricEventV1(input: Readonly<{
  eventId: string;
  metric: PosClientMetricName;
  observedAt: string;
  valueMs: number;
  dimensions: Readonly<Record<string, string | null | undefined>>;
}>): MetricEventV1 {
  if (!UUID_PATTERN.test(input.eventId)) {
    throw new Error("客户端指标 eventId 无效");
  }
  if (!POS_CLIENT_METRIC_NAMES.has(input.metric)) {
    throw new Error("客户端指标名称不在白名单");
  }
  if (!Number.isFinite(input.valueMs) || input.valueMs < 0) {
    throw new Error("客户端指标 value 必须是非负有限数值");
  }
  const observedAt = new Date(input.observedAt);
  if (
    !Number.isFinite(observedAt.getTime()) ||
    observedAt.toISOString() !== input.observedAt
  ) {
    throw new Error("客户端指标 observedAt 必须是 canonical UTC");
  }

  const dimensions: Partial<Record<ClientMetricDimensionKey, string>> = {};
  for (const [key, rawValue] of Object.entries(input.dimensions)) {
    if (!CLIENT_DIMENSION_KEYS.has(key as ClientMetricDimensionKey)) {
      throw new Error(
        `客户端指标维度 ${safeDimensionKey(key)} 不在白名单`,
      );
    }
    if (rawValue === null || rawValue === undefined) continue;
    const value = rawValue.trim();
    if (!value) continue;
    const sanitized = sanitizeText(value, 120);
    // 指标维度是低基数 selector；任何清洗变化都说明输入不适合成为维度。
    if (sanitized !== value || sanitized.includes("[REDACTED")) {
      throw new Error(
        `客户端指标维度 ${safeDimensionKey(key)} 包含不可上报内容`,
      );
    }
    if (key === "environment") {
      const environment = normalizeClientMetricEnvironment(sanitized);
      if (!environment) {
        throw new Error("客户端指标 environment 无效");
      }
      dimensions.environment = environment;
      continue;
    }
    dimensions[key as ClientMetricDimensionKey] = sanitized;
  }

  // 环境是服务端聚合的必填维度；缺失时不得默认写入 Production。
  if (!dimensions.environment) {
    throw new Error("客户端指标 environment 缺失");
  }

  return Object.freeze({
    eventId: input.eventId,
    metric: input.metric,
    observedAt: input.observedAt,
    value: Math.round(input.valueMs * 1_000) / 1_000,
    unit: "ms",
    dimensions: Object.freeze(dimensions),
  });
}

export class ClientMetricSampler {
  private readonly stableSessionUnit: number;

  public constructor(
    private readonly options: Readonly<{
      policyState: ClientMetricSamplingPolicyState;
      sessionId: string;
      stableSessionUnit?: number;
    }>,
  ) {
    const unit =
      options.stableSessionUnit ?? stableUnitInterval(options.sessionId);
    if (!Number.isFinite(unit) || unit < 0 || unit >= 1) {
      throw new Error("客户端指标 session 采样值无效");
    }
    this.stableSessionUnit = unit;
  }

  public shouldKeep(event: MetricEventV1): boolean {
    const policy = this.options.policyState.read();
    if (policy.baselineState !== "frozen") return true;
    if (event.dimensions.outcome !== "success") return true;
    const selector = metricSelector(event);
    const rule = policy.policies.find(
      (item) => item.metric === event.metric && item.selector === selector,
    );
    // 服务端只返回本批 selector 的策略；尚未完成一次策略握手的 metric 先全量。
    if (!rule) return true;
    if (
      rule?.slowThreshold !== null &&
      rule?.slowThreshold !== undefined &&
      event.value > rule.slowThreshold
    ) {
      return true;
    }
    return this.stableSessionUnit < rule.sampleRate;
  }
}

export class ClientMetricSamplingPolicyState {
  private policy: ClientMetricSamplingPolicy;

  public constructor(
    initial: ClientMetricSamplingPolicy =
      DEFAULT_CLIENT_METRIC_SAMPLING_POLICY,
  ) {
    this.policy = requireSamplingPolicy(initial);
  }

  public read(): ClientMetricSamplingPolicy {
    return this.policy;
  }

  public replace(policy: ClientMetricSamplingPolicy): void {
    this.policy = requireSamplingPolicy(policy);
  }
}

export class ClientMetricRecorder {
  public constructor(
    private readonly dependencies: Readonly<{
      outbox: ClientMetricOutboxPort;
      sampler: ClientMetricSampler;
      context: ClientMetricContext;
      createId(): string;
      nowIso(): string;
    }>,
  ) {}

  public async record(
    draft: ClientMetricDraft,
  ): Promise<"queued" | "sampled-out"> {
    const event = buildMetricEventV1({
      eventId: this.dependencies.createId(),
      metric: draft.metric,
      observedAt: this.dependencies.nowIso(),
      valueMs: draft.valueMs,
      dimensions: {
        ...draft.dimensions,
        app: this.dependencies.context.app,
        version: this.dependencies.context.version,
        channel: this.dependencies.context.channel,
        store: this.dependencies.context.store,
        // 运行时配置是唯一环境来源，业务指标草稿不得覆盖它。
        environment: this.dependencies.context.environment,
      },
    });
    if (!this.dependencies.sampler.shouldKeep(event)) return "sampled-out";
    await this.dependencies.outbox.enqueue(event);
    return "queued";
  }
}

export async function initializeClientMetricOutbox(
  db: SqliteConnectionPort,
): Promise<void> {
  await db.exec(`
    PRAGMA journal_mode = WAL;
    PRAGMA busy_timeout = 5000;
    CREATE TABLE IF NOT EXISTS client_metric_outbox (
      event_id TEXT PRIMARY KEY NOT NULL,
      observed_at_iso TEXT NOT NULL,
      payload_json TEXT NOT NULL,
      attempt_count INTEGER NOT NULL DEFAULT 0,
      next_attempt_at_iso TEXT NOT NULL,
      last_error_code TEXT NULL,
      created_at_iso TEXT NOT NULL
    );
    CREATE INDEX IF NOT EXISTS ix_client_metric_outbox_ready
      ON client_metric_outbox(next_attempt_at_iso, observed_at_iso);
    CREATE TABLE IF NOT EXISTS client_metric_sampling_policy (
      singleton_id INTEGER PRIMARY KEY NOT NULL CHECK (singleton_id = 1),
      payload_json TEXT NOT NULL,
      updated_at_iso TEXT NOT NULL
    );
  `);
}

export class SqliteClientMetricSamplingPolicyStore
  implements ClientMetricSamplingPolicyStorePort
{
  public constructor(
    private readonly db: SqliteConnectionPort,
    private readonly nowIso: () => string,
  ) {}

  public async read(): Promise<ClientMetricSamplingPolicy> {
    const row = await this.db.getFirst<{ payload_json: string }>(
      `SELECT payload_json
       FROM client_metric_sampling_policy
       WHERE singleton_id = 1`,
    );
    if (!row) return DEFAULT_CLIENT_METRIC_SAMPLING_POLICY;
    try {
      return normalizeSamplingPolicy(JSON.parse(row.payload_json)) ??
        DEFAULT_CLIENT_METRIC_SAMPLING_POLICY;
    } catch {
      return DEFAULT_CLIENT_METRIC_SAMPLING_POLICY;
    }
  }

  public async save(policy: ClientMetricSamplingPolicy): Promise<void> {
    const normalized = requireSamplingPolicy(policy);
    await this.db.run(
      `INSERT INTO client_metric_sampling_policy (
        singleton_id, payload_json, updated_at_iso
      ) VALUES (1, ?, ?)
      ON CONFLICT(singleton_id) DO UPDATE SET
        payload_json = excluded.payload_json,
        updated_at_iso = excluded.updated_at_iso`,
      [JSON.stringify(normalized), this.nowIso()],
    );
  }
}

export class SqliteClientMetricOutbox implements ClientMetricOutboxPort {
  public constructor(
    private readonly db: SqliteConnectionPort,
    private readonly nowIso: () => string,
  ) {}

  public async enqueue(event: MetricEventV1): Promise<void> {
    await this.db.withExclusiveTransaction(async (transaction) => {
      await transaction.run(
        `INSERT OR IGNORE INTO client_metric_outbox (
          event_id, observed_at_iso, payload_json, attempt_count,
          next_attempt_at_iso, last_error_code, created_at_iso
        ) VALUES (?, ?, ?, 0, ?, NULL, ?)`,
        [
          event.eventId,
          event.observedAt,
          JSON.stringify(event),
          event.observedAt,
          this.nowIso(),
        ],
      );
      // 性能样本只保留有界离线窗口，不能挤占 POS 业务数据库空间。
      await transaction.run(
        `DELETE FROM client_metric_outbox
         WHERE event_id IN (
           SELECT event_id
           FROM client_metric_outbox
           ORDER BY observed_at_iso DESC
           LIMIT -1 OFFSET ${MAX_OUTBOX_EVENTS}
         )`,
      );
    });
  }

  public async listReady(
    limit: number,
  ): Promise<readonly ClientMetricDelivery[]> {
    const safeLimit = Math.max(1, Math.min(MAX_BATCH_EVENTS, Math.floor(limit)));
    const rows = await this.db.getAll<{
      event_id: string;
      payload_json: string;
      attempt_count: number;
    }>(
      `SELECT event_id, payload_json, attempt_count
       FROM client_metric_outbox
       WHERE next_attempt_at_iso <= ?
       ORDER BY observed_at_iso, event_id
       LIMIT ?`,
      [this.nowIso(), safeLimit],
    );
    const deliveries: ClientMetricDelivery[] = [];
    const corrupted: string[] = [];
    for (const row of rows) {
      const event = parseMetricEvent(row.payload_json);
      if (!event || event.eventId !== row.event_id) {
        corrupted.push(row.event_id);
        continue;
      }
      deliveries.push({
        event,
        attemptCount: Math.max(0, Math.floor(row.attempt_count)),
      });
    }
    if (corrupted.length > 0) await this.markRejected(corrupted);
    return deliveries;
  }

  public async markAccepted(eventIds: readonly string[]): Promise<void> {
    await this.deleteEvents(eventIds);
  }

  public async markRejected(eventIds: readonly string[]): Promise<void> {
    await this.deleteEvents(eventIds);
  }

  public async releaseRetry(
    eventIds: readonly string[],
    nextAttemptAtIso: string,
    errorCode: string,
  ): Promise<void> {
    await Promise.all(
      eventIds.map((eventId) =>
        this.db.run(
          `UPDATE client_metric_outbox
           SET attempt_count = attempt_count + 1,
               next_attempt_at_iso = ?,
               last_error_code = ?
           WHERE event_id = ?`,
          [nextAttemptAtIso, errorCode, eventId],
        ),
      ),
    );
  }

  private async deleteEvents(eventIds: readonly string[]): Promise<void> {
    await Promise.all(
      eventIds.map((eventId) =>
        this.db.run(
          "DELETE FROM client_metric_outbox WHERE event_id = ?",
          [eventId],
        ),
      ),
    );
  }
}

export function resolveClientMetricUploadConfig(
  input: Readonly<{
    enabled: boolean | undefined;
    logIngestUrl: string | null | undefined;
    writeKey: string | null | undefined;
    projectCode: string;
    environment: string | null | undefined;
  }>,
): ClientMetricUploadConfig {
  const writeKey = input.writeKey?.trim() || null;
  const projectCode =
    /^[A-Za-z][A-Za-z0-9_-]{1,79}$/u.test(input.projectCode)
      ? input.projectCode
      : "invalid";
  const endpointUrl = metricEndpointFromLogIngest(input.logIngestUrl);
  const environment = normalizeClientMetricEnvironment(input.environment);
  return Object.freeze({
    enabled:
      input.enabled === true &&
      endpointUrl !== null &&
      writeKey !== null &&
      environment !== null &&
      projectCode !== "invalid",
    endpointUrl,
    writeKey: endpointUrl ? writeKey : null,
    projectCode,
  });
}

export class ClientMetricUploader {
  private inFlight: Promise<ClientMetricUploadReport> | undefined;

  public constructor(
    private readonly dependencies: Readonly<{
      outbox: ClientMetricOutboxPort;
      config: ClientMetricUploadConfig;
      fetchImpl?: typeof fetch;
      getRequestHeaders?: () => Promise<Readonly<Record<string, string>> | null>;
      now?: () => Date;
      random?: () => number;
      samplingPolicy: Readonly<{
        state: ClientMetricSamplingPolicyState;
        store: ClientMetricSamplingPolicyStorePort;
      }>;
    }>,
  ) {}

  public flush(): Promise<ClientMetricUploadReport> {
    if (this.inFlight) return this.inFlight;
    const flush = this.flushInternal()
      .catch(() => emptyUploadReport())
      .finally(() => {
        if (this.inFlight === flush) this.inFlight = undefined;
      });
    this.inFlight = flush;
    return flush;
  }

  private async flushInternal(): Promise<ClientMetricUploadReport> {
    const { config, outbox } = this.dependencies;
    if (
      !config.enabled ||
      !config.endpointUrl ||
      !config.writeKey
    ) {
      return emptyUploadReport();
    }
    const deliveries = await outbox.listReady(MAX_BATCH_EVENTS);
    if (deliveries.length === 0) return emptyUploadReport();

    const now = (this.dependencies.now ?? (() => new Date()))();
    const expiredOrFuture = deliveries.filter((item) =>
      isOutsideServerTimeWindow(item.event, now),
    );
    const uploadable = deliveries.filter(
      (item) => !isOutsideServerTimeWindow(item.event, now),
    );
    // 与服务端时间窗口保持一致；只丢弃已经能本地确定永远不会被接收的 eventId。
    if (expiredOrFuture.length > 0) {
      await outbox.markRejected(
        expiredOrFuture.map((item) => item.event.eventId),
      );
    }
    if (uploadable.length === 0) {
      return { uploaded: 0, rejected: expiredOrFuture.length, retried: 0 };
    }

    const report = await this.uploadWithIsolation(uploadable);
    return {
      uploaded: report.uploaded,
      rejected: expiredOrFuture.length + report.rejected,
      retried: report.retried,
    };
  }

  private async uploadWithIsolation(
    deliveries: readonly ClientMetricDelivery[],
  ): Promise<ClientMetricUploadReport> {
    const pending = [deliveries];
    let uploaded = 0;
    let rejected = 0;
    let retried = 0;
    while (pending.length > 0) {
      const batch = pending.shift();
      if (!batch) continue;
      const result = await this.uploadBatch(batch);
      if (result.kind === "accepted") {
        uploaded += batch.length;
        continue;
      }
      if (result.kind === "retry") {
        const retryReport = await this.retry(batch, result.errorCode);
        retried += retryReport.retried;
        continue;
      }
      if (batch.length === 1) {
        // 后端不返回逐事件拒绝 ID，只在单事件仍失败时才永久移出 outbox。
        const delivery = batch[0];
        if (!delivery) continue;
        await this.dependencies.outbox.markRejected([delivery.event.eventId]);
        rejected += 1;
        continue;
      }
      const midpoint = Math.floor(batch.length / 2);
      pending.push(batch.slice(0, midpoint), batch.slice(midpoint));
    }
    return { uploaded, rejected, retried };
  }

  private async uploadBatch(
    deliveries: readonly ClientMetricDelivery[],
  ): Promise<
    | Readonly<{ kind: "accepted" }>
    | Readonly<{ kind: "isolate" }>
    | Readonly<{ kind: "retry"; errorCode: string }>
  > {
    const { config, outbox } = this.dependencies;
    const endpointUrl = config.endpointUrl;
    const writeKey = config.writeKey;
    if (!endpointUrl || !writeKey) {
      return { kind: "retry", errorCode: "INVALID_CONFIG" };
    }
    const eventIds = deliveries.map((item) => item.event.eventId);
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), 15_000);
    try {
      const deviceHeaders = await this.dependencies.getRequestHeaders?.();
      const response = await (this.dependencies.fetchImpl ?? fetch)(
        endpointUrl,
        {
          method: "POST",
          signal: controller.signal,
          headers: {
            "Content-Type": "application/json",
            "X-Log-Project": config.projectCode,
            "X-Log-Key": writeKey,
            ...(deviceHeaders?.Authorization
              ? { Authorization: deviceHeaders.Authorization }
              : {}),
            ...(deviceHeaders?.["X-HBPOS-Device-Code"]
              ? { "X-HBPOS-Device-Code": deviceHeaders["X-HBPOS-Device-Code"] }
              : {}),
            ...(deviceHeaders?.["X-HBPOS-Store-Code"]
              ? { "X-HBPOS-Store-Code": deviceHeaders["X-HBPOS-Store-Code"] }
              : {}),
            ...(deviceHeaders?.["X-HBPOS-Hardware-Id"]
              ? { "X-HBPOS-Hardware-Id": deviceHeaders["X-HBPOS-Hardware-Id"] }
              : {}),
          },
          body: JSON.stringify({
            schemaVersion: 1,
            events: deliveries.map((item) => item.event),
          } satisfies MetricBatchV1),
        },
      );
      if (response.status === 400 || response.status === 413) {
        return { kind: "isolate" };
      }
      if (!response.ok) {
        return { kind: "retry", errorCode: `HTTP_${response.status}` };
      }

      const payload = await readIngestResponse(response);
      if (payload?.success === false || (payload?.data.rejectedCount ?? 0) > 0) {
        return { kind: "isolate" };
      }
      const consumed =
        (payload?.data.acceptedCount ?? 0) +
        (payload?.data.duplicateCount ?? 0);
      if (
        payload?.success !== true ||
        consumed !== eventIds.length ||
        payload.data.rejectedCount !== 0
      ) {
        return { kind: "retry", errorCode: "INVALID_RESPONSE" };
      }
      if (payload.data.samplingPolicy) {
        const policy = mergeSamplingPolicy(
          this.dependencies.samplingPolicy.state.read(),
          payload.data.samplingPolicy,
          deliveries.map((item) => item.event),
        );
        // 先耐久化再切换内存采样；失败时保留同一 eventId，靠 duplicate ACK 重试。
        await this.dependencies.samplingPolicy.store.save(policy);
        this.dependencies.samplingPolicy.state.replace(policy);
      }
      await outbox.markAccepted(eventIds);
      return { kind: "accepted" };
    } catch {
      return { kind: "retry", errorCode: "NETWORK" };
    } finally {
      clearTimeout(timeout);
    }
  }

  private async retry(
    deliveries: readonly ClientMetricDelivery[],
    errorCode: string,
  ): Promise<ClientMetricUploadReport> {
    const now = (this.dependencies.now ?? (() => new Date()))();
    const attempt = Math.max(
      0,
      ...deliveries.map((item) => item.attemptCount),
    );
    const baseDelayMs = Math.min(30 * 60_000, 1_000 * 2 ** Math.min(attempt, 10));
    const jitterMs = Math.round(
      (this.dependencies.random ?? Math.random)() * 15_000,
    );
    await this.dependencies.outbox.releaseRetry(
      deliveries.map((item) => item.event.eventId),
      new Date(now.getTime() + baseDelayMs + jitterMs).toISOString(),
      errorCode,
    );
    return {
      uploaded: 0,
      rejected: 0,
      retried: deliveries.length,
    };
  }
}

export class ClientMetricRuntime {
  private interval: ReturnType<typeof setInterval> | undefined;
  private flushTimer: ReturnType<typeof setTimeout> | undefined;
  private readonly pendingRecords = new Set<Promise<void>>();
  private closed = false;

  public constructor(
    private readonly recorder: ClientMetricRecorder,
    private readonly uploader: ClientMetricUploader,
    private readonly closeOutbox: () => Promise<void>,
    private readonly timer: Readonly<{
      setTimeout(callback: () => void, delayMs: number): ReturnType<typeof setTimeout>;
      clearTimeout(handle: ReturnType<typeof setTimeout>): void;
    }> = {
      setTimeout: (callback, delayMs) => setTimeout(callback, delayMs),
      clearTimeout: (handle) => clearTimeout(handle),
    },
  ) {}

  public record(draft: ClientMetricDraft): void {
    if (this.closed) return;
    const pending = this.recorder
      .record(draft)
      .then((result) => {
        if (result === "queued") this.scheduleFlush();
      })
      .catch(() => undefined)
      .finally(() => this.pendingRecords.delete(pending));
    this.pendingRecords.add(pending);
  }

  public start(): void {
    if (this.closed) return;
    if (!this.interval) {
      this.interval = setInterval(() => {
        void this.uploader.flush();
      }, 60_000);
    }
    void this.uploader.flush();
  }

  public async shutdown(): Promise<void> {
    if (this.closed) return;
    this.closed = true;
    if (this.interval) {
      clearInterval(this.interval);
      this.interval = undefined;
    }
    if (this.flushTimer) {
      this.timer.clearTimeout(this.flushTimer);
      this.flushTimer = undefined;
    }
    await Promise.allSettled([...this.pendingRecords]);
    await this.uploader.flush().catch(() => undefined);
    await this.uploader.flush().catch(() => undefined);
    await this.closeOutbox().catch(() => undefined);
  }

  private scheduleFlush(): void {
    if (this.closed || this.flushTimer) return;
    this.flushTimer = this.timer.setTimeout(() => {
      this.flushTimer = undefined;
      void this.flushAfterPendingRecords();
    }, RUNTIME_FLUSH_DEBOUNCE_MS);
  }

  private async flushAfterPendingRecords(): Promise<void> {
    // SQLite 入队可能仍在串行执行；等当前记录全部耐久化后再读取批次，避免首条先发、尾条滞留。
    await Promise.allSettled([...this.pendingRecords]);
    if (!this.closed) await this.uploader.flush();
  }
}

class ClientMetricBinding {
  private delegate: Pick<ClientMetricRuntime, "record"> | null = null;

  public bind(
    delegate: Pick<ClientMetricRuntime, "record">,
  ): () => void {
    this.delegate = delegate;
    return () => {
      if (this.delegate === delegate) this.delegate = null;
    };
  }

  public record(draft: ClientMetricDraft): void {
    try {
      this.delegate?.record(draft);
    } catch {
      // 指标旁路永远不能改变 POS 业务结果。
    }
  }
}

export const clientMetrics = new ClientMetricBinding();

export function createBusinessStartupTimer(input: Readonly<{
  startedAt?: number;
  now(): number;
  record(draft: ClientMetricDraft): void;
}>): Readonly<{
  markRuntimeReady(): void;
  markSalesFirstFrameCommitted(): void;
  markSalesInteractive(): void;
  fail(): void;
}> {
  const initializedAt = input.now();
  const startedAt =
    typeof input.startedAt === "number" &&
    Number.isFinite(input.startedAt) &&
    input.startedAt >= 0 &&
    input.startedAt <= initializedAt
      ? input.startedAt
      : initializedAt;
  let terminal = false;
  let runtimeReady = false;
  let salesFirstFrameCommitted = false;
  let salesInteractive = false;

  const finish = (outcome: "success" | "failure"): void => {
    if (terminal) return;
    terminal = true;
    try {
      input.record({
        metric: POS_CLIENT_METRICS.coldStart,
        valueMs: Math.max(0, input.now() - startedAt),
        dimensions: { outcome },
      });
    } catch {
      // 冷启动指标旁路失败不得改变应用启动结果。
    }
  };
  const finishWhenReady = (): void => {
    if (
      runtimeReady &&
      salesFirstFrameCommitted &&
      salesInteractive
    ) {
      finish("success");
    }
  };

  return Object.freeze({
    markRuntimeReady() {
      if (terminal) return;
      runtimeReady = true;
      finishWhenReady();
    },
    markSalesFirstFrameCommitted() {
      if (terminal) return;
      salesFirstFrameCommitted = true;
      finishWhenReady();
    },
    markSalesInteractive() {
      if (terminal) return;
      salesInteractive = true;
      finishWhenReady();
    },
    fail() {
      finish("failure");
    },
  });
}

function metricEndpointFromLogIngest(
  value: string | null | undefined,
): string | null {
  if (!value?.trim()) return null;
  try {
    const url = new URL(value.trim());
    const loopbackHttp =
      url.protocol === "http:" &&
      ["localhost", "127.0.0.1", "::1", "[::1]"].includes(url.hostname);
    if (
      (url.protocol !== "https:" && !loopbackHttp) ||
      url.username ||
      url.password
    ) {
      return null;
    }
    url.pathname = "/api/system/performance/client-batches";
    url.search = "";
    url.hash = "";
    return url.toString();
  } catch {
    return null;
  }
}

function parseMetricEvent(value: string): MetricEventV1 | null {
  try {
    const parsed: unknown = JSON.parse(value);
    if (!isRecord(parsed)) return null;
    return buildMetricEventV1({
      eventId: typeof parsed.eventId === "string" ? parsed.eventId : "",
      metric: parsed.metric as PosClientMetricName,
      observedAt:
        typeof parsed.observedAt === "string" ? parsed.observedAt : "",
      valueMs: typeof parsed.value === "number" ? parsed.value : Number.NaN,
      dimensions: isStringRecord(parsed.dimensions)
        ? parsed.dimensions
        : { invalid: "invalid" },
    });
  } catch {
    return null;
  }
}

function isOutsideServerTimeWindow(event: MetricEventV1, now: Date): boolean {
  const observedAtMs = new Date(event.observedAt).getTime();
  if (!Number.isFinite(observedAtMs)) return false;
  const nowMs = now.getTime();
  return observedAtMs < nowMs - MAX_EVENT_AGE_MS ||
    observedAtMs > nowMs + MAX_FUTURE_CLOCK_SKEW_MS;
}

async function readIngestResponse(
  response: Response,
): Promise<Readonly<{
  success: boolean;
  data: Readonly<{
    acceptedCount: number;
    duplicateCount: number;
    rejectedCount: number;
    samplingPolicy: ClientMetricSamplingPolicy | null;
  }>;
}> | null> {
  try {
    const payload: unknown = await response.json();
    if (!isRecord(payload) || typeof payload.success !== "boolean") return null;
    const data = payload.data;
    if (
      !isRecord(data) ||
      !isNonNegativeInteger(data.acceptedCount) ||
      !isNonNegativeInteger(data.duplicateCount) ||
      !isNonNegativeInteger(data.rejectedCount)
    ) {
      return null;
    }
    const hasSamplingPolicy =
      "baselineState" in data ||
      "defaultSampleRate" in data ||
      "policies" in data;
    const samplingPolicy = hasSamplingPolicy
      ? normalizeSamplingPolicy({
          baselineState: data.baselineState,
          defaultSampleRate: data.defaultSampleRate,
          policies: data.policies,
        })
      : null;
    if (hasSamplingPolicy && !samplingPolicy) return null;
    return {
      success: payload.success,
      data: {
        acceptedCount: data.acceptedCount,
        duplicateCount: data.duplicateCount,
        rejectedCount: data.rejectedCount,
        samplingPolicy,
      },
    };
  } catch {
    return null;
  }
}

function mergeSamplingPolicy(
  current: ClientMetricSamplingPolicy,
  incoming: ClientMetricSamplingPolicy,
  delivered: readonly MetricEventV1[],
): ClientMetricSamplingPolicy {
  if (incoming.baselineState !== "frozen") return incoming;
  const covered = new Set(
    delivered.map((event) => policyKey(event.metric, metricSelector(event))),
  );
  const merged = new Map<string, ClientMetricSamplingRule>();
  if (current.baselineState === "frozen") {
    for (const rule of current.policies) {
      if (!covered.has(policyKey(rule.metric, rule.selector))) {
        merged.set(policyKey(rule.metric, rule.selector), rule);
      }
    }
  }
  for (const rule of incoming.policies) {
    merged.set(policyKey(rule.metric, rule.selector), rule);
  }
  for (const event of delivered) {
    const selector = metricSelector(event);
    const key = policyKey(event.metric, selector);
    if (!merged.has(key)) {
      merged.set(key, {
        metric: event.metric,
        selector,
        sampleRate: incoming.defaultSampleRate,
        slowThreshold: null,
      });
    }
  }
  return requireSamplingPolicy({
    baselineState: incoming.baselineState,
    defaultSampleRate: incoming.defaultSampleRate,
    policies: [...merged.values()],
  });
}

function metricSelector(event: MetricEventV1): string {
  return event.dimensions.app ?? "all";
}

function policyKey(metric: PosClientMetricName, selector: string): string {
  return `${metric}\u0000${selector}`;
}

function requireSamplingPolicy(
  value: ClientMetricSamplingPolicy,
): ClientMetricSamplingPolicy {
  const normalized = normalizeSamplingPolicy(value);
  if (!normalized) throw new Error("客户端指标采样策略无效");
  return normalized;
}

function normalizeSamplingPolicy(
  value: unknown,
): ClientMetricSamplingPolicy | null {
  if (!isRecord(value)) return null;
  if (
    value.baselineState !== "not_started" &&
    value.baselineState !== "observing" &&
    value.baselineState !== "frozen"
  ) {
    return null;
  }
  if (!isSampleRate(value.defaultSampleRate)) return null;
  if (
    !Array.isArray(value.policies) ||
    value.policies.length > MAX_SAMPLING_POLICIES
  ) {
    return null;
  }
  const policies: ClientMetricSamplingRule[] = [];
  const keys = new Set<string>();
  for (const rawRule of value.policies) {
    if (!isRecord(rawRule)) return null;
    // 后端全局 WhenWritingNull 会省略 nullable slowThreshold；wire 缺省等同 null。
    const slowThreshold = rawRule.slowThreshold ?? null;
    if (
      typeof rawRule.metric !== "string" ||
      !POS_CLIENT_METRIC_NAMES.has(rawRule.metric as PosClientMetricName) ||
      typeof rawRule.selector !== "string" ||
      !isSampleRate(rawRule.sampleRate) ||
      !(
        slowThreshold === null ||
        (typeof slowThreshold === "number" &&
          Number.isFinite(slowThreshold) &&
          slowThreshold >= 0)
      )
    ) {
      return null;
    }
    const selector = rawRule.selector.trim();
    if (
      !selector ||
      selector.length > 120 ||
      sanitizeText(selector, 120) !== selector
    ) {
      return null;
    }
    const metric = rawRule.metric as PosClientMetricName;
    const key = policyKey(metric, selector);
    if (keys.has(key)) return null;
    keys.add(key);
    policies.push(
      Object.freeze({
        metric,
        selector,
        sampleRate: rawRule.sampleRate,
        slowThreshold,
      }),
    );
  }
  return Object.freeze({
    baselineState: value.baselineState,
    defaultSampleRate: value.defaultSampleRate,
    policies: Object.freeze(policies),
  });
}

function isSampleRate(value: unknown): value is number {
  return typeof value === "number" &&
    Number.isFinite(value) &&
    value >= 0 &&
    value <= 1;
}

function stableUnitInterval(value: string): number {
  let hash = 0x811c9dc5;
  for (let index = 0; index < value.length; index += 1) {
    hash ^= value.charCodeAt(index);
    hash = Math.imul(hash, 0x01000193);
  }
  return (hash >>> 0) / 0x1_0000_0000;
}

function safeDimensionKey(value: string): string {
  return /^[A-Za-z][A-Za-z0-9_-]{0,63}$/u.test(value)
    ? value
    : "<invalid>";
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function isStringRecord(
  value: unknown,
): value is Record<string, string> {
  return (
    isRecord(value) &&
    Object.values(value).every((item) => typeof item === "string")
  );
}

function isNonNegativeInteger(value: unknown): value is number {
  return Number.isSafeInteger(value) && Number(value) >= 0;
}

function emptyUploadReport(): ClientMetricUploadReport {
  return { uploaded: 0, rejected: 0, retried: 0 };
}
