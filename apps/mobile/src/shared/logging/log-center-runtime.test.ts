import assert from "node:assert/strict";
import type { LogCenterConfig } from "./log-center";
import {
  __flushPendingLogsForTests,
  __getPendingLogCountForTests,
  __resetLogCenterRuntimeForTests,
  __setLogCenterConfigForTests,
  reportApplicationLog,
} from "./log-center-runtime";

const originalFetch = globalThis.fetch;

function createEnabledConfig(overrides: Partial<LogCenterConfig> = {}): LogCenterConfig {
  return {
    enabled: true,
    endpoint: "https://logs.example.com/api/system/logs/ingest",
    projectCode: "HbwebExpo",
    key: "test-log-key",
    environment: "test",
    serviceName: "HbwebExpoApp",
    sourceType: "Mobile",
    batchSize: 10,
    maxQueueSize: 30,
    retryLimit: 1,
    ...overrides,
  };
}

async function waitForFlush() {
  await new Promise((resolve) => setTimeout(resolve, 10));
  await new Promise((resolve) => setTimeout(resolve, 10));
}

async function run() {
  try {
    __resetLogCenterRuntimeForTests();

    let fetchCalls = 0;
    globalThis.fetch = (async () => {
      fetchCalls += 1;
      return {
        ok: true,
        status: 200,
      } as Response;
    }) as typeof fetch;

    __setLogCenterConfigForTests(createEnabledConfig({ enabled: false, key: "", environment: "" }));
    reportApplicationLog({
      level: "Error",
      message: "缺少配置时不上报",
      sourceType: "app.test",
    });
    await waitForFlush();
    assert.equal(fetchCalls, 0, "缺少日志中心配置时不应发送上报请求");
    assert.equal(__getPendingLogCountForTests(), 0, "缺少配置时不应残留队列");

    __resetLogCenterRuntimeForTests();
    const requestBodies: string[] = [];
    globalThis.fetch = (async (_input, init) => {
      fetchCalls += 1;
      requestBodies.push(String(init?.body ?? ""));
      return {
        ok: true,
        status: 200,
      } as Response;
    }) as typeof fetch;

    __setLogCenterConfigForTests(createEnabledConfig());
    fetchCalls = 0;
    reportApplicationLog({
      level: "Error",
      message: "配置存在时异步 flush",
      sourceType: "app.test",
    });
    assert.equal(__getPendingLogCountForTests(), 1, "reportApplicationLog 应先把日志放入队列");
    assert.equal(fetchCalls, 0, "flush 应异步执行，调用当下不应阻塞业务");
    await waitForFlush();
    assert.equal(fetchCalls, 1, "异步 flush 应触发一次请求");
    assert.match(requestBodies[0] ?? "", /配置存在时异步 flush/, "flush 请求体应包含日志内容");
    assert.equal(__getPendingLogCountForTests(), 0, "flush 成功后应清空队列");

    __resetLogCenterRuntimeForTests();
    globalThis.fetch = (async () => {
      throw new Error("network down");
    }) as typeof fetch;
    __setLogCenterConfigForTests(createEnabledConfig({ retryLimit: 1 }));
    reportApplicationLog({
      level: "Error",
      message: "flush 失败也不能抛错",
      sourceType: "app.test",
    });
    await assert.doesNotReject(
      async () => {
        await __flushPendingLogsForTests();
      },
      "flush 失败时也必须吞掉异常，不能向调用方抛错"
    );
  } finally {
    globalThis.fetch = originalFetch;
    __resetLogCenterRuntimeForTests();
  }
}

void run();
