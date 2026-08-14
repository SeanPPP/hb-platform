import assert from "node:assert/strict";
import test from "node:test";

import {
  createSettingsApiHealthProbe,
  reloadSettingsRuntimeTerminally,
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
        state: "required",
        policyVersion: "ios-required-110",
        platform: "iOS",
        required: true,
        latestVersion: "1.1.0",
        latestBuild: "110",
        minimumSupportedVersion: "1.0.0",
        distribution: "app-store",
        downloadUrl: "https://apps.apple.com/au/app/hb-pos/id123456789",
        fileSize: null,
        sha256: null,
        packageName: null,
        signingCertificateSha256: null,
        bundleIdentifier: "com.hbweb.poshandheld",
        appStoreId: "123456789",
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
