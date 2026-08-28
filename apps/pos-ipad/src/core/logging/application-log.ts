import type { SqliteConnectionPort } from "@hb/pos-db/core/db/types";

export type ApplicationLogLevel =
  "Trace" | "Debug" | "Information" | "Warning" | "Error" | "Critical";

export type ApplicationLogEntry = Readonly<{
  clientEventId: string;
  timestampUtc: string;
  level: ApplicationLogLevel;
  message: string;
  category: string | null;
  traceId: string | null;
  exceptionType: string | null;
  exceptionMessage: string | null;
  stackTrace: string | null;
  storeCode: string | null;
  deviceCode: string | null;
  userId: string | null;
  userName: string | null;
  appVersion: string | null;
  instanceId: string | null;
  properties: Readonly<Record<string, string>> | null;
}>;

export type ApplicationLogDeliveryEntry = ApplicationLogEntry &
  Readonly<{ attemptCount: number }>;

export type ApplicationLogDraft = Readonly<{
  level: ApplicationLogLevel;
  message: string;
  category?: string | null;
  traceId?: string | null;
  error?: unknown;
  properties?: Readonly<Record<string, unknown>> | null;
}>;

export type ApplicationLogContext = Readonly<{
  storeCode: string | null;
  deviceCode: string | null;
  userId: string | null;
  userName: string | null;
  appVersion: string;
  instanceId: string;
}>;

export type ApplicationLogActor = Readonly<{
  userId: string;
  userName: string;
}>;

/**
 * 日志只需收银员的不可变身份投影，绝不保存条码、权限或 bearer。
 * 由组合根在可信登录成功后绑定，ApplicationLogger 在入队时读取快照。
 */
export class ApplicationLogActorBinding {
  private actor: ApplicationLogActor | null = null;

  public bind(actor: ApplicationLogActor): void {
    this.actor = Object.freeze({
      userId: actor.userId,
      userName: actor.userName,
    });
  }

  public clear(): void {
    this.actor = null;
  }

  public read(): ApplicationLogActor | null {
    return this.actor;
  }
}

export type ApplicationLogCenterConfig = Readonly<{
  enabled: boolean;
  ingestUrl: string | null;
  writeKey: string | null;
  environment: string;
}>;

export interface ApplicationLogOutboxPort {
  enqueue(entry: ApplicationLogEntry): Promise<void>;
  listReady(limit: number): Promise<readonly ApplicationLogDeliveryEntry[]>;
  markAccepted(eventIds: readonly string[]): Promise<void>;
  markRejected(
    entries: readonly Readonly<{ eventId: string; code: string }>[],
  ): Promise<void>;
  releaseRetry(
    eventIds: readonly string[],
    nextAttemptAtIso: string,
    errorCode: string,
  ): Promise<void>;
}

export type ApplicationLogUploadReport = Readonly<{
  uploaded: number;
  rejected: number;
  retried: number;
}>;

type ApplicationLogWireItem = Readonly<{
  level: string;
  message: string;
  timestampUtc: string;
  projectCode: string;
  environment: string;
  sourceType: string;
  serviceName: string;
  instanceId: string | null;
  clientEventId: string;
  storeCode: string | null;
  deviceCode: string | null;
  appVersion: string | null;
  category: string | null;
  eventId: null;
  traceId: string | null;
  requestPath: null;
  requestMethod: null;
  statusCode: null;
  userId: string | null;
  userName: string | null;
  clientIp: null;
  exceptionType: string | null;
  exceptionMessage: string | null;
  stackTrace: string | null;
  properties: Readonly<Record<string, string>> | null;
}>;

type PreparedApplicationLogUpload = Readonly<{
  entry: ApplicationLogDeliveryEntry;
  serializedWire: string;
}>;

// 与公开入口 canonical JSON 契约一致：字段看解码值，item/batch 看最终 wire JSON。
const applicationLogFieldMaximumBytes = 32 * 1024;
const applicationLogItemMaximumBytes = 64 * 1024;
const applicationLogBatchMaximumBytes = 1024 * 1024;
// ASP.NET RequestSizeLimit 仍是最终安全兜底；正常 batch 已被更严格的 1 MiB 限制覆盖。
const applicationLogRequestMaximumBytes = 4 * 1024 * 1024;
const emptyApplicationLogRequest = '{"logs":[]}';
const utf8Encoder = new TextEncoder();

export class ApplicationLogger {
  public constructor(
    private readonly outbox: ApplicationLogOutboxPort,
    private readonly context: () => ApplicationLogContext,
    private readonly createId: () => string,
    private readonly nowIso: () => string,
  ) {}

  public async record(draft: ApplicationLogDraft): Promise<void> {
    try {
      const context = this.context();
      await this.outbox.enqueue({
        clientEventId: this.createId(),
        timestampUtc: this.nowIso(),
        level: draft.level,
        message: sanitizeText(draft.message, 8_000),
        category: sanitizeOptionalText(draft.category),
        traceId: sanitizeOptionalText(draft.traceId),
        exceptionType: sanitizeErrorType(draft.error),
        exceptionMessage: sanitizeErrorMessage(draft.error),
        stackTrace: sanitizeStack(draft.error),
        storeCode: context.storeCode,
        deviceCode: context.deviceCode,
        userId: context.userId,
        userName: context.userName,
        appVersion: context.appVersion,
        instanceId: context.instanceId,
        properties: sanitizeProperties(draft.properties),
      });
    } catch {
      // 程序日志是旁路；SQLite 异常不能反向中断 POS 业务或 UI 生命周期。
    }
  }
}

/** 直连中心日志：不使用 HbposTransport，因此永不携带 device/cashier Bearer。 */
export class ApplicationLogUploader {
  private inFlight: Promise<ApplicationLogUploadReport> | undefined;

  public constructor(
    private readonly outbox: ApplicationLogOutboxPort,
    private readonly config: ApplicationLogCenterConfig,
    private readonly fetchImpl: typeof fetch = fetch,
    private readonly now: () => Date = () => new Date(),
    private readonly random: () => number = Math.random,
  ) {}

  public flush(): Promise<ApplicationLogUploadReport> {
    if (this.inFlight) return this.inFlight;
    const flush = this.flushInternal()
      .catch(() => emptyReport())
      .finally(() => {
        if (this.inFlight === flush) this.inFlight = undefined;
      });
    this.inFlight = flush;
    return flush;
  }

  private async flushInternal(): Promise<ApplicationLogUploadReport> {
    if (
      !this.config.enabled ||
      !this.config.ingestUrl ||
      !this.config.writeKey
    ) {
      return emptyReport();
    }
    const entries = await this.outbox.listReady(100);
    if (!entries.length) return emptyReport();

    const prepared = prepareApplicationLogBatch(
      entries,
      this.config.environment,
    );
    if (prepared.rejected.length > 0) {
      await this.outbox.markRejected(prepared.rejected);
    }
    const localReport: ApplicationLogUploadReport = {
      uploaded: 0,
      rejected: prepared.rejected.length,
      retried: 0,
    };
    if (!prepared.uploads.length) return localReport;

    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), 15_000);
    try {
      return mergeUploadReports(
        localReport,
        await this.uploadBatch(prepared.uploads, controller.signal),
      );
    } finally {
      clearTimeout(timeout);
    }
  }

  private async uploadBatch(
    uploads: readonly PreparedApplicationLogUpload[],
    signal: AbortSignal,
  ): Promise<ApplicationLogUploadReport> {
    const entries = uploads.map((item) => item.entry);
    try {
      const requestBody = buildApplicationLogRequestBody(uploads);
      // prepareApplicationLogBatch 已逐项精确计数；这里对最终实际发送文本再做一次确认。
      if (utf8ByteLength(requestBody) > applicationLogRequestMaximumBytes) {
        return emptyReport();
      }
      const response = await this.fetchImpl(this.config.ingestUrl!, {
        method: "POST",
        signal,
        headers: {
          "Content-Type": "application/json",
          "X-Log-Project": "hbpos_ipad",
          "X-Log-Key": this.config.writeKey!,
        },
        body: requestBody,
      });
      if (response.status === 400 || response.status === 413) {
        if (uploads.length === 1) {
          await this.outbox.markRejected([
            {
              eventId: entries[0]!.clientEventId,
              code: `LOG_HTTP_${response.status}`,
            },
          ]);
          return { uploaded: 0, rejected: 1, retried: 0 };
        }

        // 仅对契约/大小拒绝二分；子批每次严格缩小，最多 2N-1 次请求（N <= 100）。
        const midpoint = Math.floor(uploads.length / 2);
        const left = await this.uploadBatch(uploads.slice(0, midpoint), signal);
        const right = await this.uploadBatch(uploads.slice(midpoint), signal);
        return mergeUploadReports(left, right);
      }
      if (!response.ok) {
        return this.retryAll(
          entries,
          response.status === 429 ? retryAfterMs(response) : 0,
          `LOG_HTTP_${response.status}`,
          response.status === 401 || response.status === 403,
        );
      }
      const body: unknown = await response.json().catch(() => null);
      const acknowledgements = readAcknowledgements(body);
      const received = new Map(
        acknowledgements.map((item) => [
          canonicalizeApplicationLogClientEventId(item.eventId),
          item,
        ]),
      );
      const accepted: string[] = [];
      const rejected: { eventId: string; code: string }[] = [];
      const retry: ApplicationLogDeliveryEntry[] = [];
      for (const entry of entries) {
        // 后端 Guid 回执使用小写 D 格式；状态写回仍必须使用本地 outbox 原始主键。
        const acknowledgement = received.get(
          canonicalizeApplicationLogClientEventId(entry.clientEventId),
        );
        if (!acknowledgement) {
          retry.push(entry);
          continue;
        }
        if (
          acknowledgement.status === "accepted" ||
          acknowledgement.status === "duplicate"
        ) {
          accepted.push(entry.clientEventId);
        } else if (acknowledgement.status === "rejected") {
          rejected.push({
            eventId: entry.clientEventId,
            code: acknowledgement.code ?? "LOG_REJECTED",
          });
        } else {
          retry.push(entry);
        }
      }
      await this.outbox.markAccepted(accepted);
      await this.outbox.markRejected(rejected);
      const retried = retry.length
        ? await this.retry(retry, 0, "LOG_ACK_INCOMPLETE")
        : 0;
      return { uploaded: accepted.length, rejected: rejected.length, retried };
    } catch {
      // 上传器绝不通过 ApplicationLogger 记录自身错误，防止递归产生日志。
      return this.retryAll(entries, 0, "LOG_NETWORK_FAILURE");
    }
  }

  private async retryAll(
    entries: readonly ApplicationLogDeliveryEntry[],
    minimumDelayMs: number,
    errorCode: string,
    authenticationFailure = false,
  ): Promise<ApplicationLogUploadReport> {
    const retried = await this.retry(
      entries,
      authenticationFailure
        ? Math.max(minimumDelayMs, 30 * 60_000)
        : minimumDelayMs,
      errorCode,
    );
    return { uploaded: 0, rejected: 0, retried };
  }

  private async retry(
    entries: readonly ApplicationLogDeliveryEntry[],
    minimumDelayMs: number,
    errorCode: string,
  ): Promise<number> {
    try {
      if (!entries.length) return 0;
      const attemptCount = Math.max(
        0,
        ...entries.map((entry) => entry.attemptCount),
      );
      const baseDelayMs = [60_000, 120_000, 300_000, 900_000, 1_800_000][
        Math.min(attemptCount, 4)
      ]!;
      const jitterMs = Math.round(this.random() * 15_000);
      await this.outbox.releaseRetry(
        entries.map((entry) => entry.clientEventId),
        new Date(
          this.now().getTime() +
            Math.max(baseDelayMs + jitterMs, minimumDelayMs),
        ).toISOString(),
        errorCode,
      );
      return entries.length;
    } catch {
      return 0;
    }
  }
}

export class ApplicationLogRuntime {
  private interval: ReturnType<typeof setInterval> | undefined;

  public constructor(
    public readonly logger: ApplicationLogger,
    private readonly uploader: ApplicationLogUploader,
  ) {}

  public record(draft: ApplicationLogDraft): void {
    void this.logger.record(draft).catch(() => undefined);
  }

  public onApplicationStarted(): void {
    this.startTimer();
    void this.flush();
  }

  public onForeground(): void {
    this.startTimer();
    void this.flush();
  }

  public onNetworkChanged(isOnline: boolean): void {
    if (isOnline) void this.flush();
  }

  public async flush(): Promise<ApplicationLogUploadReport> {
    return this.uploader.flush();
  }

  public async shutdown(): Promise<void> {
    if (this.interval) {
      clearInterval(this.interval);
      this.interval = undefined;
    }
    // 若已有单飞 flush 在本次 shutdown 日志入队前完成 listReady，第一次只会等待它；
    // 第二次必须重扫，才能在关闭数据库前看到已 await 的最后一条日志。
    await this.flush().catch(() => undefined);
    await this.flush().catch(() => undefined);
  }

  private startTimer(): void {
    if (this.interval) return;
    this.interval = setInterval(() => {
      void this.flush();
    }, 60_000);
  }
}

export class SqliteApplicationLogOutbox implements ApplicationLogOutboxPort {
  public constructor(
    private readonly db: SqliteConnectionPort,
    private readonly nowIso: () => string,
  ) {}

  public async enqueue(entry: ApplicationLogEntry): Promise<void> {
    await this.db.withExclusiveTransaction(async (transaction) => {
      await transaction.run(
        `INSERT INTO application_log_outbox (
          event_id, occurred_at_iso, payload_json, delivery_state, attempt_count,
          next_attempt_at_iso, last_error_code, created_at_iso
        ) VALUES (?, ?, ?, 'pending', 0, ?, NULL, ?)`,
        [
          entry.clientEventId,
          entry.timestampUtc,
          JSON.stringify(entry),
          entry.timestampUtc,
          this.nowIso(),
        ],
      );
      // 程序日志允许有界丢弃最旧 Pending；员工审计不使用此表，永不受影响。
      await transaction.run(
        `DELETE FROM application_log_outbox
         WHERE event_id IN (
           SELECT event_id FROM application_log_outbox
           WHERE delivery_state = 'pending'
           ORDER BY occurred_at_iso DESC
           LIMIT -1 OFFSET 50000
         )`,
      );
      await transaction.run(
        `DELETE FROM application_log_outbox
         WHERE delivery_state = 'rejected' AND created_at_iso < ?`,
        [
          new Date(
            Date.parse(this.nowIso()) - 30 * 24 * 60 * 60 * 1000,
          ).toISOString(),
        ],
      );
    });
  }

  public async listReady(
    limit: number,
  ): Promise<readonly ApplicationLogDeliveryEntry[]> {
    await this.db.run(
      `DELETE FROM application_log_outbox
       WHERE delivery_state = 'rejected' AND created_at_iso < ?`,
      [
        new Date(
          Date.parse(this.nowIso()) - 30 * 24 * 60 * 60 * 1000,
        ).toISOString(),
      ],
    );
    const maximum = Math.max(0, Math.floor(limit));
    if (maximum === 0) return [];

    const ready: ApplicationLogDeliveryEntry[] = [];
    const selectedEventIds = new Set<string>();
    while (ready.length < maximum) {
      const rows = await this.db.getAll<{
        event_id: unknown;
        payload_json: unknown;
        attempt_count: unknown;
      }>(
        `SELECT event_id, payload_json, attempt_count FROM application_log_outbox
         WHERE delivery_state = 'pending' AND next_attempt_at_iso <= ?
         ORDER BY occurred_at_iso
         LIMIT ?`,
        [this.nowIso(), maximum - ready.length],
      );
      if (!rows.length) break;

      const rejected: { eventId: string; code: string }[] = [];
      for (const row of rows) {
        const entry = parseEntry(row.payload_json);
        // payload 和主键必须一致；否则不可确定应上传哪一条，永久隔离而非热循环。
        if (
          typeof row.event_id !== "string" ||
          !entry ||
          entry.clientEventId !== row.event_id
        ) {
          if (typeof row.event_id === "string") {
            rejected.push({
              eventId: row.event_id,
              code: "LOG_PAYLOAD_INVALID",
            });
          }
          continue;
        }
        if (!selectedEventIds.has(entry.clientEventId)) {
          selectedEventIds.add(entry.clientEventId);
          ready.push({
            ...entry,
            attemptCount: Math.max(0, Number(row.attempt_count) || 0),
          });
        }
      }

      if (!rejected.length) break;
      await this.markRejected(rejected);
    }
    return ready;
  }

  public async markAccepted(eventIds: readonly string[]): Promise<void> {
    await Promise.all(
      eventIds.map((eventId) =>
        this.db.run(
          "DELETE FROM application_log_outbox WHERE event_id = ? AND delivery_state = 'pending'",
          [eventId],
        ),
      ),
    );
  }

  public async markRejected(
    entries: readonly Readonly<{ eventId: string; code: string }>[],
  ): Promise<void> {
    await Promise.all(
      entries.map((entry) =>
        this.db.run(
          `UPDATE application_log_outbox
       SET delivery_state = 'rejected', last_error_code = ?
       WHERE event_id = ? AND delivery_state = 'pending'`,
          [entry.code, entry.eventId],
        ),
      ),
    );
  }

  public async releaseRetry(
    eventIds: readonly string[],
    nextAttemptAtIso: string,
    errorCode: string,
  ): Promise<void> {
    await Promise.all(
      eventIds.map((eventId) =>
        this.db.run(
          `UPDATE application_log_outbox
       SET attempt_count = attempt_count + 1, next_attempt_at_iso = ?, last_error_code = ?
       WHERE event_id = ? AND delivery_state = 'pending'`,
          [nextAttemptAtIso, errorCode, eventId],
        ),
      ),
    );
  }
}

export function resolveApplicationLogCenterConfig(
  input: Readonly<{
    enabled: boolean | undefined;
    ingestUrl: string | undefined;
    writeKey: string | undefined;
    environment: string | undefined;
  }>,
): ApplicationLogCenterConfig {
  const ingestUrl = input.ingestUrl?.trim() || null;
  const writeKey = input.writeKey?.trim() || null;
  const validUrl = isSafeIngestUrl(ingestUrl);
  return Object.freeze({
    // 配置错误只关闭日志旁路，绝不能阻止 POS 运行时初始化。
    enabled: input.enabled === true && validUrl && writeKey !== null,
    ingestUrl: validUrl ? ingestUrl : null,
    writeKey: validUrl ? writeKey : null,
    environment: input.environment?.trim() || "production",
  });
}

export function sanitizeProperties(
  properties: Readonly<Record<string, unknown>> | null | undefined,
): Readonly<Record<string, string>> | null {
  if (!properties) return null;
  const result: Record<string, string> = {};
  for (const [key, value] of Object.entries(properties)) {
    if (isSensitiveKey(key)) {
      // 敏感 key 本身也可能携带秘密；固定占位符碰撞采用可接受的 last-wins。
      result[redactedPropertyKey] = "[REDACTED]";
    } else if (typeof value === "string") {
      const sanitizedKey = sanitizeText(key, 1_000);
      result[sanitizedKey] = sanitizeText(value, 1_000);
    } else if (typeof value === "number" || typeof value === "boolean") {
      const sanitizedKey = sanitizeText(key, 1_000);
      result[sanitizedKey] = String(value);
    }
  }
  return Object.keys(result).length > 0 ? result : null;
}

function parseEntry(raw: unknown): ApplicationLogEntry | null {
  if (typeof raw !== "string") return null;
  try {
    const value: unknown = JSON.parse(raw);
    return isApplicationLogEntry(value) ? value : null;
  } catch {
    return null;
  }
}

const applicationLogLevels = new Set<ApplicationLogLevel>([
  "Trace",
  "Debug",
  "Information",
  "Warning",
  "Error",
  "Critical",
]);
const applicationLogClientEventIdPattern =
  /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/iu;
const applicationLogTimestampPattern =
  /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2}):(\d{2})(?:\.\d{1,7})?(Z|[+-](\d{2}):(\d{2}))$/u;
const applicationLogCanonicalTimestampPattern =
  /^(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2})(?:\.(\d{1,7}))?(Z|[+-]\d{2}:\d{2})$/u;

function isApplicationLogEntry(value: unknown): value is ApplicationLogEntry {
  if (!value || typeof value !== "object" || Array.isArray(value)) return false;
  const entry = value as Record<string, unknown>;
  if (
    typeof entry.clientEventId !== "string" ||
    !applicationLogClientEventIdPattern.test(entry.clientEventId) ||
    typeof entry.timestampUtc !== "string" ||
    !isApplicationLogTimestamp(entry.timestampUtc) ||
    typeof entry.level !== "string" ||
    !applicationLogLevels.has(entry.level as ApplicationLogLevel) ||
    typeof entry.message !== "string"
  ) {
    return false;
  }

  for (const field of [
    "category",
    "traceId",
    "exceptionType",
    "exceptionMessage",
    "stackTrace",
    "storeCode",
    "deviceCode",
    "userId",
    "userName",
    "appVersion",
    "instanceId",
  ]) {
    const fieldValue = entry[field];
    if (fieldValue !== null && typeof fieldValue !== "string") return false;
  }

  if (entry.properties === null) return true;
  if (
    !entry.properties ||
    typeof entry.properties !== "object" ||
    Array.isArray(entry.properties)
  ) {
    return false;
  }
  return Object.values(entry.properties).every(
    (propertyValue) => typeof propertyValue === "string",
  );
}

function isApplicationLogTimestamp(value: string): boolean {
  const match = applicationLogTimestampPattern.exec(value);
  if (!match || !Number.isFinite(Date.parse(value))) return false;
  const year = Number(match[1]);
  const month = Number(match[2]);
  const day = Number(match[3]);
  const hour = Number(match[4]);
  const minute = Number(match[5]);
  const second = Number(match[6]);
  const offsetHour = match[8] === undefined ? 0 : Number(match[8]);
  const offsetMinute = match[9] === undefined ? 0 : Number(match[9]);
  const leapYear = year % 4 === 0 && (year % 100 !== 0 || year % 400 === 0);
  const daysInMonth = [
    31,
    leapYear ? 29 : 28,
    31,
    30,
    31,
    30,
    31,
    31,
    30,
    31,
    30,
    31,
  ];
  return (
    year >= 1 &&
    month >= 1 &&
    month <= 12 &&
    day >= 1 &&
    day <= daysInMonth[month - 1]! &&
    hour <= 23 &&
    minute <= 59 &&
    second <= 59 &&
    offsetHour <= 14 &&
    offsetMinute <= 59 &&
    (offsetHour < 14 || offsetMinute === 0)
  );
}

function canonicalizeApplicationLogClientEventId(value: string): string {
  return applicationLogClientEventIdPattern.test(value)
    ? value.toLowerCase()
    : value;
}

function canonicalizeApplicationLogTimestamp(value: string): string {
  const match = applicationLogCanonicalTimestampPattern.exec(value);
  if (!match) return value;

  // 先按 offset 换算整秒，再原样保留最多 7 位 tick 精度；仅裁掉后端也会裁掉的尾零。
  const wholeSecond = new Date(`${match[1]}${match[3]}`);
  if (!Number.isFinite(wholeSecond.getTime())) return value;
  const utcTimestamp = wholeSecond.toISOString();
  if (utcTimestamp.startsWith("+") || utcTimestamp.startsWith("-")) {
    return value;
  }
  const fraction = match[2]?.replace(/0+$/u, "") ?? "";
  return `${utcTimestamp.slice(0, 19)}${fraction ? `.${fraction}` : ""}Z`;
}

function readAcknowledgements(body: unknown): readonly Readonly<{
  eventId: string;
  status: string;
  code: string | null;
}>[] {
  if (!body || typeof body !== "object") return [];
  const response = body as {
    data?: { results?: unknown; items?: unknown } | null;
    results?: unknown;
    items?: unknown;
  };
  // SystemLogsController 返回 ApiResponse<ApplicationLogIngestResultDto>；兼容旧裸响应。
  const values =
    response.data?.results ??
    response.data?.items ??
    response.results ??
    response.items;
  if (!Array.isArray(values)) return [];
  return values.flatMap((value) => {
    if (!value || typeof value !== "object") return [];
    const item = value as {
      clientEventId?: unknown;
      eventId?: unknown;
      status?: unknown;
      errorCode?: unknown;
      code?: unknown;
    };
    const eventId =
      typeof item.clientEventId === "string"
        ? item.clientEventId
        : item.eventId;
    return typeof eventId === "string" && typeof item.status === "string"
      ? [
          {
            eventId,
            status: item.status.toLowerCase(),
            code:
              typeof item.errorCode === "string"
                ? item.errorCode
                : typeof item.code === "string"
                  ? item.code
                  : null,
          },
        ]
      : [];
  });
}

function retryAfterMs(response: Response): number {
  const header = response.headers.get("Retry-After");
  if (!header) return 60_000;
  const seconds = Number(header);
  return Number.isFinite(seconds) && seconds >= 0 ? seconds * 1_000 : 60_000;
}

function toWireItem(
  entry: ApplicationLogEntry,
  environment: string,
): ApplicationLogWireItem {
  // 显式对齐 ApplicationLogIngestItemDto，禁止传递本地 delivery 元数据或过期字段。
  return {
    level: entry.level,
    // 旧版 outbox 或非标准调用方也只能上传脱敏后的自由文本。
    message: sanitizeText(entry.message, 8_000),
    timestampUtc: canonicalizeApplicationLogTimestamp(entry.timestampUtc),
    projectCode: "hbpos_ipad",
    environment,
    sourceType: "POS",
    serviceName: "Hbpos.Client.iPad",
    instanceId: entry.instanceId,
    clientEventId: canonicalizeApplicationLogClientEventId(
      entry.clientEventId,
    ),
    storeCode: entry.storeCode,
    deviceCode: entry.deviceCode,
    appVersion: entry.appVersion,
    category: sanitizeOptionalText(entry.category),
    eventId: null,
    traceId: sanitizeOptionalText(entry.traceId),
    requestPath: null,
    requestMethod: null,
    statusCode: null,
    userId: entry.userId,
    userName: entry.userName,
    clientIp: null,
    exceptionType: sanitizeOptionalText(entry.exceptionType),
    // 异常正文与堆栈采用与记录时相同的上限，上传前仍强制二次脱敏。
    exceptionMessage: entry.exceptionMessage
      ? sanitizeText(entry.exceptionMessage, 2_000)
      : null,
    stackTrace: entry.stackTrace
      ? sanitizeText(entry.stackTrace, 8_000)
      : null,
    properties: sanitizeProperties(entry.properties),
  };
}

function prepareApplicationLogBatch(
  entries: readonly ApplicationLogDeliveryEntry[],
  environment: string,
): Readonly<{
  uploads: readonly PreparedApplicationLogUpload[];
  rejected: readonly Readonly<{ eventId: string; code: string }>[];
}> {
  const uploads: PreparedApplicationLogUpload[] = [];
  const rejected: { eventId: string; code: string }[] = [];
  let requestBytes = utf8ByteLength(emptyApplicationLogRequest);

  for (const entry of entries) {
    try {
      const wire = toWireItem(entry, environment);
      const serializedWire = JSON.stringify(wire);
      const serializedWireBytes = utf8ByteLength(serializedWire);
      if (
        !hasValidApplicationLogWireFields(wire) ||
        serializedWireBytes > applicationLogItemMaximumBytes
      ) {
        rejected.push({
          eventId: entry.clientEventId,
          code: "LOG_PAYLOAD_TOO_LARGE",
        });
        continue;
      }

      const additionalRequestBytes =
        serializedWireBytes + (uploads.length === 0 ? 0 : 1);
      if (
        additionalRequestBytes > applicationLogBatchMaximumBytes - requestBytes ||
        additionalRequestBytes >
          applicationLogRequestMaximumBytes - requestBytes
      ) {
        // 第一个未装入的有效项及其后的所有项保持 pending，禁止越过 FIFO 容量边界。
        break;
      }

      uploads.push({ entry, serializedWire });
      requestBytes += additionalRequestBytes;
    } catch {
      // 非 SQLite 实现若违反 port 形状，也只隔离该项，不能拖累同批有效日志。
      rejected.push({
        eventId: entry.clientEventId,
        code: "LOG_PAYLOAD_INVALID",
      });
    }
  }

  return { uploads, rejected };
}

function hasValidApplicationLogWireFields(
  item: ApplicationLogWireItem,
): boolean {
  // 字段集合镜像 ApplicationLogIngestItemDto；非字符串的 UUID/时间不属于字段预算。
  const fields = [
    item.level,
    item.message,
    item.projectCode,
    item.environment,
    item.sourceType,
    item.serviceName,
    item.instanceId,
    item.storeCode,
    item.deviceCode,
    item.appVersion,
    item.category,
    item.traceId,
    item.userId,
    item.userName,
    item.exceptionType,
    item.exceptionMessage,
    item.stackTrace,
  ];
  for (const field of fields) {
    const bytes = field ? utf8ByteLength(field) : 0;
    if (bytes > applicationLogFieldMaximumBytes) return false;
  }
  for (const [key, value] of Object.entries(item.properties ?? {})) {
    const keyBytes = utf8ByteLength(key);
    const valueBytes = utf8ByteLength(value);
    if (
      keyBytes > applicationLogFieldMaximumBytes ||
      valueBytes > applicationLogFieldMaximumBytes
    ) {
      return false;
    }
  }
  return true;
}

function buildApplicationLogRequestBody(
  uploads: readonly PreparedApplicationLogUpload[],
): string {
  return `{"logs":[${uploads.map((item) => item.serializedWire).join(",")}]}`;
}

function utf8ByteLength(value: string): number {
  return utf8Encoder.encode(value).byteLength;
}

function mergeUploadReports(
  left: ApplicationLogUploadReport,
  right: ApplicationLogUploadReport,
): ApplicationLogUploadReport {
  return {
    uploaded: left.uploaded + right.uploaded,
    rejected: left.rejected + right.rejected,
    retried: left.retried + right.retried,
  };
}

function emptyReport(): ApplicationLogUploadReport {
  return { uploaded: 0, rejected: 0, retried: 0 };
}

const redactedPropertyKey = "[REDACTED_KEY]";

function isSensitiveKey(key: string): boolean {
  // 先 NFKC 折叠全角/兼容字符；仍含非 ASCII 的结构键无法可靠分类，统一 fail-closed。
  const canonical = key.normalize("NFKC");
  if (/[^\x00-\x7f]/u.test(canonical)) return true;
  const normalized = canonical
    .replaceAll(/[^A-Za-z0-9]/gu, "")
    .toLowerCase();
  return (
    normalized !== "authorizationmode" &&
    /authorization|token|password|pin|secret|apikey|credential|card|pan|cvv|voucher|cookie|header/i.test(
      normalized,
    )
  );
}

function sanitizeOptionalText(value: string | null | undefined): string | null {
  return value ? sanitizeText(value, 1_000) : null;
}

function sanitizeErrorType(error: unknown): string | null {
  return error instanceof Error ? sanitizeText(error.name, 240) : null;
}

function sanitizeErrorMessage(error: unknown): string | null {
  return error instanceof Error ? sanitizeText(error.message, 2_000) : null;
}

function sanitizeStack(error: unknown): string | null {
  return error instanceof Error && error.stack
    ? sanitizeText(error.stack, 8_000)
    : null;
}

export function sanitizeText(value: string, maxLength: number): string {
  // 必须先处理完整 JSON；预先截断会把合法敏感父对象变成非法片段并泄漏后续成员。
  return sanitizeTextAtDepth(value, 0).slice(0, maxLength);
}

const maxJsonSanitizeDepth = 16;
const maxJsonFragmentDepth = 32;

function sanitizeTextAtDepth(value: string, depth: number): string {
  return sanitizeFlatText(
    depth >= maxJsonSanitizeDepth ? value : sanitizeJsonFragments(value, depth),
  );
}

/**
 * 合法 JSON 既可能是整个日志，也可能嵌在普通诊断文字中。不能用单层正则替换父键，
 * 否则敏感对象/数组的后续成员仍会保留在输出里。
 */
function sanitizeJsonFragments(value: string, depth: number): string {
  const firstNonWhitespace = value.search(/\S/u);
  if (
    firstNonWhitespace >= 0 &&
    (value[firstNonWhitespace] === "{" || value[firstNonWhitespace] === "[")
  ) {
    const root = trySanitizeJsonFragment(value, depth);
    if (root !== null) return root;
  }

  // 一次扫描仅产出最外层候选，避免每个 `{`/`[` 都重复扫描到文本尾部。
  let result = "";
  let copiedUntil = 0;
  for (const candidate of scanJsonFragments(value)) {
    const end = candidate.end ?? value.length - 1;
    const fragment = value.slice(candidate.start, end + 1);
    const hasSensitiveParent =
      candidate.hasSensitivePrefix || hasSensitiveStructuredParent(fragment);
    // 前置 assignment 已确认敏感时优先整值替换；即使 `{...}` 自身可解析也不能绕过父键。
    const replacement = candidate.hasSensitivePrefix
      ? "[REDACTED]"
      : candidate.isTrusted
        ? (trySanitizeJsonFragment(fragment, depth) ??
          (hasSensitiveParent ? "[REDACTED]" : null))
        : hasSensitiveParent
          ? "[REDACTED]"
          : null;
    if (replacement === null) continue;
    result += value.slice(copiedUntil, candidate.start) + replacement;
    copiedUntil = end + 1;
  }
  return copiedUntil === 0 ? value : result + value.slice(copiedUntil);
}

function hasSensitiveStructuredParent(value: string): boolean {
  const pattern = /(["'])([^"'\r\n]{1,128})\1(\s*[:=]\s*)([\[{])/giu;
  for (let match = pattern.exec(value); match; match = pattern.exec(value)) {
    if (isSensitiveKey(match[2]!)) return true;
  }
  return false;
}

function hasSensitiveStructuredAssignmentPrefix(
  value: string,
  structureStart: number,
): boolean {
  // 逆向跳过任意空白和分隔符；键本身最多读取 128 字符，空白长度不能绕过。
  let cursor = structureStart - 1;
  while (cursor >= 0 && isAssignmentWhitespace(value[cursor]!)) cursor -= 1;
  if (value[cursor] !== ":" && value[cursor] !== "=") return false;
  cursor -= 1;
  while (cursor >= 0 && isAssignmentWhitespace(value[cursor]!)) cursor -= 1;
  // 已确认 assignment 但没有完整键，不能把结构交给平面回退。
  if (cursor < 0) return true;

  const quote = value[cursor];
  if (quote === '"' || quote === "'") {
    return hasSensitiveOrUntrustedQuotedAssignmentKey(value, cursor, quote);
  }

  return hasSensitiveOrUntrustedUnquotedAssignmentKey(value, cursor);
}

function hasSensitiveOrUntrustedQuotedAssignmentKey(
  value: string,
  keyEnd: number,
  quote: string,
): boolean {
  // 结束引号本身被转义时，不能把它当成一个可信的键结尾。
  if (isEscapedCharacter(value, keyEnd)) return true;

  let keyStart = keyEnd - 1;
  while (
    keyStart >= 0 &&
    keyEnd - keyStart <= 256 &&
    (value[keyStart] !== quote || isEscapedCharacter(value, keyStart))
  ) {
    keyStart -= 1;
  }
  if (
    keyStart < 0 ||
    keyEnd - keyStart > 256 ||
    !isTrustedAssignmentBoundary(value, keyStart - 1)
  ) {
    return true;
  }

  const key = decodeQuotedAssignmentKey(
    value.slice(keyStart, keyEnd + 1),
    quote,
  );
  // 引号、转义、外侧边界或解码后的键任一不完整，均不能让结构值回退到平面规则。
  return key === null || !isAssignmentKey(key) || isSensitiveKey(key);
}

function hasSensitiveOrUntrustedUnquotedAssignmentKey(
  value: string,
  keyEnd: number,
): boolean {
  // `响应片段: {...}` 是常见诊断标签，并非 assignment；仅纯非 ASCII 标签可走此分支。
  // 一旦混入 ASCII 键字符或不可信符号，仍按潜在键 fail-closed。
  if (
    !isAssignmentKeyCharacter(value[keyEnd]!) &&
    isPlainTextStructuredLabel(value, keyEnd)
  ) {
    return false;
  }

  let keyStart = keyEnd;
  let keyLength = 0;
  while (keyStart >= 0 && !isTrustedAssignmentBoundary(value, keyStart)) {
    if (keyLength >= 128 || !isAssignmentKeyCharacter(value[keyStart]!)) {
      return true;
    }
    keyStart -= 1;
    keyLength += 1;
  }

  if (keyLength === 0) return true;
  const key = value.slice(keyStart + 1, keyEnd + 1);
  return !isAssignmentKey(key) || isSensitiveKey(key);
}

function isPlainTextStructuredLabel(value: string, labelEnd: number): boolean {
  let cursor = labelEnd;
  let sawNonAscii = false;
  let length = 0;
  while (cursor >= 0 && !isTrustedAssignmentBoundary(value, cursor)) {
    const character = value[cursor]!;
    if (
      length >= 128 ||
      isAssignmentKeyCharacter(character) ||
      character <= "\x1f" ||
      "/@#'\"=:".includes(character)
    ) {
      return false;
    }
    sawNonAscii ||= character > "\x7f";
    cursor -= 1;
    length += 1;
  }
  return sawNonAscii;
}

function isAssignmentWhitespace(character: string): boolean {
  return (
    character === " " ||
    character === "\t" ||
    character === "\r" ||
    character === "\n"
  );
}

function isAssignmentKeyCharacter(character: string): boolean {
  return /[A-Za-z0-9_.-]/u.test(character);
}

function isAssignmentKey(value: string): boolean {
  return value.length > 0 && [...value].every(isAssignmentKeyCharacter);
}

function isTrustedAssignmentBoundary(value: string, index: number): boolean {
  if (index < 0) return true;
  const character = value[index]!;
  return isAssignmentWhitespace(character) || ",[;|([{".includes(character);
}

function isEscapedCharacter(value: string, index: number): boolean {
  let slashCount = 0;
  for (
    let cursor = index - 1;
    cursor >= 0 && value[cursor] === "\\";
    cursor -= 1
  ) {
    slashCount += 1;
  }
  return slashCount % 2 === 1;
}

function decodeQuotedAssignmentKey(
  value: string,
  quote: string,
): string | null {
  try {
    if (
      value.length < 2 ||
      value[0] !== quote ||
      value[value.length - 1] !== quote ||
      isEscapedCharacter(value, value.length - 1)
    ) {
      return null;
    }
    if (quote === '"') {
      const parsed: unknown = JSON.parse(value);
      return typeof parsed === "string" && parsed.length <= 128 ? parsed : null;
    }
    let result = "";
    for (let index = 1; index < value.length - 1; index += 1) {
      if (value[index] !== "\\") {
        result += value[index];
        continue;
      }
      const escape = value[index + 1];
      if (!escape) return null;
      if (escape === "u") {
        const hex = value.slice(index + 2, index + 6);
        if (!/^[0-9A-Fa-f]{4}$/u.test(hex)) return null;
        result += String.fromCharCode(Number.parseInt(hex, 16));
        index += 5;
      } else {
        const decodedEscape: Record<string, string> = {
          "\\": "\\",
          "'": "'",
          '"': '"',
          b: "\b",
          f: "\f",
          n: "\n",
          r: "\r",
          t: "\t",
        };
        if (!(escape in decodedEscape)) return null;
        result += decodedEscape[escape]!;
        index += 1;
      }
    }
    return result.length <= 128 ? result : null;
  } catch {
    return null;
  }
}

function trySanitizeJsonFragment(value: string, depth: number): string | null {
  try {
    const parsed: unknown = JSON.parse(value);
    return JSON.stringify(sanitizeJsonValue(parsed, depth));
  } catch {
    // 非法片段沿用自由文本脱敏；日志旁路不能因解析失败抛出。
    return null;
  }
}

function sanitizeJsonValue(value: unknown, depth: number): unknown {
  // 超过递归上限时整体替换该分支，优先保证日志不会泄漏深层敏感内容。
  if (depth >= maxJsonSanitizeDepth) return "[REDACTED]";
  if (Array.isArray(value))
    return value.map((item) => sanitizeJsonValue(item, depth + 1));
  if (value && typeof value === "object") {
    return Object.fromEntries(
      Object.entries(value).map(([key, item]) => {
        const sensitive = isSensitiveKey(key);
        return [
          sensitive ? redactedPropertyKey : sanitizeText(key, 1_000),
          sensitive ? "[REDACTED]" : sanitizeJsonValue(item, depth + 1),
        ];
      }),
    );
  }
  return typeof value === "string"
    ? sanitizeTextAtDepth(value, depth + 1)
    : value;
}

function scanJsonFragments(value: string): readonly Readonly<{
  start: number;
  end: number | null;
  isTrusted: boolean;
  hasSensitivePrefix: boolean;
}>[] {
  const candidates: {
    start: number;
    end: number | null;
    isTrusted: boolean;
    hasSensitivePrefix: boolean;
  }[] = [];
  const closers: string[] = [];
  let start = -1;
  let quoted = false;
  let escaped = false;
  let overflowDepth = 0;
  let isTrusted = true;
  let hasSensitivePrefix = false;
  for (let index = 0; index < value.length; index += 1) {
    const character = value[index]!;
    if (quoted) {
      if (escaped) escaped = false;
      else if (character === "\\") escaped = true;
      else if (character === '"') quoted = false;
      continue;
    }
    if (character === '"' && closers.length > 0) {
      quoted = true;
    } else if (character === "{" || character === "[") {
      const isSensitivePrefix = hasSensitiveStructuredAssignmentPrefix(
        value,
        index,
      );
      if (closers.length === 0) {
        start = index;
        hasSensitivePrefix = isSensitivePrefix;
      } else {
        hasSensitivePrefix ||= isSensitivePrefix;
      }
      if (closers.length >= maxJsonFragmentDepth) {
        overflowDepth += 1;
        isTrusted = false;
      } else {
        closers.push(character === "{" ? "}" : "]");
      }
    } else if (character === "}" || character === "]") {
      if (closers.length === 0) continue;
      if (overflowDepth > 0) {
        overflowDepth -= 1;
        continue;
      }
      if (closers.pop() !== character) {
        // 括号类型错配后不再把后续子对象当独立可信 JSON，防止脱离敏感父键泄漏。
        candidates.push({
          start,
          end: null,
          isTrusted: false,
          hasSensitivePrefix,
        });
        return candidates;
      }
      if (closers.length === 0) {
        candidates.push({ start, end: index, isTrusted, hasSensitivePrefix });
        start = -1;
        isTrusted = true;
        hasSensitivePrefix = false;
      }
    }
  }
  if (closers.length > 0)
    candidates.push({ start, end: null, isTrusted: false, hasSensitivePrefix });
  return candidates;
}

function sanitizeQuotedAssignmentValues(value: string): string {
  const pattern = /\b([A-Za-z][A-Za-z0-9_.-]{0,127})(\s*[:=]\s*)(["'])/giu;
  let result = "";
  let copiedUntil = 0;
  for (let match = pattern.exec(value); match; match = pattern.exec(value)) {
    const key = match[1]!;
    if (!isSensitiveKey(key)) continue;

    const quote = match[3]!;
    const valueStart = match.index + match[0].length;
    const boundary = findQuotedValueBoundary(value, valueStart, quote);
    result +=
      value.slice(copiedUntil, valueStart) + `[REDACTED]${quote}`;
    copiedUntil = boundary.closed ? boundary.end + 1 : boundary.end;
    // 未闭合值只吃到当前行；下一行仍需继续扫描其他独立 assignment。
    pattern.lastIndex = copiedUntil;
  }
  return copiedUntil === 0 ? value : result + value.slice(copiedUntil);
}

function findQuotedValueBoundary(
  value: string,
  valueStart: number,
  quote: string,
): Readonly<{ end: number; closed: boolean }> {
  for (let index = valueStart; index < value.length; index += 1) {
    const character = value[index]!;
    if (character === "\r" || character === "\n") {
      return { end: index, closed: false };
    }
    if (character === "\\") {
      // 引号前奇数个反斜杠属于值；成对反斜杠后的引号仍可正常闭合。
      index += 1;
      continue;
    }
    if (character === quote) return { end: index, closed: true };
  }
  return { end: value.length, closed: false };
}

function sanitizeFlatText(value: string): string {
  return (
    sanitizeQuotedAssignmentValues(value)
      // JSON 和类似 JSON 的诊断文本需要保留引号结构；敏感键统一复用属性白名单判断。
      .replace(
        /((["'])([^"'\r\n]{1,128})\2\s*[:=]\s*(["']))(?:bearer\s+)?[^"'\r\n]*\4/giu,
        (
          match,
          prefix: string,
          keyQuote: string,
          key: string,
          valueQuote: string,
        ) =>
          shouldRedactQuotedKey(key, keyQuote)
            ? `${prefix}[REDACTED]${valueQuote}`
            : match,
      )
      .replace(
        /(["'])([^"'\r\n]{1,128})\1(\s*[:=]\s*)(?!["'])[^,}\]\s;]+/giu,
        (match, keyQuote: string, key: string, separator: string) =>
          shouldRedactQuotedKey(key, keyQuote)
            ? `${keyQuote}${key}${keyQuote}${separator}${keyQuote}[REDACTED]${keyQuote}`
            : match,
      )
      .replace(/(bearer\s+)[^\s,;]+/giu, "$1[REDACTED]")
      .replace(
        /\b([A-Za-z][A-Za-z0-9_.-]{0,127})(\s*[:=]\s*)(?:bearer\s+)?(?!["'])[^\s,;]+/giu,
        (match, key: string, separator: string) =>
          isSensitiveKey(key) ? `${key}${separator}[REDACTED]` : match,
      )
      .replace(
        /([?&])([A-Za-z][A-Za-z0-9_.-]{0,127})(=)[^&#\s]+/giu,
        (match, prefix: string, key: string, separator: string) =>
          isSensitiveKey(key) ? `${prefix}${key}${separator}[REDACTED]` : match,
      )
      .replace(/\b(?:\d[ -]?){12,18}\d\b/gu, "[REDACTED]")
  );
}

function shouldRedactQuotedKey(key: string, quote: string): boolean {
  const decoded = decodeQuotedAssignmentKey(`${quote}${key}${quote}`, quote);
  return (
    decoded === null || !isAssignmentKey(decoded) || isSensitiveKey(decoded)
  );
}

function isSafeIngestUrl(value: string | null): value is string {
  if (!value) return false;
  try {
    const parsed = new URL(value);
    return (
      parsed.protocol === "https:" &&
      !parsed.username &&
      !parsed.password &&
      !parsed.search &&
      !parsed.hash
    );
  } catch {
    return false;
  }
}
