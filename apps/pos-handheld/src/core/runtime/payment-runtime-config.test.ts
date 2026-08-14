import assert from "node:assert/strict";
import test from "node:test";

import {
  configuredCardProvider,
  configuredLinklyEnvironment,
  createPaymentConfigurationSources,
} from "./payment-runtime-config";

test("支付公开配置缺失时三个 provider 均保持明确不可配置状态", async () => {
  const sources = createPaymentConfigurationSources(undefined);

  assert.equal(await sources.square.load(), null);
  assert.equal(await sources.linkly.load(), null);
  assert.deepEqual(await sources.voucher.load(), { enabled: false });
  assert.equal(configuredLinklyEnvironment(undefined), null);
});

test("支付公开配置只映射环境和终端选择，不接受额外凭据字段", async () => {
  const sources = createPaymentConfigurationSources({
    square: {
      environment: "Sandbox",
      deviceId: " square-device ",
      locationId: " square-location ",
      accessToken: "must-not-be-read",
    } as never,
    linkly: {
      environment: "Production",
      secret: "must-not-be-read",
    } as never,
    voucher: {
      enabled: true,
      token: "must-not-be-read",
    } as never,
  });

  assert.deepEqual(await sources.square.load(), {
    environment: "Sandbox",
    deviceId: "square-device",
    locationId: "square-location",
  });
  assert.deepEqual(await sources.linkly.load(), {
    environment: "Production",
  });
  assert.deepEqual(await sources.voucher.load(), { enabled: true });
  assert.doesNotMatch(
    JSON.stringify({
      square: await sources.square.load(),
      linkly: await sources.linkly.load(),
      voucher: await sources.voucher.load(),
    }),
    /token|secret|credential/i,
  );
  assert.equal(
    configuredLinklyEnvironment({
      linkly: { environment: "Production" },
    }),
    "Production",
  );
});

test("非法 Linkly 环境不开放 operator runtime", () => {
  assert.equal(
    configuredLinklyEnvironment({
      linkly: { environment: "development" },
    }),
    null,
  );
});

test("新卡交易只接受显式选择的 Square 或 Linkly provider", () => {
  assert.equal(configuredCardProvider(undefined), null);
  assert.equal(configuredCardProvider({ provider: "invalid" }), null);
  assert.equal(configuredCardProvider({ provider: "square" }), "square");
  assert.equal(configuredCardProvider({ provider: "linkly" }), "linkly-cloud");
});
