import assert from "node:assert/strict";
import {
  IOS_NATIVE_UPDATE_CENTER_BASE_URL,
  resolveIosNativeUpdateCenterBaseUrl,
} from "./ios-native-update-center";

function run() {
  assert.equal(
    IOS_NATIVE_UPDATE_CENTER_BASE_URL,
    "https://hotbargain.vip/api",
    "正式 iOS 包必须使用固定的中央公开更新决策地址",
  );

  assert.equal(
    resolveIosNativeUpdateCenterBaseUrl({
      buildProfile: "production",
      override: "https://192.168.31.247:5002/api",
    }),
    IOS_NATIVE_UPDATE_CENTER_BASE_URL,
    "正式包绝不能继承局域网或业务 API Host",
  );

  assert.equal(
    resolveIosNativeUpdateCenterBaseUrl({
      buildProfile: " production ",
      override: "https://attacker.example/api",
    }),
    IOS_NATIVE_UPDATE_CENTER_BASE_URL,
    "生产 profile 的可变 override 必须被忽略",
  );

  assert.equal(
    resolveIosNativeUpdateCenterBaseUrl({
      buildProfile: "development",
      override: " https://updates.example.test/api/ ",
    }),
    "https://updates.example.test/api",
    "非生产环境只接受规范化的显式 HTTPS 更新中心地址",
  );

  assert.equal(
    resolveIosNativeUpdateCenterBaseUrl({
      buildProfile: "preview",
      override: undefined,
    }),
    IOS_NATIVE_UPDATE_CENTER_BASE_URL,
    "没有显式 override 时，非生产环境也只可回退中央 HTTPS 地址",
  );

  for (const override of [
    "http://updates.example.test/api",
    "https://attacker@updates.example.test/api",
    "https://name:secret@updates.example.test/api",
    "https://updates.example.test/api?redirect=http://attacker.test",
  ]) {
    assert.throws(
      () => resolveIosNativeUpdateCenterBaseUrl({ buildProfile: "development", override }),
      /trusted/i,
      `非生产 override 必须拒绝不可信地址：${override}`,
    );
  }

  console.log("ios-native-update-center.test.ts: ok");
}

run();
