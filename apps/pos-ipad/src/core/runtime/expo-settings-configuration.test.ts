import assert from "node:assert/strict";
import test from "node:test";

import {
  createSettingsApiHealthProbe,
  reloadSettingsRuntimeTerminally,
  reregisterSettingsDevice,
  settingsAppUpdateSnapshot,
  settingsPaymentConfiguration,
} from "./expo-settings-configuration";

test("只把完整公开支付选择映射到设置页，未知或半配置保持禁用", () => {
  assert.deepEqual(
    settingsPaymentConfiguration({
      provider: "square",
      square: {
        environment: "Sandbox",
        deviceId: "SQ-1",
        locationId: "LOC-1",
      },
    }),
    {
      provider: "square",
      square: {
        environment: "Sandbox",
        deviceId: "SQ-1",
        locationId: "LOC-1",
      },
      linkly: null,
    },
  );
  assert.deepEqual(
    settingsPaymentConfiguration({
      provider: "square",
      square: {
        environment: "Sandbox",
        deviceId: "",
        locationId: "LOC-1",
      },
    }),
    null,
  );
  assert.equal(
    settingsPaymentConfiguration({
      square: {
        environment: "Sandbox",
        deviceId: "SQ-1",
        locationId: "LOC-1",
      },
      linkly: { environment: "Production" },
    }),
    null,
  );
});

test("更新策略只映射公开版本字段，restart 可用性由受保护协调器给出", () => {
  assert.deepEqual(
    settingsAppUpdateSnapshot({
      channel: "preview",
      currentVersion: "1.0.0",
      policy: {
        enabled: true,
        minimumSupportedVersion: "1.0.0",
        latestVersion: "1.1.0",
        forceUpdate: true,
        appStoreUrl: null,
        releaseMessage: null,
      },
      restartAvailable: true,
    }),
    {
      channel: "preview",
      currentVersion: "1.0.0",
      availableVersion: "1.1.0",
      updateRequired: true,
      restartAvailable: true,
    },
  );
});

test("候选 API 探测只接受 2xx，并把 AbortSignal 传给 fetch", async () => {
  const signals: AbortSignal[] = [];
  const probe = createSettingsApiHealthProbe(async (_url, init) => {
    signals.push(init.signal);
    return { ok: true };
  });
  const abort = new AbortController();

  assert.equal(
    await probe(
      "https://pos.example.test/api/v1/health",
      abort.signal,
    ),
    true,
  );
  assert.equal(signals[0], abort.signal);
});

test("设备 rebind 提交后 signal 中止仍保留 authorized 终态，提交前中止零调用", async () => {
  const controller = new AbortController();
  const requests: string[] = [];
  let committedEvents = 0;
  const rebind = async (
    request: Readonly<{ activationCode: string }>,
    onCredentialsCommitted: () => void,
  ) => {
    requests.push(request.activationCode);
    onCredentialsCommitted();
    controller.abort();
    return { status: "authorized" } as const;
  };

  assert.deepEqual(
    await reregisterSettingsDevice(
      { activationCode: "HBDEV1-CODE" },
      controller.signal,
      rebind,
      () => { committedEvents += 1; },
    ),
    { status: "committed" },
  );
  assert.deepEqual(requests, ["HBDEV1-CODE"]);
  assert.equal(committedEvents, 1);

  const preAborted = new AbortController();
  preAborted.abort();
  await assert.rejects(
    () =>
      reregisterSettingsDevice(
        { activationCode: "HBDEV1-SECOND" },
        preAborted.signal,
        rebind,
        () => { committedEvents += 1; },
      ),
    /abort/i,
  );
  assert.deepEqual(requests, ["HBDEV1-CODE"]);
  assert.equal(committedEvents, 1);
});

test("设备凭据已提交后吞并后置异常或 supersession，并只发布一次窄 committed outcome", async () => {
  for (const outcome of ["throw", "superseded"] as const) {
    const committedPayloads: unknown[][] = [];
    const result = await reregisterSettingsDevice(
      { activationCode: `HBDEV1-${outcome}` },
      new AbortController().signal,
      async (_request, onCredentialsCommitted) => {
        onCredentialsCommitted();
        onCredentialsCommitted();
        if (outcome === "throw") throw new Error("post-save cleanup failed");
        return { status: "verifying" };
      },
      (...payload) => { committedPayloads.push(payload); },
    );

    assert.deepEqual(result, { status: "committed" });
    assert.deepEqual(committedPayloads, [[]]);
  }
});

test("设置 reload 成功后保持 terminal pending，失败时仍向上抛出", async () => {
  let settled = false;
  const terminalReload = reloadSettingsRuntimeTerminally(
    async () => undefined,
  );
  void terminalReload.then(
    () => {
      settled = true;
    },
    () => {
      settled = true;
    },
  );

  await Promise.resolve();
  await Promise.resolve();
  assert.equal(settled, false);

  await assert.rejects(
    () =>
      reloadSettingsRuntimeTerminally(async () => {
        throw new Error("RELOAD_FAILED");
      }),
    /RELOAD_FAILED/u,
  );
});
