import assert from "node:assert/strict";
import test from "node:test";

import type { SettingsPendingDataSnapshot } from "../../features/settings/settings-presenter";

import { PreloginServerConnectionControl } from "./prelogin-server-connection-control";

const CLEAR: SettingsPendingDataSnapshot = {
  hasActiveCart: false,
  hasFulfilmentInFlight: false,
  hasSyncOrAuditInFlight: false,
  paymentConfigurationSensitiveOrderCount: 0,
  pendingDurableWriteCount: 0,
  pendingReturnCount: 0,
  pendingSaleCount: 0,
  unresolvedPaymentCount: 0,
};

test("预登录服务器测试只访问受信 health，不写配置", async () => {
  const events: string[] = [];
  const subject = control(events);

  assert.equal(
    await subject.test("https://pos.example.test/pos/", signal()),
    true,
  );
  assert.deepEqual(events, [
    "probe:https://pos.example.test/pos/api/v1/health",
  ]);
});

test("预登录服务器切换在待处理数据存在时失败关闭", async () => {
  const events: string[] = [];
  const subject = control(events, {
    pending: { ...CLEAR, pendingSaleCount: 1 },
  });

  assert.deepEqual(
    await subject.change("https://pos.example.test/pos", signal()),
    { status: "blocked", reason: "pending-local-data" },
  );
  assert.deepEqual(events, ["exclusive", "pending"]);
});

test("预登录服务器切换仅在安全快照清空且 health 通过后保存", async () => {
  const events: string[] = [];
  let reachable = false;
  const subject = control(events, {
    reachable: () => reachable,
  });

  assert.deepEqual(
    await subject.change("https://pos.example.test/pos", signal()),
    { status: "blocked", reason: "candidate-unreachable" },
  );
  assert.equal(events.includes("save:https://pos.example.test/pos"), false);

  reachable = true;
  assert.deepEqual(
    await subject.change("https://pos.example.test/pos/", signal()),
    { status: "completed", apiBaseUrl: "https://pos.example.test/pos" },
  );
  assert.deepEqual(events.slice(-4), [
    "exclusive",
    "pending",
    "probe:https://pos.example.test/pos/api/v1/health",
    "save:https://pos.example.test/pos",
  ]);
});

test("预登录服务器在探测前拒绝非白名单地址和不安全 URL", async () => {
  const events: string[] = [];
  const subject = control(events);

  await assert.rejects(
    subject.test("https://evil.example.test/pos", signal()),
    /trusted build allowlist/,
  );
  await assert.rejects(
    subject.test("https://pos.example.test/pos?token=secret", signal()),
    /unsupported data/,
  );
  assert.deepEqual(events, []);
});

test("地址持久化提交后收到取消仍如实返回成功", async () => {
  const controller = new AbortController();
  const events: string[] = [];
  const subject = new PreloginServerConnectionControl({
    allowSwitchWithPendingLocalData: false,
    currentApiBaseUrl: "https://pos.example.test/pos",
    trustedApiOrigins: ["https://pos.example.test"],
    runExclusive: async (operation) => operation(),
    readPendingData: async () => CLEAR,
    probe: async () => true,
    save: async (url) => {
      events.push(`save:${url}`);
      controller.abort();
    },
  });

  assert.deepEqual(
    await subject.change(
      "https://pos.example.test/alternate",
      controller.signal,
    ),
    {
      status: "completed",
      apiBaseUrl: "https://pos.example.test/alternate",
    },
  );
  assert.deepEqual(events, [
    "save:https://pos.example.test/alternate",
  ]);
});

function signal(): AbortSignal {
  return new AbortController().signal;
}

function control(
  events: string[],
  options: Readonly<{
    pending?: SettingsPendingDataSnapshot;
    reachable?: () => boolean;
  }> = {},
): PreloginServerConnectionControl {
  return new PreloginServerConnectionControl({
    allowSwitchWithPendingLocalData: false,
    currentApiBaseUrl: "https://pos.example.test/pos",
    trustedApiOrigins: ["https://pos.example.test"],
    runExclusive: async (operation) => {
      events.push("exclusive");
      return operation();
    },
    readPendingData: async () => {
      events.push("pending");
      return options.pending ?? CLEAR;
    },
    probe: async (url) => {
      events.push(`probe:${url}`);
      return options.reachable?.() ?? true;
    },
    save: async (url) => {
      events.push(`save:${url}`);
    },
  });
}
