import assert from "node:assert/strict";
import { fetchIosNativeUpdateDecision } from "./ios-native-app-update-api";

const context = {
  apiBaseUrl: "https://hotbargain.vip/api",
  installedVersion: "1.0.2",
  installedBuild: "28",
};

async function fetchDecision(data: unknown) {
  return fetchIosNativeUpdateDecision(
    context,
    async () => ({
      data: {
        success: true,
        data,
      },
    }),
  );
}

async function run() {
  let request:
    | {
        url: string;
        config: {
          params?: Record<string, unknown>;
          timeout?: number;
          headers?: Record<string, string>;
        };
      }
    | undefined;

  const decision = await fetchIosNativeUpdateDecision(
    {
      apiBaseUrl: "https://hotbargain.vip/api/",
      installedVersion: "1.0.2",
      installedBuild: "28",
    },
    async (url, config) => {
      request = { url, config };
      return {
        data: {
          success: true,
          data: {
            state: "none",
            policyVersion: "none",
            latestVersion: null,
            minimumSupportedVersion: null,
            appStoreUrl: null,
            releaseMessage: null,
          },
        },
      };
    },
  );

  assert.deepEqual(decision, {
    state: "none",
    policyVersion: "none",
    latestVersion: null,
    minimumSupportedVersion: null,
    appStoreUrl: null,
    releaseMessage: null,
  });
  assert.equal(request?.url, "https://hotbargain.vip/api/app-updates/mobile-ios");
  assert.deepEqual(request?.config.params, {
    version: "1.0.2",
    build: "28",
  });
  assert.equal(request?.config.timeout, 8_000, "启动检查必须有独立短超时，失败后按缓存策略处理");
  assert.equal(
    request?.config.headers?.Accept,
    "application/json",
    "公开决策请求不应携带登录或设备凭据",
  );

  assert.deepEqual(
    await fetchDecision({
      state: "required",
      policyVersion: "12",
      latestVersion: "1.1.0",
      minimumSupportedVersion: "1.0.3",
      appStoreUrl: "https://apps.apple.com/au/app/example/id123",
      releaseMessage: "必须升级",
    }),
    {
      state: "required",
      policyVersion: "12",
      latestVersion: "1.1.0",
      minimumSupportedVersion: "1.0.3",
      appStoreUrl: "https://apps.apple.com/au/app/example/id123",
      releaseMessage: "必须升级",
    },
    "完整且状态一致的强制决策必须通过公开合同校验",
  );

  await assert.rejects(
    () =>
      fetchIosNativeUpdateDecision(
        {
          apiBaseUrl: "https://hotbargain.vip/api",
          installedVersion: "1.0.2",
          installedBuild: "28",
        },
        async () => ({
          data: {
            success: false,
            message: "policy unavailable",
          },
        }),
      ),
    /policy unavailable/,
    "标准 ApiResponse 失败 envelope 必须抛错，不能归一化成放行决策",
  );

  const malformedDecisions: ReadonlyArray<{
    name: string;
    data: Record<string, unknown>;
  }> = [
    {
      name: "缺少字段",
      data: {
        state: "none",
        policyVersion: "none",
        latestVersion: null,
        minimumSupportedVersion: null,
        appStoreUrl: null,
      },
    },
    {
      name: "包含额外字段",
      data: {
        state: "none",
        policyVersion: "none",
        latestVersion: null,
        minimumSupportedVersion: null,
        appStoreUrl: null,
        releaseMessage: null,
        forceUpdate: true,
      },
    },
    {
      name: "none 状态携带非空发布字段",
      data: {
        state: "none",
        policyVersion: "none",
        latestVersion: "1.0.3",
        minimumSupportedVersion: null,
        appStoreUrl: null,
        releaseMessage: null,
      },
    },
    {
      name: "none 状态携带活动策略版本",
      data: {
        state: "none",
        policyVersion: "12",
        latestVersion: null,
        minimumSupportedVersion: null,
        appStoreUrl: null,
        releaseMessage: null,
      },
    },
    {
      name: "optional 状态使用 none 策略版本",
      data: {
        state: "optional",
        policyVersion: "none",
        latestVersion: "1.0.3",
        minimumSupportedVersion: null,
        appStoreUrl: "https://apps.apple.com/au/app/example/id123",
        releaseMessage: null,
      },
    },
    {
      name: "required 状态缺少最低版本",
      data: {
        state: "required",
        policyVersion: "12",
        latestVersion: "1.0.3",
        minimumSupportedVersion: null,
        appStoreUrl: "https://apps.apple.com/au/app/example/id123",
        releaseMessage: null,
      },
    },
  ];

  for (const malformed of malformedDecisions) {
    await assert.rejects(
      () => fetchDecision(malformed.data),
      /decision/i,
      `${malformed.name}必须 fail-closed`,
    );
  }

  console.log("ios-native-app-update-api.test.ts: ok");
}

void run();
