import assert from "node:assert/strict";
import { buildAppUpdateInfoRows, resolveAppUpdateCheckAvailability } from "./app-update-info";

function run() {
  const rows = buildAppUpdateInfoRows({
    appVersion: "1.0.1",
    runtimeVersion: "1.0.1",
    channel: "preview",
    updateId: "12345678-90ab-cdef-1234-567890abcdef",
    isEmbeddedLaunch: false,
  });

  assert.deepEqual(
    rows,
    [
      { key: "version", labelKey: "updates.version", value: "1.0.1" },
      { key: "runtime", labelKey: "updates.runtime", value: "1.0.1" },
      { key: "channel", labelKey: "updates.channel", value: "preview" },
      { key: "source", labelKey: "updates.source", valueKey: "updates.sourceOta" },
      { key: "updateId", labelKey: "updates.updateId", value: "12345678-90ab-cdef-1234-567890abcdef" },
    ],
    "OTA 更新状态应显示版本、runtime、渠道、来源和 updateId",
  );

  assert.deepEqual(
    buildAppUpdateInfoRows({
      appVersion: null,
      runtimeVersion: null,
      channel: null,
      updateId: null,
      isEmbeddedLaunch: true,
    }),
    [
      { key: "version", labelKey: "updates.version", valueKey: "updates.unknown" },
      { key: "runtime", labelKey: "updates.runtime", valueKey: "updates.unknown" },
      { key: "channel", labelKey: "updates.channel", valueKey: "updates.noChannel" },
      { key: "source", labelKey: "updates.source", valueKey: "updates.sourceEmbedded" },
      { key: "updateId", labelKey: "updates.updateId", valueKey: "updates.noUpdateId" },
    ],
    "内置包状态应对空值提供稳定文案 key",
  );

  assert.equal(
    resolveAppUpdateCheckAvailability({ isDev: false, isEnabled: true }),
    "available",
    "生产启用 OTA 时允许检查更新",
  );
  assert.equal(
    resolveAppUpdateCheckAvailability({ isDev: true, isEnabled: true }),
    "development-disabled",
    "开发模式不应调用 expo-updates 检查 API",
  );
  assert.equal(
    resolveAppUpdateCheckAvailability({ isDev: false, isEnabled: false }),
    "configuration-disabled",
    "生产未启用 OTA 应暴露为配置异常",
  );

  console.log("app-update-info.test.ts: ok");
}

run();
