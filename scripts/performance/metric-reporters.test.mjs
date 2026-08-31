import assert from "node:assert/strict";
import test from "node:test";

import { validateMetricBatchV1 } from "./lib/metric-batch.mjs";
import { redactSensitive } from "./lib/http-reporter.mjs";
import {
  reportMetricBatch,
  reportMetricBatchFromEnvironment,
} from "./report-metric-batch.mjs";
import {
  buildReleaseEvent,
  reportReleaseEvent,
  validateReleaseEventV1,
} from "./report-release-event.mjs";

const COMMIT_SHA = "a".repeat(40);
const SERVICE_TOKEN = `hbsvc_${"A".repeat(43)}`;

function apiResponse(payload, { status = 200, requestId = null } = {}) {
  return {
    ok: status >= 200 && status < 300,
    status,
    headers: {
      get: (name) => {
        if (name === "x-request-id") return requestId;
        if (name === "content-type") return "application/json";
        return null;
      },
    },
    body: { cancel: async () => {} },
    text: async () => JSON.stringify(payload),
  };
}

function createValidBatch() {
  return {
    schemaVersion: 1,
    events: [
      {
        eventId: "33333333-3333-4333-8333-333333333333",
        metric: "ci.run.duration",
        observedAt: "2026-08-25T01:00:01.250Z",
        value: 1250,
        unit: "ms",
        dimensions: {
          environment: "CI",
          lane: "backend",
          outcome: "accepted",
          source: "github-actions",
          project: "hotbargain/hb-platform",
        },
      },
    ],
  };
}

test("MetricBatchV1 严格接受完整且有限的 payload", () => {
  const payload = createValidBatch();
  assert.equal(validateMetricBatchV1(payload), payload);
});

test("MetricBatchV1 拒绝未知字段、非白名单指标和负值", () => {
  const unknownField = { ...createValidBatch(), secret: "不应上报" };
  assert.throws(() => validateMetricBatchV1(unknownField), /未知字段|secret/i);

  const unknownMetric = createValidBatch();
  unknownMetric.events[0].metric = "quality_baseline.backend.duration_ms";
  assert.throws(
    () => validateMetricBatchV1(unknownMetric),
    /白名单|metric|ci\.run\.duration/i,
  );

  const negative = createValidBatch();
  negative.events[0].value = -1;
  assert.throws(() => validateMetricBatchV1(negative), /非负|value/i);
});

test("MetricBatchV1 只向固定 HTTPS 路径发送 Bearer service token", async () => {
  const calls = [];
  const result = await reportMetricBatch({
    payload: createValidBatch(),
    baseUrl: "https://metrics.example.test",
    token: SERVICE_TOKEN,
    timeoutMs: 500,
    fetchImpl: async (url, options) => {
      calls.push({ url, options });
      return new Response(
        JSON.stringify({
          success: true,
          data: { acceptedCount: 1, duplicateCount: 0, rejectedCount: 0 },
        }),
        {
          status: 200,
          headers: {
            "content-type": "application/json; charset=utf-8",
            "x-request-id": "req-123",
          },
        },
      );
    },
  });

  assert.deepEqual(result, {
    status: 200,
    requestId: "req-123",
    acceptedCount: 1,
    duplicateCount: 0,
  });
  assert.equal(calls.length, 1);
  assert.equal(
    calls[0].url,
    "https://metrics.example.test/api/system/performance/automation-batches",
  );
  assert.equal(calls[0].options.method, "POST");
  assert.equal(calls[0].options.redirect, "error");
  assert.equal(calls[0].options.headers.authorization, `Bearer ${SERVICE_TOKEN}`);
  assert.equal(calls[0].options.headers["content-type"], "application/json");
  assert.ok(calls[0].options.signal instanceof AbortSignal);
  assert.deepEqual(JSON.parse(calls[0].options.body), createValidBatch());
});

test("service reporter 接受后端返回的采样策略字段", async () => {
  const result = await reportMetricBatch({
    payload: createValidBatch(),
    baseUrl: "https://metrics.example.test",
    token: SERVICE_TOKEN,
    fetchImpl: async () =>
      apiResponse({
        success: true,
        data: {
          acceptedCount: 1,
          duplicateCount: 0,
          rejectedCount: 0,
          baselineState: "frozen",
          defaultSampleRate: 1,
          policies: [
            {
              metric: "ci.run.duration",
              selector: "backend",
              sampleRate: 0.2,
              slowThreshold: 5_000,
            },
          ],
        },
      }),
  });

  assert.deepEqual(result, {
    status: 200,
    requestId: null,
    acceptedCount: 1,
    duplicateCount: 0,
  });
});

test("service reporter 接受后端基于极大 P95 放大的有限慢阈值", async () => {
  const result = await reportMetricBatch({
    payload: createValidBatch(),
    baseUrl: "https://metrics.example.test",
    token: SERVICE_TOKEN,
    fetchImpl: async () =>
      apiResponse({
        success: true,
        data: {
          acceptedCount: 1,
          duplicateCount: 0,
          rejectedCount: 0,
          baselineState: "frozen",
          defaultSampleRate: 1,
          policies: [
            {
              metric: "ci.run.duration",
              selector: "backend",
              sampleRate: 0.2,
              slowThreshold: 1_200_000_000_000_000,
            },
          ],
        },
      }),
  });

  assert.equal(result.acceptedCount, 1);
});

test("service reporter 继续拒绝不完整或未知的采样策略响应", async () => {
  const validCounts = {
    acceptedCount: 1,
    duplicateCount: 0,
    rejectedCount: 0,
  };
  const cases = [
    {
      name: "采样字段不完整",
      data: { ...validCounts, baselineState: "observing" },
      pattern: /采样策略字段必须同时返回/i,
    },
    {
      name: "基线状态未知",
      data: {
        ...validCounts,
        baselineState: "unknown",
        defaultSampleRate: 1,
        policies: [],
      },
      pattern: /baselineState|not_started|observing|frozen/i,
    },
    {
      name: "策略包含未知字段",
      data: {
        ...validCounts,
        baselineState: "frozen",
        defaultSampleRate: 1,
        policies: [
          {
            metric: "ci.run.duration",
            selector: "backend",
            sampleRate: 0.2,
            unexpected: true,
          },
        ],
      },
      pattern: /未知字段|unexpected/i,
    },
    {
      name: "策略指标不属于 automation 端点",
      data: {
        ...validCounts,
        baselineState: "frozen",
        defaultSampleRate: 1,
        policies: [
          {
            metric: "api.request.duration",
            selector: "all",
            sampleRate: 0.2,
          },
        ],
      },
      pattern: /metric|ci\.run\.duration|web\.first_screen\.bytes/i,
    },
    {
      name: "响应包含未知顶层字段",
      data: { ...validCounts, unexpected: true },
      pattern: /未知字段|unexpected/i,
    },
  ];

  for (const current of cases) {
    await assert.rejects(
      reportMetricBatch({
        payload: createValidBatch(),
        baseUrl: "https://metrics.example.test",
        token: SERVICE_TOKEN,
        fetchImpl: async () => apiResponse({ success: true, data: current.data }),
      }),
      current.pattern,
      current.name,
    );
  }
});

test("service reporter 拒绝 HTTP、非 hbsvc token、单边配置和重定向错误泄密", async () => {
  const payload = createValidBatch();
  await assert.rejects(
    reportMetricBatch({
      payload,
      baseUrl: "http://metrics.example.test",
      token: SERVICE_TOKEN,
      fetchImpl: async () => assert.fail("HTTP URL 不得触发请求"),
    }),
    /HTTPS/i,
  );
  await assert.rejects(
    reportMetricBatch({
      payload,
      baseUrl: "https://metrics.example.test",
      token: "jwt-looking-token",
      fetchImpl: async () => assert.fail("非 service token 不得触发请求"),
    }),
    /service token|hbsvc_/i,
  );
  await assert.rejects(
    reportMetricBatchFromEnvironment({
      payload,
      optional: true,
      env: { QUALITY_BASELINE_SERVICE_URL: "https://metrics.example.test" },
      fetchImpl: async () => assert.fail("单边配置不得触发请求"),
    }),
    /必须同时|成对/i,
  );

  const secretError = new Error(
    `redirect Authorization: Bearer ${SERVICE_TOKEN} ${SERVICE_TOKEN}`,
  );
  const redacted = redactSensitive(secretError, [SERVICE_TOKEN]);
  assert.doesNotMatch(redacted, new RegExp(SERVICE_TOKEN));
  assert.match(redacted, /REDACTED/);
});

test("optional 模式在两个 secret 都缺失时跳过且不触网", async () => {
  let called = false;
  const result = await reportMetricBatchFromEnvironment({
    payload: createValidBatch(),
    optional: true,
    env: {},
    fetchImpl: async () => {
      called = true;
      throw new Error("不应触网");
    },
  });

  assert.deepEqual(result, { skipped: true, reason: "credentials_missing" });
  assert.equal(called, false);
});

test("optional 模式即使无密钥也先严格校验 payload", async () => {
  const invalidPayload = { ...createValidBatch(), unexpected: true };
  await assert.rejects(
    reportMetricBatchFromEnvironment({
      payload: invalidPayload,
      optional: true,
      env: {},
      fetchImpl: async () => assert.fail("无密钥且 payload 无效时不得触网"),
    }),
    /未知字段|unexpected/i,
  );
});

test("service reporter 到时主动 abort，且不泄露反射型 request-id/响应正文", async () => {
  await assert.rejects(
    reportMetricBatch({
      payload: createValidBatch(),
      baseUrl: "https://metrics.example.test",
      token: SERVICE_TOKEN,
      timeoutMs: 100,
      fetchImpl: async (_url, options) =>
        new Promise((_resolve, reject) => {
          options.signal.addEventListener(
            "abort",
            () => reject(new Error(`aborted ${SERVICE_TOKEN}`)),
            { once: true },
          );
        }),
    }),
    (error) => {
      assert.match(error.message, /超时|100ms/i);
      assert.doesNotMatch(error.message, new RegExp(SERVICE_TOKEN));
      return true;
    },
  );

  let bodyRead = false;
  await assert.rejects(
    reportMetricBatch({
      payload: createValidBatch(),
      baseUrl: "https://metrics.example.test",
      token: SERVICE_TOKEN,
      fetchImpl: async () => ({
        ok: false,
        status: 500,
        headers: { get: () => SERVICE_TOKEN },
        body: { cancel: async () => {} },
        text: async () => {
          bodyRead = true;
          return SERVICE_TOKEN;
        },
      }),
    }),
    (error) => {
      assert.doesNotMatch(error.message, new RegExp(SERVICE_TOKEN));
      return true;
    },
  );
  assert.equal(bodyRead, false);

  await assert.rejects(
    reportMetricBatch({
      payload: createValidBatch(),
      baseUrl: "https://metrics.example.test",
      token: SERVICE_TOKEN,
      fetchImpl: async () =>
        apiResponse({
          success: false,
          errorCode: "PERFORMANCE_METRIC_BATCH_INVALID",
          message: SERVICE_TOKEN,
        }),
    }),
    (error) => {
      assert.match(error.message, /PERFORMANCE_METRIC_BATCH_INVALID|业务拒绝/i);
      assert.doesNotMatch(error.message, new RegExp(SERVICE_TOKEN));
      return true;
    },
  );

  await assert.rejects(
    reportMetricBatch({
      payload: createValidBatch(),
      baseUrl: "https://metrics.example.test",
      token: SERVICE_TOKEN,
      fetchImpl: async () =>
        apiResponse({
          success: true,
          data: { acceptedCount: 0, duplicateCount: 0, rejectedCount: 1 },
        }),
    }),
    /计数|rejected/i,
  );

  const reflectedSuccess = await reportMetricBatch({
    payload: createValidBatch(),
    baseUrl: "https://metrics.example.test",
    token: SERVICE_TOKEN,
    fetchImpl: async () =>
      apiResponse(
        {
          success: true,
          data: { acceptedCount: 1, duplicateCount: 0, rejectedCount: 0 },
        },
        { requestId: SERVICE_TOKEN },
      ),
  });
  assert.deepEqual(reflectedSuccess, {
    status: 200,
    requestId: null,
    acceptedCount: 1,
    duplicateCount: 0,
  });
});

test("release event 必须显式声明已完成健康验收并限制 action/conclusion", () => {
  assert.throws(
    () =>
      buildReleaseEvent({
        action: "deploy",
        conclusion: "accepted",
        component: "backend",
        environment: "production",
        releaseId: "release-20260825-1",
        commitSha: COMMIT_SHA,
        healthChecked: false,
      }),
    /健康验收|health/i,
  );
  assert.throws(
    () =>
      buildReleaseEvent({
        action: "promote",
        conclusion: "accepted",
        component: "backend",
        environment: "production",
        releaseId: "release-20260825-1",
        commitSha: COMMIT_SHA,
        healthChecked: true,
      }),
    /deploy|rollback/i,
  );

  const event = buildReleaseEvent(
    {
      action: "rollback",
      conclusion: "failed",
      component: "backend",
      environment: "production",
      releaseId: "release-20260825-1",
      commitSha: COMMIT_SHA,
      healthChecked: true,
      healthCheckReference: "health-run-88",
      startedAtUtc: "2026-08-25T01:55:00.000Z",
      completedAtUtc: "2026-08-25T02:00:00.000Z",
    },
    {
      eventId: "11111111-1111-4111-8111-111111111111",
    },
  );

  assert.equal(validateReleaseEventV1(event), event);
  assert.equal(event.action, "rollback");
  assert.equal(event.status, "failed");
  assert.equal(event.commit, COMMIT_SHA);
  assert.equal(event.version, "release-20260825-1");
  assert.equal(event.startedAtUtc, "2026-08-25T01:55:00.000Z");
  assert.equal(event.completedAtUtc, "2026-08-25T02:00:00.000Z");
  assert.match(event.source, /manual:health-run-88/);
  assert.deepEqual(Object.keys(event).sort(), [
    "action",
    "commit",
    "completedAtUtc",
    "component",
    "environment",
    "eventId",
    "source",
    "startedAtUtc",
    "status",
    "version",
  ]);
});

test("release event 对同一部署验收生成稳定 eventId 和完全相同载荷", () => {
  const input = {
    action: "deploy",
    conclusion: "accepted",
    component: "backend",
    environment: "production",
    releaseId: "release-20260825-stable",
    commitSha: COMMIT_SHA,
    healthChecked: true,
    healthCheckReference: "deployment-acceptance-9001",
    startedAtUtc: "2026-08-25T03:58:00.000Z",
    completedAtUtc: "2026-08-25T04:00:00.000Z",
    sourceProvider: "github-actions",
    sourceRunId: "9001",
  };

  const first = buildReleaseEvent(input);
  const retry = buildReleaseEvent(input);

  assert.equal(first.eventId, retry.eventId);
  assert.deepEqual(first, retry);
});

test("release event 使用独立固定路径上报", async () => {
  const event = buildReleaseEvent(
    {
      action: "deploy",
      conclusion: "accepted",
      component: "web",
      environment: "production",
      releaseId: "release-20260825-2",
      commitSha: COMMIT_SHA,
      healthChecked: true,
      startedAtUtc: "2026-08-25T02:58:00.000Z",
      completedAtUtc: "2026-08-25T03:00:00.000Z",
    },
    {
      eventId: "22222222-2222-4222-8222-222222222222",
    },
  );
  const calls = [];
  await reportReleaseEvent({
    event,
    baseUrl: "https://metrics.example.test/",
    token: SERVICE_TOKEN,
    fetchImpl: async (url, options) => {
      calls.push({ url, options });
      return apiResponse({ success: true, data: event });
    },
  });

  assert.equal(
    calls[0].url,
    "https://metrics.example.test/api/system/performance/release-events",
  );
});
