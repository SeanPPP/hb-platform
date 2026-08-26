import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import path from "node:path";
import test from "node:test";

import {
  resolveSentryConfiguration,
  sanitizeSentryEvent,
} from "./sentry-config";

test("Sentry runtime 固定关闭默认 PII 并设置 release/dist/environment", () => {
  const configuration = resolveSentryConfiguration({
    dsn: "https://public@example.ingest.sentry.io/1",
    appIdentifier: "com.hbweb.posipad",
    appVersion: "0.2.0",
    buildNumber: "42",
    environment: "production",
  });

  assert.equal(configuration.enabled, true);
  assert.deepEqual(configuration.options, {
    dsn: "https://public@example.ingest.sentry.io/1",
    enabled: true,
    release: "com.hbweb.posipad@0.2.0",
    dist: "42",
    environment: "production",
    sendDefaultPii: false,
  });
  assert.equal(
    resolveSentryConfiguration({
      dsn: " ",
      appIdentifier: "com.hbweb.posipad",
      appVersion: "0.2.0",
      buildNumber: null,
      environment: "development",
    }).enabled,
    false,
  );
});

test("Sentry beforeSend 清洗请求、身份、秘密和业务标识", () => {
  const serialized = JSON.stringify(
    sanitizeSentryEvent({
      message: "provider failed token=provider-secret",
      user: { email: "cashier@example.test" },
      request: {
        headers: { authorization: "Bearer auth-secret" },
      },
      extra: {
        barcode: "930000000001",
        safe: "checkout failed",
      },
    }),
  );

  for (const secret of [
    "provider-secret",
    "cashier@example.test",
    "auth-secret",
    "930000000001",
  ]) {
    assert.equal(serialized.includes(secret), false);
  }
  assert.equal(serialized.includes("checkout failed"), true);
});

test("Expo/Sentry 配置使用独立项目、env DSN 与 sourcemap 命令，仓库不含真实秘密", () => {
  const root = process.cwd();
  const packageJson = JSON.parse(
    readFileSync(path.join(root, "package.json"), "utf8"),
  );
  const metro = readFileSync(path.join(root, "metro.config.js"), "utf8");
  const appConfig = readFileSync(path.join(root, "app.config.ts"), "utf8");
  const environment = readFileSync(path.join(root, ".env.example"), "utf8");
  const eas = JSON.parse(readFileSync(path.join(root, "eas.json"), "utf8"));
  const combined = [metro, appConfig, environment].join("\n");

  assert.equal(
    typeof packageJson.dependencies?.["@sentry/react-native"],
    "string",
  );
  assert.equal(
    packageJson.scripts?.["sentry:upload-sourcemaps"],
    "sentry-expo-upload-sourcemaps dist",
  );
  assert.match(metro, /getSentryExpoConfig/u);
  assert.match(appConfig, /@sentry\/react-native\/expo/u);
  assert.match(environment, /^EXPO_PUBLIC_HBPOS_SENTRY_DSN=$/mu);
  assert.match(environment, /^SENTRY_PROJECT=hb-pos-ipad$/mu);
  assert.equal(eas.build.production.environment, "production");
  assert.doesNotMatch(combined, /https:\/\/[A-Za-z0-9]+@[^/\s]+\/\d+/u);
  assert.doesNotMatch(environment, /^SENTRY_AUTH_TOKEN=\S+/mu);
});
