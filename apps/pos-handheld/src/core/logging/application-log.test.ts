import assert from "node:assert/strict";
import test from "node:test";

import {
  ApplicationLogger,
  ApplicationLogRuntime,
  ApplicationLogUploader,
  resolveApplicationLogCenterConfig,
  sanitizeProperties,
  sanitizeText,
  SqliteApplicationLogOutbox,
  type ApplicationLogDeliveryEntry,
  type ApplicationLogEntry,
  type ApplicationLogOutboxPort,
} from "./application-log";

function entry(
  clientEventId: string,
  attemptCount = 0,
): ApplicationLogDeliveryEntry {
  return {
    clientEventId,
    timestampUtc: "2026-07-31T00:00:00.000Z",
    level: "Error",
    message: "network failed",
    category: "sync",
    traceId: "trace-1",
    exceptionType: "TypeError",
    exceptionMessage: "request token=secret failed",
    stackTrace: "TypeError: request token=secret failed",
    storeCode: "S1",
    deviceCode: "IPAD-1",
    userId: "cashier-1",
    userName: "Alice",
    appVersion: "1",
    instanceId: "install-1",
    properties: { authorizationMode: "supervisor" },
    attemptCount,
  };
}

function testUuid(sequence: number): string {
  return `00000000-0000-4000-8000-${sequence.toString().padStart(12, "0")}`;
}

function applicationLogBoundaryFixture(
  targetBytes: number,
  sequence: number,
): Readonly<{
  entry: ApplicationLogDeliveryEntry;
  expectedWire: Readonly<Record<string, unknown>>;
}> {
  const clientEventId = testUuid(sequence);
  const baseEntry: ApplicationLogDeliveryEntry = {
    ...entry(clientEventId),
    timestampUtc: "2026-08-01T10:20:30Z",
    message: "m",
    category: null,
    traceId: null,
    exceptionType: null,
    exceptionMessage: null,
    stackTrace: null,
    storeCode: "",
    deviceCode: "",
    userId: null,
    userName: null,
    appVersion: null,
    instanceId: null,
    properties: null,
  };
  const toExpectedWire = (
    storeCode: string,
    deviceCode: string,
  ): Readonly<Record<string, unknown>> => ({
    level: "Error",
    message: "m",
    timestampUtc: "2026-08-01T10:20:30Z",
    projectCode: "hbpos_handheld",
    environment: "production",
    sourceType: "POS",
    serviceName: "Hbpos.Client.Handheld",
    instanceId: null,
    clientEventId,
    storeCode,
    deviceCode,
    appVersion: null,
    category: null,
    eventId: null,
    traceId: null,
    requestPath: null,
    requestMethod: null,
    statusCode: null,
    userId: null,
    userName: null,
    clientIp: null,
    exceptionType: null,
    exceptionMessage: null,
    stackTrace: null,
    properties: null,
  });
  const emptyWireBytes = new TextEncoder().encode(
    JSON.stringify(toExpectedWire("", "")),
  ).byteLength;
  const fillerBytes = targetBytes - emptyWireBytes;
  const storeCodeBytes = Math.min(32 * 1024, fillerBytes);
  const deviceCodeBytes = fillerBytes - storeCodeBytes;
  assert.ok(storeCodeBytes >= 0 && storeCodeBytes <= 32 * 1024);
  assert.ok(deviceCodeBytes >= 0 && deviceCodeBytes <= 32 * 1024);

  const storeCode = "s".repeat(storeCodeBytes);
  const deviceCode = "d".repeat(deviceCodeBytes);
  const expectedWire = toExpectedWire(storeCode, deviceCode);
  assert.equal(
    new TextEncoder().encode(JSON.stringify(expectedWire)).byteLength,
    targetBytes,
  );
  return {
    entry: { ...baseEntry, storeCode, deviceCode },
    expectedWire,
  };
}

class FakeOutbox implements ApplicationLogOutboxPort {
  public enqueued: ApplicationLogEntry[] = [];
  public accepted: string[] = [];
  public rejected: { eventId: string; code: string }[] = [];
  public retried: {
    eventIds: readonly string[];
    nextAttemptAtIso: string;
    errorCode: string;
  }[] = [];
  public listError: Error | null = null;
  public markError: Error | null = null;
  public retryError: Error | null = null;
  public constructor(
    private readonly entries: readonly ApplicationLogDeliveryEntry[],
  ) {}
  public async enqueue(entry: ApplicationLogEntry): Promise<void> {
    this.enqueued.push(entry);
  }
  public async listReady(): Promise<readonly ApplicationLogDeliveryEntry[]> {
    if (this.listError) throw this.listError;
    return this.entries;
  }
  public async markAccepted(eventIds: readonly string[]): Promise<void> {
    if (this.markError) throw this.markError;
    this.accepted.push(...eventIds);
  }
  public async markRejected(
    entries: readonly Readonly<{ eventId: string; code: string }>[],
  ): Promise<void> {
    if (this.markError) throw this.markError;
    this.rejected.push(...entries);
  }
  public async releaseRetry(
    eventIds: readonly string[],
    nextAttemptAtIso: string,
    errorCode: string,
  ): Promise<void> {
    if (this.retryError) throw this.retryError;
    this.retried.push({ eventIds, nextAttemptAtIso, errorCode });
  }
}

const configuration = {
  enabled: true,
  ingestUrl: "https://logs.example.test/api/system/logs/ingest",
  writeKey: "write-only",
  environment: "production",
} as const;

test("程序日志按 ApplicationLogIngestItemDto 白名单发送真实 ApiResponse envelope 所需字段", async () => {
  const outbox = new FakeOutbox([entry("a")]);
  let body: unknown;
  const uploader = new ApplicationLogUploader(
    outbox,
    configuration,
    async (_url, request) => {
      body = JSON.parse(String(request?.body));
      return new Response(
        JSON.stringify({
          success: true,
          data: { results: [{ clientEventId: "a", status: "accepted" }] },
        }),
        { status: 200 },
      );
    },
  );

  assert.deepEqual(await uploader.flush(), {
    uploaded: 1,
    rejected: 0,
    retried: 0,
  });
  assert.deepEqual(body, {
    logs: [
      {
        level: "Error",
        message: "network failed",
        timestampUtc: "2026-07-31T00:00:00Z",
        projectCode: "hbpos_handheld",
        environment: "production",
        sourceType: "POS",
        serviceName: "Hbpos.Client.Handheld",
        instanceId: "install-1",
        clientEventId: "a",
        storeCode: "S1",
        deviceCode: "IPAD-1",
        appVersion: "1",
        category: "sync",
        eventId: null,
        traceId: "trace-1",
        requestPath: null,
        requestMethod: null,
        statusCode: null,
        userId: "cashier-1",
        userName: "Alice",
        clientIp: null,
        exceptionType: "TypeError",
        exceptionMessage: "request token=[REDACTED] failed",
        stackTrace: "TypeError: request token=[REDACTED] failed",
        properties: { authorizationMode: "supervisor" },
      },
    ],
  });
  assert.doesNotMatch(JSON.stringify(body), /cashierId|"exception"/u);
});

test("程序日志 wire 按后端 Guid 与 DateTime canonical 格式发送并匹配回执", async () => {
  const originalEventId = "ABCDEFAB-CDEF-4ABC-8DEF-ABCDEFABCDEF";
  const canonicalEventId = originalEventId.toLowerCase();
  const outbox = new FakeOutbox([
    {
      ...entry(originalEventId),
      timestampUtc: "2026-08-01T10:20:30.1200000+10:00",
    },
  ]);
  let wire: { clientEventId: string; timestampUtc: string } | undefined;
  const uploader = new ApplicationLogUploader(
    outbox,
    configuration,
    async (_url, request) => {
      const body = JSON.parse(String(request?.body)) as {
        logs: { clientEventId: string; timestampUtc: string }[];
      };
      wire = body.logs[0];
      return new Response(
        JSON.stringify({
          data: {
            results: [
              { clientEventId: canonicalEventId, status: "accepted" },
            ],
          },
        }),
        { status: 200 },
      );
    },
  );

  assert.deepEqual(await uploader.flush(), {
    uploaded: 1,
    rejected: 0,
    retried: 0,
  });
  assert.equal(wire?.clientEventId, canonicalEventId);
  assert.equal(wire?.timestampUtc, "2026-08-01T00:20:30.12Z");
  assert.deepEqual(outbox.accepted, [originalEventId]);
});

test("程序日志逐项确认 accepted/duplicate/rejected，缺失回执仅重试未确认项", async () => {
  const outbox = new FakeOutbox([entry("a"), entry("b"), entry("c")]);
  const uploader = new ApplicationLogUploader(
    outbox,
    configuration,
    async () =>
      new Response(
        JSON.stringify({
          success: true,
          data: {
            results: [
              { clientEventId: "a", status: "accepted" },
              { clientEventId: "b", status: "rejected", errorCode: "INVALID" },
            ],
          },
        }),
        { status: 200 },
      ),
    () => new Date("2026-07-31T00:00:00.000Z"),
    () => 0.5,
  );

  assert.deepEqual(await uploader.flush(), {
    uploaded: 1,
    rejected: 1,
    retried: 1,
  });
  assert.deepEqual(outbox.accepted, ["a"]);
  assert.deepEqual(outbox.rejected, [{ eventId: "b", code: "INVALID" }]);
  assert.deepEqual(outbox.retried, [
    {
      eventIds: ["c"],
      nextAttemptAtIso: "2026-07-31T00:01:07.500Z",
      errorCode: "LOG_ACK_INCOMPLETE",
    },
  ]);
});

test("程序日志 wire 保留异常消息约 2k、堆栈约 8k，并继续二次脱敏", async () => {
  const outbox = new FakeOutbox([
    {
      ...entry("long-exception"),
      exceptionMessage: `token=secret ${"m".repeat(2_100)}`,
      stackTrace: `token=secret ${"s".repeat(8_100)}`,
    },
  ]);
  let body: any;
  const uploader = new ApplicationLogUploader(
    outbox,
    configuration,
    async (_url, request) => {
      body = JSON.parse(String(request?.body));
      return new Response(
        JSON.stringify({
          success: true,
          data: {
            results: [{ clientEventId: "long-exception", status: "accepted" }],
          },
        }),
        { status: 200 },
      );
    },
  );

  await uploader.flush();

  const wire = body.logs[0];
  assert.equal(wire.exceptionMessage.length, 2_000);
  assert.equal(wire.stackTrace.length, 8_000);
  assert.doesNotMatch(
    `${wire.exceptionMessage}\n${wire.stackTrace}`,
    /token=secret/u,
  );
});

test("程序日志上传前按 UTF-8 隔离超字段和超单条日志，并继续发送后续有效项", async () => {
  const oversizedField = {
    ...entry("oversized-field"),
    // 10_923 个中文字符是 32_769 UTF-8 字节，刚好超过服务端 32 KiB 字段预算。
    storeCode: "界".repeat(10_923),
  };
  const oversizedItem = {
    ...entry("oversized-item"),
    message: "界".repeat(8_000),
    exceptionMessage: "界".repeat(2_000),
    stackTrace: "界".repeat(8_000),
    properties: Object.fromEntries(
      Array.from({ length: 5 }, (_, index) => [
        `context${index}`,
        "界".repeat(1_000),
      ]),
    ),
  };
  const oversizedEscapedItem = {
    ...entry("oversized-escaped-item"),
    // 解码后字段只有 11 KiB，但 JSON.stringify 会扩成 66 KiB，必须按最终 wire 隔离。
    storeCode: "\u0000".repeat(11 * 1024),
  };
  const outbox = new FakeOutbox([
    oversizedField,
    entry("valid-after-field"),
    oversizedItem,
    entry("valid-after-item"),
    oversizedEscapedItem,
    entry("valid-after-escaped-item"),
  ]);
  let sentIds: string[] = [];
  const uploader = new ApplicationLogUploader(
    outbox,
    configuration,
    async (_url, request) => {
      const body = JSON.parse(String(request?.body)) as {
        logs: { clientEventId: string }[];
      };
      sentIds = body.logs.map((item) => item.clientEventId);
      return new Response(
        JSON.stringify({
          success: true,
          data: {
            results: body.logs.map((item) => ({
              clientEventId: item.clientEventId,
              status: "accepted",
            })),
          },
        }),
        { status: 200 },
      );
    },
  );

  assert.deepEqual(await uploader.flush(), {
    uploaded: 3,
    rejected: 3,
    retried: 0,
  });
  assert.deepEqual(sentIds, [
    "valid-after-field",
    "valid-after-item",
    "valid-after-escaped-item",
  ]);
  assert.deepEqual(outbox.rejected, [
    { eventId: "oversized-field", code: "LOG_PAYLOAD_TOO_LARGE" },
    { eventId: "oversized-item", code: "LOG_PAYLOAD_TOO_LARGE" },
    {
      eventId: "oversized-escaped-item",
      code: "LOG_PAYLOAD_TOO_LARGE",
    },
  ]);
  assert.deepEqual(outbox.retried, []);
});

test("程序日志按后端 canonical JSON 精确执行 64 KiB 单条边界", async () => {
  const fixtures = [65_535, 65_536, 65_537].map((targetBytes, index) => ({
    targetBytes,
    ...applicationLogBoundaryFixture(targetBytes, 401 + index),
  }));
  const outbox = new FakeOutbox(fixtures.map((fixture) => fixture.entry));
  let sentLogs: Readonly<Record<string, unknown>>[] = [];
  const uploader = new ApplicationLogUploader(
    outbox,
    configuration,
    async (_url, request) => {
      const body = JSON.parse(String(request?.body)) as {
        logs: Readonly<Record<string, unknown>>[];
      };
      sentLogs = body.logs;
      return new Response(
        JSON.stringify({
          data: {
            results: body.logs.map((item) => ({
              clientEventId: item.clientEventId,
              status: "accepted",
            })),
          },
        }),
        { status: 200 },
      );
    },
  );

  assert.deepEqual(await uploader.flush(), {
    uploaded: 2,
    rejected: 1,
    retried: 0,
  });
  assert.deepEqual(sentLogs, fixtures.slice(0, 2).map((item) => item.expectedWire));
  assert.deepEqual(
    sentLogs.map(
      (item) => new TextEncoder().encode(JSON.stringify(item)).byteLength,
    ),
    [65_535, 65_536],
  );
  assert.deepEqual(outbox.rejected, [
    {
      eventId: fixtures[2]!.entry.clientEventId,
      code: "LOG_PAYLOAD_TOO_LARGE",
    },
  ]);
});

test("程序日志按服务端 1 MiB 最终批次 JSON 预算选择 FIFO 前缀，未发送尾部保持 pending", async () => {
  const entries = [
    ...Array.from({ length: 99 }, (_, index) => ({
      ...entry(`batch-${index.toString().padStart(3, "0")}`),
      message: "m".repeat(8_000),
      stackTrace: "s".repeat(8_000),
    })),
    {
      ...entry("oversized-after-capacity-boundary"),
      storeCode: "界".repeat(10_923),
    },
  ];
  const outbox = new FakeOutbox(entries);
  let sentIds: string[] = [];
  const uploader = new ApplicationLogUploader(
    outbox,
    configuration,
    async (_url, request) => {
      const body = JSON.parse(String(request?.body)) as {
        logs: { clientEventId: string }[];
      };
      sentIds = body.logs.map((item) => item.clientEventId);
      return new Response(
        JSON.stringify({
          data: {
            results: body.logs.map((item) => ({
              clientEventId: item.clientEventId,
              status: "accepted",
            })),
          },
        }),
        { status: 200 },
      );
    },
  );

  const report = await uploader.flush();

  assert.ok(sentIds.length > 0 && sentIds.length < entries.length);
  assert.deepEqual(
    sentIds,
    entries.slice(0, sentIds.length).map((item) => item.clientEventId),
  );
  assert.deepEqual(report, {
    uploaded: sentIds.length,
    rejected: 0,
    retried: 0,
  });
  assert.deepEqual(outbox.rejected, []);
  assert.deepEqual(outbox.retried, []);
});

test("程序日志按最终 JSON 选择不超过 1 MiB 的批次，请求体自然低于 4 MiB", async () => {
  const entries = Array.from({ length: 17 }, (_, index) => ({
    ...entry(`escaped-${index.toString().padStart(2, "0")}`),
    // 单条序列化后约 63 KiB，17 条超过 1 MiB；解码字段仍低于 32 KiB。
    storeCode: "\u0000".repeat(10_500),
  }));
  const outbox = new FakeOutbox(entries);
  let requestBytes = 0;
  let sentIds: string[] = [];
  const uploader = new ApplicationLogUploader(
    outbox,
    configuration,
    async (_url, request) => {
      const serialized = String(request?.body);
      requestBytes = new TextEncoder().encode(serialized).byteLength;
      const body = JSON.parse(serialized) as {
        logs: { clientEventId: string }[];
      };
      sentIds = body.logs.map((item) => item.clientEventId);
      return new Response(
        JSON.stringify({
          data: {
            results: body.logs.map((item) => ({
              clientEventId: item.clientEventId,
              status: "accepted",
            })),
          },
        }),
        { status: 200 },
      );
    },
  );

  await uploader.flush();

  assert.ok(requestBytes <= 1024 * 1024);
  assert.ok(requestBytes <= 4 * 1024 * 1024);
  assert.ok(sentIds.length > 0 && sentIds.length < entries.length);
  assert.deepEqual(
    sentIds,
    entries.slice(0, sentIds.length).map((item) => item.clientEventId),
  );
  assert.deepEqual(outbox.retried, []);
});

test("程序日志遇到 HTTP 400/413 时二分隔离坏项，正常项仍按 ACK 成功", async () => {
  for (const status of [400, 413]) {
    const outbox = new FakeOutbox([
      entry(`good-left-${status}`),
      entry(`bad-${status}`),
      entry(`good-right-${status}`),
    ]);
    const requests: string[][] = [];
    const uploader = new ApplicationLogUploader(
      outbox,
      configuration,
      async (_url, request) => {
        const body = JSON.parse(String(request?.body)) as {
          logs: { clientEventId: string }[];
        };
        const ids = body.logs.map((item) => item.clientEventId);
        requests.push(ids);
        if (ids.some((id) => id === `bad-${status}`)) {
          return new Response(null, { status });
        }
        return new Response(
          JSON.stringify({
            data: {
              results: ids.map((clientEventId) => ({
                clientEventId,
                status: "accepted",
              })),
            },
          }),
          { status: 200 },
        );
      },
    );

    assert.deepEqual(await uploader.flush(), {
      uploaded: 2,
      rejected: 1,
      retried: 0,
    });
    assert.deepEqual(outbox.accepted, [
      `good-left-${status}`,
      `good-right-${status}`,
    ]);
    assert.deepEqual(outbox.rejected, [
      { eventId: `bad-${status}`, code: `LOG_HTTP_${status}` },
    ]);
    assert.deepEqual(outbox.retried, []);
    assert.ok(requests.length > 1);
    assert.ok(requests.every((ids) => ids.length > 0));
  }
});

test("100 条无法解析的 pending 程序日志会隔离，后续有效日志不被饿死", async () => {
  const validEventId = testUuid(101);
  const rows = [
    ...Array.from({ length: 100 }, (_, index) => ({
      eventId: `poison-${index}`,
      payload: "{not-json",
      attemptCount: 0,
      state: "pending",
    })),
    {
      eventId: validEventId,
      payload: JSON.stringify(entry(validEventId)),
      attemptCount: 0,
      state: "pending",
    },
  ];
  const database = {
    async run(sql: string, parameters?: readonly unknown[]) {
      if (sql.includes("SET delivery_state = 'rejected'")) {
        const eventId = parameters?.[1];
        const row = rows.find((candidate) => candidate.eventId === eventId);
        if (row) row.state = "rejected";
      }
      return { changes: 1, lastInsertRowId: 0 };
    },
    async getAll(_sql: string, parameters?: readonly unknown[]) {
      const limit = Number(parameters?.[1] ?? 100);
      return rows
        .filter((row) => row.state === "pending")
        .slice(0, limit)
        .map((row) => ({
          event_id: row.eventId,
          payload_json: row.payload,
          attempt_count: row.attemptCount,
        }));
    },
  };
  const outbox = new SqliteApplicationLogOutbox(
    database as never,
    () => "2026-08-01T00:00:00.000Z",
  );

  const ready = await outbox.listReady(100);

  assert.deepEqual(
    ready.map((item) => item.clientEventId),
    [validEventId],
  );
  assert.equal(
    rows.filter((row) => row.state === "rejected").length,
    100,
  );
});

test("结构可解析但不符合 ApplicationLogEntry wire 形状的行会隔离", async () => {
  const malformed = [
    { ...entry(testUuid(201)), exceptionMessage: 7 },
    { ...entry(testUuid(202)), level: "Verbose" },
    { ...entry(testUuid(203)), category: 7 },
    { ...entry(testUuid(204)), properties: { safe: 7 } },
    { ...entry(testUuid(205)), properties: "not-a-record" },
    { ...entry(testUuid(206)), clientEventId: "not-a-uuid" },
    { ...entry(testUuid(207)), timestampUtc: "2026-07-31" },
  ];
  const validEventId = testUuid(208);
  const rows = [
    ...malformed.map((payload) => ({
      eventId: payload.clientEventId,
      payload: JSON.stringify(payload),
      state: "pending",
    })),
    {
      eventId: validEventId,
      payload: JSON.stringify({
        ...entry(validEventId),
        timestampUtc: "2026-08-01T10:00:00.000+10:00",
      }),
      state: "pending",
    },
  ];
  const database = {
    async run(sql: string, parameters?: readonly unknown[]) {
      if (sql.includes("SET delivery_state = 'rejected'")) {
        const row = rows.find((item) => item.eventId === parameters?.[1]);
        if (row) row.state = "rejected";
      }
      return { changes: 1, lastInsertRowId: 0 };
    },
    async getAll(_sql: string, parameters?: readonly unknown[]) {
      const limit = Number(parameters?.[1] ?? 100);
      return rows
        .filter((row) => row.state === "pending")
        .slice(0, limit)
        .map((row) => ({
          event_id: row.eventId,
          payload_json: row.payload,
          attempt_count: 0,
        }));
    },
  };
  const outbox = new SqliteApplicationLogOutbox(
    database as never,
    () => "2026-08-01T00:00:00.000Z",
  );

  const ready = await outbox.listReady(100);

  assert.deepEqual(
    ready.map((item) => item.clientEventId),
    [validEventId],
  );
  assert.deepEqual(
    rows
      .filter((row) => row.state === "rejected")
      .map((row) => row.eventId),
    malformed.map((item) => item.clientEventId),
  );
});

test("程序日志补扫使用剩余容量，并发清理补行也不会超过 listReady limit", async () => {
  const beforeChangeId = testUuid(301);
  const afterChangeId1 = testUuid(302);
  const afterChangeId2 = testUuid(303);
  const rows = [
    {
      eventId: "invalid-first",
      payload: "{not-json",
      state: "pending",
    },
    {
      eventId: beforeChangeId,
      payload: JSON.stringify(entry(beforeChangeId)),
      state: "pending",
    },
  ];
  const queryLimits: number[] = [];
  let concurrentChangeApplied = false;
  const database = {
    async run(sql: string, parameters?: readonly unknown[]) {
      if (sql.includes("SET delivery_state = 'rejected'")) {
        const rejected = rows.find((row) => row.eventId === parameters?.[1]);
        if (rejected) rejected.state = "rejected";
        if (!concurrentChangeApplied) {
          concurrentChangeApplied = true;
          const priorReady = rows.find(
            (row) => row.eventId === beforeChangeId,
          );
          if (priorReady) priorReady.state = "accepted-elsewhere";
          rows.push(
            {
              eventId: afterChangeId1,
              payload: JSON.stringify(entry(afterChangeId1)),
              state: "pending",
            },
            {
              eventId: afterChangeId2,
              payload: JSON.stringify(entry(afterChangeId2)),
              state: "pending",
            },
          );
        }
      }
      return { changes: 1, lastInsertRowId: 0 };
    },
    async getAll(_sql: string, parameters?: readonly unknown[]) {
      const limit = Number(parameters?.[1] ?? 100);
      queryLimits.push(limit);
      return rows
        .filter((row) => row.state === "pending")
        .slice(0, limit)
        .map((row) => ({
          event_id: row.eventId,
          payload_json: row.payload,
          attempt_count: 0,
        }));
    },
  };
  const outbox = new SqliteApplicationLogOutbox(
    database as never,
    () => "2026-08-01T00:00:00.000Z",
  );

  const ready = await outbox.listReady(2);

  assert.deepEqual(
    ready.map((item) => item.clientEventId),
    [beforeChangeId, afterChangeId1],
  );
  assert.deepEqual(queryLimits, [2, 1]);
});

test("程序日志退避使用 attemptCount；401/403 至少 30 分钟，429 尊重 Retry-After", async () => {
  const networkOutbox = new FakeOutbox([entry("retry", 2)]);
  const now = () => new Date("2026-07-31T00:00:00.000Z");
  const networkUploader = new ApplicationLogUploader(
    networkOutbox,
    configuration,
    async () => {
      throw new Error("offline");
    },
    now,
    () => 0.5,
  );
  await networkUploader.flush();
  assert.deepEqual(networkOutbox.retried[0], {
    eventIds: ["retry"],
    nextAttemptAtIso: "2026-07-31T00:05:07.500Z",
    errorCode: "LOG_NETWORK_FAILURE",
  });

  const authOutbox = new FakeOutbox([entry("auth")]);
  const authUploader = new ApplicationLogUploader(
    authOutbox,
    configuration,
    async () => new Response(null, { status: 401 }),
    now,
    () => 0.5,
  );
  await authUploader.flush();
  assert.deepEqual(authOutbox.retried[0], {
    eventIds: ["auth"],
    nextAttemptAtIso: "2026-07-31T00:30:00.000Z",
    errorCode: "LOG_HTTP_401",
  });

  const limitedOutbox = new FakeOutbox([entry("limited")]);
  const limitedUploader = new ApplicationLogUploader(
    limitedOutbox,
    configuration,
    async () =>
      new Response(null, {
        status: 429,
        headers: { "Retry-After": "7200" },
      }),
    now,
    () => 0.5,
  );
  await limitedUploader.flush();
  assert.deepEqual(limitedOutbox.retried[0], {
    eventIds: ["limited"],
    nextAttemptAtIso: "2026-07-31T02:00:00.000Z",
    errorCode: "LOG_HTTP_429",
  });
});

test("程序日志失败路径、无效配置与并发 flush 都是旁路且不抛出", async () => {
  const invalid = resolveApplicationLogCenterConfig({
    enabled: true,
    ingestUrl: "not-a-url",
    writeKey: "write-only",
    environment: "production",
  });
  assert.equal(invalid.enabled, false);

  const unavailable = new FakeOutbox([entry("a")]);
  unavailable.listError = new Error("sqlite unavailable");
  const safeUploader = new ApplicationLogUploader(unavailable, configuration);
  await assert.doesNotReject(safeUploader.flush());

  const writeFailure = new FakeOutbox([entry("b")]);
  writeFailure.markError = new Error("sqlite mark unavailable");
  const writeUploader = new ApplicationLogUploader(
    writeFailure,
    configuration,
    async () =>
      new Response(
        JSON.stringify({
          success: true,
          data: { results: [{ clientEventId: "b", status: "accepted" }] },
        }),
        { status: 200 },
      ),
  );
  await assert.doesNotReject(writeUploader.flush());

  const retryFailure = new FakeOutbox([entry("c")]);
  retryFailure.retryError = new Error("sqlite retry unavailable");
  const retryUploader = new ApplicationLogUploader(
    retryFailure,
    configuration,
    async () => {
      throw new Error("offline");
    },
  );
  const runtime = new ApplicationLogRuntime(
    { async record() {} } as never,
    retryUploader,
  );
  await assert.doesNotReject(runtime.shutdown());

  let fetchCalls = 0;
  let release: (() => void) | undefined;
  const inFlightOutbox = new FakeOutbox([entry("d")]);
  const singleFlightUploader = new ApplicationLogUploader(
    inFlightOutbox,
    configuration,
    async () => {
      fetchCalls += 1;
      await new Promise<void>((resolve) => {
        release = resolve;
      });
      return new Response(
        JSON.stringify({
          success: true,
          data: { results: [{ clientEventId: "d", status: "accepted" }] },
        }),
        { status: 200 },
      );
    },
  );
  const first = singleFlightUploader.flush();
  const second = singleFlightUploader.flush();
  assert.equal(first, second);
  await Promise.resolve();
  assert.equal(fetchCalls, 1);
  release?.();
  await assert.doesNotReject(first);
});

test("shutdown 等待已入队日志，并在旧单飞 flush 后重新扫描", async () => {
  const initialListStarted = deferred<void>();
  const releaseInitialList = deferred<void>();
  const pending: ApplicationLogDeliveryEntry[] = [];
  const accepted: string[] = [];
  let listCalls = 0;
  const outbox: ApplicationLogOutboxPort = {
    async enqueue(log) {
      pending.push({ ...log, attemptCount: 0 });
    },
    async listReady() {
      listCalls += 1;
      const snapshot = [...pending];
      if (listCalls === 1) {
        initialListStarted.resolve();
        await releaseInitialList.promise;
      }
      return snapshot;
    },
    async markAccepted(eventIds) {
      accepted.push(...eventIds);
    },
    async markRejected() {},
    async releaseRetry() {},
  };
  const logger = new ApplicationLogRuntime(
    new ApplicationLogger(
      outbox,
      () => ({
        storeCode: null,
        deviceCode: null,
        userId: null,
        userName: null,
        appVersion: "1",
        instanceId: "instance",
      }),
      () => "shutdown-event",
      () => "2026-07-31T00:00:00.000Z",
    ),
    new ApplicationLogUploader(outbox, configuration, async (_url, request) => {
      const body = JSON.parse(String(request?.body)) as {
        logs: { clientEventId: string }[];
      };
      return new Response(
        JSON.stringify({
          success: true,
          data: {
            results: body.logs.map((log) => ({
              clientEventId: log.clientEventId,
              status: "accepted",
            })),
          },
        }),
        { status: 200 },
      );
    }),
  );

  const oldFlush = logger.flush();
  await initialListStarted.promise;
  await logger.logger.record({
    level: "Information",
    message: "POS runtime shutting down.",
    category: "runtime.shutdown",
  });
  const shutdown = logger.shutdown();
  releaseInitialList.resolve();
  await Promise.all([oldFlush, shutdown]);

  assert.equal(listCalls, 2);
  assert.deepEqual(accepted, ["shutdown-event"]);
});

test("程序日志脱敏自由文本中的 PAN 与全部敏感键，保留 authorizationMode", () => {
  const text = sanitizeText(
    "Authorization: Bearer private-token token=abc password: pass apiKey=key " +
      "api_key=underscore-key api-key=hyphen-key secret: hidden " +
      "credential=client-secret voucher=GIFT-ABC cvv=XYZ cookie=session123 header=x-private " +
      "card=4111111111111111 authorizationMode=supervisor",
    1_000,
  );
  assert.doesNotMatch(
    text,
    /private-token|=abc\b|:\s+pass\b|=key\b|underscore-key|hyphen-key|:\s+hidden\b|client-secret|GIFT-ABC|XYZ|session123|x-private|4111/u,
  );
  assert.match(text, /\[REDACTED\]/u);
  assert.match(text, /authorizationMode=supervisor/u);
  assert.deepEqual(
    sanitizeProperties({
      authorizationMode: "supervisor",
      authorizationToken: "Bearer private-token",
      api_key: "underscore-key",
      "api-key": "hyphen-key",
      cardNumber: "4111111111111111",
      endpoint: "https://example.test?a=1&token=secret",
    }),
    {
      authorizationMode: "supervisor",
      "[REDACTED_KEY]": "[REDACTED]",
      endpoint: "https://example.test?a=1&token=[REDACTED]",
    },
  );
});

test("程序日志脱敏 JSON 双引号和单引号的敏感键值，保留 authorizationMode", () => {
  const doubleQuoted = sanitizeText(
    '{"password":"hunter2","TOKEN":"abc123","apiKey":"api-value","Secret":"top-secret",' +
      '"clientCredential":"client-secret","voucher":"GIFT-ABC","cvv":"XYZ",' +
      '"cookie":"session123","header":"x-private","pin":1234,"authorizationMode":"supervisor"}',
    1_000,
  );
  const singleQuoted = sanitizeText(
    "{'Password':'hunter2','token':'abc123','APIKEY':'api-value','secret':'top-secret'," +
      "'clientCredential':'client-secret','voucher':'GIFT-ABC','cvv':'XYZ'," +
      "'cookie':'session123','header':'x-private','pin':9999,'authorizationMode':'supervisor'}",
    1_000,
  );

  for (const text of [doubleQuoted, singleQuoted]) {
    assert.doesNotMatch(
      text,
      /hunter2|abc123|api-value|top-secret|client-secret|GIFT-ABC|XYZ|session123|x-private|1234|9999/u,
    );
    assert.match(text, /\[REDACTED\]/u);
    assert.match(text, /authorizationMode/u);
    assert.match(text, /supervisor/u);
  }
});

test("程序日志脱敏无引号敏感键的单双引号值、转义和未闭合行", () => {
  const closed = sanitizeText(
    'token="secret" password=\'hunter two\' apiKey="private-key" ' +
      'credential="escaped \\"quoted\\" secret" authorizationMode="supervisor" note=\'visible words\'',
    2_000,
  );
  const multiline = sanitizeText(
    'token="line-secret\nnote="visible line"\npassword=\'unterminated-secret',
    2_000,
  );

  for (const text of [closed, multiline]) {
    assert.doesNotMatch(
      text,
      /secret|hunter two|private-key|escaped|quoted|unterminated/u,
    );
    assert.match(text, /\[REDACTED\]/u);
  }
  assert.match(closed, /authorizationMode="supervisor"/u);
  assert.match(closed, /note='visible words'/u);
  assert.match(multiline, /note="visible line"/u);
});

test("程序日志 record 与 wire 均二次脱敏 quoted 值及属性键，并限制属性键字段", async () => {
  const longPropertyKey = `diagnostic=${"k".repeat(40_000)}`;
  const recordOutbox = new FakeOutbox([]);
  const logger = new ApplicationLogger(
    recordOutbox,
    () => ({
      storeCode: "S1",
      deviceCode: "IPAD-1",
      userId: "cashier-1",
      userName: "Alice",
      appVersion: "1",
      instanceId: "install-1",
    }),
    () => "record-event",
    () => "2026-08-01T00:00:00.000Z",
  );

  await logger.record({
    level: "Error",
    message: 'token="record-secret"',
    properties: {
      'password="property-key-secret"': "property-value-secret",
      [longPropertyKey]: "safe-value",
    },
  });

  const recorded = recordOutbox.enqueued[0];
  assert.ok(recorded);
  assert.doesNotMatch(
    JSON.stringify(recorded),
    /record-secret|property-key-secret|property-value-secret/u,
  );
  assert.ok(
    Object.keys(recorded.properties ?? {}).every(
      (key) => new TextEncoder().encode(key).byteLength <= 32 * 1024,
    ),
  );

  const wireOutbox = new FakeOutbox([
    {
      ...entry("wire-event"),
      message: 'apiKey="wire-secret"',
      properties: {
        'token="wire-property-key-secret"': "wire-property-value-secret",
      },
    },
  ]);
  let wireBody = "";
  const uploader = new ApplicationLogUploader(
    wireOutbox,
    configuration,
    async (_url, request) => {
      wireBody = String(request?.body);
      return new Response(
        JSON.stringify({
          data: {
            results: [
              { clientEventId: "wire-event", status: "accepted" },
            ],
          },
        }),
        { status: 200 },
      );
    },
  );

  await uploader.flush();

  assert.doesNotMatch(
    wireBody,
    /wire-secret|wire-property-key-secret|wire-property-value-secret/u,
  );
  assert.match(wireBody, /\[REDACTED\]/u);
});

test("程序日志属性与嵌入 JSON 的敏感键统一替换，NFKC 后非 ASCII 键 fail-closed", () => {
  const properties = sanitizeProperties({
    "secret-private-value": "property-secret-value",
    "密钥标签": "unicode-key-value",
    authorizationMode: "supervisor",
  });
  const json = sanitizeText(
    '{"secret-private-value":"json-secret-value","密钥标签":"json-unicode-value","authorizationMode":"supervisor"}',
    2_000,
  );

  for (const serialized of [JSON.stringify(properties), json]) {
    assert.doesNotMatch(
      serialized,
      /secret-private-value|property-secret-value|密钥标签|unicode-key-value|json-secret-value|json-unicode-value/u,
    );
    assert.match(serialized, /\[REDACTED_KEY\]/u);
    assert.match(serialized, /authorizationMode/u);
    assert.match(serialized, /supervisor/u);
  }
});

test("程序日志递归脱敏 JSON 敏感父键的对象和数组，并安全处理嵌入片段", () => {
  const rootObject = sanitizeText(
    '{"token":{"value":"secret-value","other":"second-secret"},"authorizationMode":"supervisor"}',
    1_000,
  );
  const rootArray = sanitizeText(
    '{"token":["secret-one","secret-two"],"authorizationMode":"supervisor"}',
    1_000,
  );
  const embedded = sanitizeText(
    '同步失败，响应片段: {"details":{"password":"nested-secret"},"authorizationMode":"supervisor"}。',
    1_000,
  );
  const malformed = sanitizeText(
    '非法 JSON {"token":{"value":"still-safe"}',
    1_000,
  );

  for (const text of [rootObject, rootArray, embedded]) {
    assert.doesNotMatch(
      text,
      /secret-value|second-secret|secret-one|secret-two|nested-secret/u,
    );
    assert.match(text, /\[REDACTED\]/u);
    assert.match(text, /authorizationMode/u);
    assert.match(text, /supervisor/u);
  }
  assert.doesNotThrow(() => malformed);
});

test("程序日志 JSON 超过递归深度时整体脱敏而不泄漏", () => {
  let deep: unknown = { token: ["deep-secret-one", "deep-secret-two"] };
  for (let index = 0; index < 20; index += 1) {
    deep = { context: deep };
  }

  const text = sanitizeText(JSON.stringify(deep), 8_000);

  assert.doesNotMatch(text, /deep-secret-one|deep-secret-two/u);
  assert.match(text, /\[REDACTED\]/u);
});

test("程序日志截断或非法 JSON 的敏感父对象数组 fail-closed，键名归一化一致", () => {
  const malformedObject = sanitizeText(
    '{"token":{"value":"secret-one","other":"secret-two"}',
    1_000,
  );
  const malformedArray = sanitizeText(
    '{"token":["array-secret-one","array-secret-two"}',
    1_000,
  );
  const longJson = `{"token":["long-secret-one","long-secret-two","${"x".repeat(256)}"],"note":"safe"}`;
  const truncated = sanitizeText(longJson, 64);
  const normalizedKeys = sanitizeText(
    '{"api.key":"dot-key-secret","authorization.mode":"supervisor"}',
    1_000,
  );

  for (const text of [malformedObject, malformedArray, truncated]) {
    assert.doesNotMatch(
      text,
      /secret-one|secret-two|array-secret-one|array-secret-two|long-secret-one|long-secret-two/u,
    );
    assert.match(text, /\[REDACTED\]/u);
  }
  assert.doesNotMatch(normalizedKeys, /dot-key-secret/u);
  assert.match(normalizedKeys, /authorization\.mode/u);
  assert.match(normalizedKeys, /supervisor/u);
});

test("程序日志大量未闭合括号只作有界扫描并对敏感结构 fail-closed", () => {
  const malformed = `${"{".repeat(2_000)}"token":{"value":"bulk-secret-one","other":"bulk-secret-two"}`;

  const text = sanitizeText(malformed, 8_000);

  assert.doesNotMatch(text, /bulk-secret-one|bulk-secret-two/u);
  assert.match(text, /\[REDACTED\]/u);
});

test("程序日志普通文本的未配对引号不阻断后续敏感 JSON 对象和数组", () => {
  const object = sanitizeText(
    'prefix "oops {"token":{"value":"quoted-secret-one","other":"quoted-secret-two"}}',
    1_000,
  );
  const array = sanitizeText(
    'prefix "oops {"token":["quoted-array-one","quoted-array-two"]}',
    1_000,
  );

  for (const text of [object, array]) {
    assert.doesNotMatch(
      text,
      /quoted-secret-one|quoted-secret-two|quoted-array-one|quoted-array-two/u,
    );
    assert.match(text, /\[REDACTED\]/u);
  }
});

test("程序日志回看前置敏感赋值键，对未闭合对象数组 fail-closed", () => {
  const inputs = [
    'token={"value":"prefix-secret-one","other":"prefix-secret-two"',
    '"token":{"value":"quoted-prefix-one","other":"quoted-prefix-two"',
    'token=["array-prefix-one","array-prefix-two"',
    '"token":["quoted-array-one","quoted-array-two"',
  ];

  for (const input of inputs) {
    const text = sanitizeText(input, 1_000);
    assert.doesNotMatch(
      text,
      /prefix-secret-one|prefix-secret-two|quoted-prefix-one|quoted-prefix-two|array-prefix-one|array-prefix-two|quoted-array-one|quoted-array-two/u,
    );
    assert.match(text, /\[REDACTED\]/u);
  }
  assert.match(
    sanitizeText('note={"value":"safe","other":"still-safe"', 1_000),
    /still-safe/u,
  );
  assert.match(
    sanitizeText('authorizationMode={"value":"supervisor"', 1_000),
    /supervisor/u,
  );
});

test("程序日志前置任意空白与 API.KEY 赋值不会绕过敏感结构或平面脱敏", () => {
  const gap = " ".repeat(200);
  const inputs = [
    `token${gap}={"value":"wide-object-one","other":"wide-object-two"`,
    `"token"${gap}:["wide-array-one","wide-array-two"`,
    `API.KEY${gap}={"value":"dot-object-one","other":"dot-object-two"`,
    `"API.KEY"${gap}:["dot-array-one","dot-array-two"`,
    "API.KEY=dot-equals-secret API.KEY:dot-colon-secret 'API.KEY'='dot-single-secret' \"API.KEY\":\"dot-double-secret\"",
  ];

  for (const input of inputs) {
    const text = sanitizeText(input, 2_000);
    assert.doesNotMatch(
      text,
      /wide-object-one|wide-object-two|wide-array-one|wide-array-two|dot-object-one|dot-object-two|dot-array-one|dot-array-two|dot-equals-secret|dot-colon-secret|dot-single-secret|dot-double-secret/u,
    );
    assert.match(text, /\[REDACTED\]/u);
  }
  assert.match(
    sanitizeText(`authorization.mode${gap}={"value":"supervisor"`, 1_000),
    /supervisor/u,
  );
});

test("程序日志敏感 assignment 对象数组闭合与未闭合组合矩阵均整值脱敏", () => {
  const whitespace = ["", " ", " ".repeat(200)];
  const separators = [":", "="];
  const keys = ["token", '"token"', "API.KEY", '"API.KEY"', '"to\\u006ben"'];
  const structures = [
    {
      open: "{",
      body: '"value":"matrix-secret-one","other":"matrix-secret-two"',
      close: "}",
    },
    { open: "[", body: '"matrix-secret-one","matrix-secret-two"', close: "]" },
  ];

  for (const gap of whitespace) {
    for (const separator of separators) {
      for (const key of keys) {
        for (const structure of structures) {
          for (const isClosed of [true, false]) {
            const text = sanitizeText(
              `${key}${gap}${separator}${gap}${structure.open}${structure.body}${isClosed ? structure.close : ""}`,
              2_000,
            );
            assert.doesNotMatch(text, /matrix-secret-one|matrix-secret-two/u);
            assert.match(text, /\[REDACTED\]/u);
          }
        }
      }
    }
  }
});

test("程序日志不可靠 assignment 键长度或非 ASCII 边界均 fail-closed", () => {
  const keys = [
    `token${"x".repeat(123)}`,
    `token${"x".repeat(124)}`,
    `n${"x".repeat(128)}`,
    "密token",
    "token密",
    "to密ken",
  ];
  const structures = [
    {
      open: "{",
      body: '"value":"unreliable-secret-one","other":"unreliable-secret-two"',
      close: "}",
    },
    {
      open: "[",
      body: '"unreliable-secret-one","unreliable-secret-two"',
      close: "]",
    },
  ];

  for (const key of keys) {
    for (const structure of structures) {
      for (const isClosed of [true, false]) {
        const text = sanitizeText(
          `${key}=${structure.open}${structure.body}${isClosed ? structure.close : ""}`,
          2_000,
        );
        assert.doesNotMatch(
          text,
          /unreliable-secret-one|unreliable-secret-two/u,
        );
        assert.match(text, /\[REDACTED\]/u);
      }
    }
  }
});

test("程序日志 assignment 键必须是完整且边界可信的 token", () => {
  const malformedKeys = [
    "to/ken",
    "to@ken",
    "api/key",
    "pass/word",
    "authoriz/ation",
    "'to'ken'",
    '"to"ken"',
    "'token\\'",
    '"token\\"',
  ];
  const structures = [
    {
      open: "{",
      body: '"value":"boundary-secret-one","other":"boundary-secret-two"',
      close: "}",
    },
    {
      open: "[",
      body: '"boundary-secret-one","boundary-secret-two"',
      close: "]",
    },
  ];

  for (const key of malformedKeys) {
    for (const structure of structures) {
      for (const isClosed of [true, false]) {
        const text = sanitizeText(
          `${key}=${structure.open}${structure.body}${isClosed ? structure.close : ""}`,
          2_000,
        );
        assert.doesNotMatch(text, /boundary-secret-one|boundary-secret-two/u);
        assert.match(text, /\[REDACTED\]/u);
      }
    }
  }

  for (const key of [
    "note",
    '"note"',
    "authorizationMode",
    '"authorization\\u004dode"',
  ]) {
    const text = sanitizeText(
      `${key}={"value":"boundary-retained","other":"also-retained"}`,
      2_000,
    );
    assert.match(text, /boundary-retained|also-retained/u);
    assert.doesNotMatch(text, /\[REDACTED\]/u);
  }

  const chineseSafe = sanitizeText('响应片段: {"note":"中文安全值"}', 2_000);
  const chineseSensitive = sanitizeText(
    '响应片段: {"details":{"token":"中文标签敏感值"}}',
    2_000,
  );
  assert.match(chineseSafe, /中文安全值/u);
  assert.doesNotMatch(chineseSafe, /\[REDACTED\]/u);
  assert.doesNotMatch(chineseSensitive, /中文标签敏感值/u);
  assert.match(chineseSensitive, /\[REDACTED\]/u);
});

test("程序日志单引号标准控制转义与未知转义键均安全处理", () => {
  const keys = [
    "'to\\\\ken'",
    "'to\\'ken'",
    "'to\\\"ken'",
    "'to\\bken'",
    "'to\\fken'",
    "'to\\nken'",
    "'to\\rken'",
    "'to\\tken'",
    "'to\\u0009ken'",
    "'to\\xken'",
    "'to\\qken'",
    "'to" + "\\",
  ];
  const structures = [
    {
      open: "{",
      body: '"value":"escape-secret-one","other":"escape-secret-two"',
      close: "}",
    },
    { open: "[", body: '"escape-secret-one","escape-secret-two"', close: "]" },
  ];

  for (const key of keys) {
    for (const structure of structures) {
      for (const isClosed of [true, false]) {
        const text = sanitizeText(
          `${key}:${structure.open}${structure.body}${isClosed ? structure.close : ""}`,
          2_000,
        );
        assert.doesNotMatch(text, /escape-secret-one|escape-secret-two/u);
        assert.match(text, /\[REDACTED\]/u);
      }
    }
  }

  const authorizationMode = sanitizeText(
    '"authorization\\u004dode":{"value":"supervisor","other":"retained"}',
    1_000,
  );
  assert.match(authorizationMode, /supervisor|retained/u);
  assert.doesNotMatch(authorizationMode, /\[REDACTED\]/u);
});

test("程序日志脱敏 URL 查询中的扩展敏感键", () => {
  const text = sanitizeText(
    "https://logs.example.test/report?credential=client-secret&voucher=GIFT-ABC" +
      "&cvv=XYZ&cookie=session123&header=x-private&authorizationMode=supervisor",
    1_000,
  );

  assert.doesNotMatch(text, /client-secret|GIFT-ABC|XYZ|session123|x-private/u);
  assert.match(text, /authorizationMode=supervisor/u);
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
