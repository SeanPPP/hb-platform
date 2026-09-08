import assert from "node:assert/strict";
import test from "node:test";
import {
  createAppDownloadsApi,
  type AppDownloadsTransport,
} from "./api-contract";

function harness(reply: unknown) {
  const calls: { method: string; url: string; value: unknown }[] = [];
  const transport: AppDownloadsTransport = {
    async get(url, value) {
      calls.push({ method: "GET", url, value });
      return { data: reply };
    },
    async post(url, value) {
      calls.push({ method: "POST", url, value });
      return { data: reply };
    },
    async put(url, value) {
      calls.push({ method: "PUT", url, value });
      return { data: reply };
    },
  };
  return { api: createAppDownloadsApi(transport), calls };
}

test("安装包完整镜像字段、分页与 /api 相对路径", async () => {
  for (const key of ["items", "list", "data"]) {
    const { api, calls } = harness({
      success: true,
      data: {
        [key]: [
          {
            id: "build",
            appKey: "pos-handheld",
            artifactUrl: "https://example.test/a.apk",
            originalArtifactUrl: "https://expo.dev/a.apk",
            cosArtifactUrl: "https://example.test/a.apk",
            cosObjectKey: "builds/a.apk",
            cosMirrorStatus: "Succeeded",
            cosMirrorError: null,
            cosMirroredAt: "2026-09-06",
            gitCommitHash: "abc",
            buildDetailsPageUrl: "https://expo.dev/build/1",
          },
        ],
        total: 45,
        page: 2,
        pageSize: 20,
      },
    });
    const result = await api.getBuilds("pos-handheld", "preview", 2);
    assert.equal(result.total, 45);
    assert.equal(result.page, 2);
    assert.equal(result.items[0].cosMirrorStatus, "Succeeded");
    assert.equal(result.items[0].cosObjectKey, "builds/a.apk");
    assert.equal(result.items[0].originalArtifactUrl, "https://expo.dev/a.apk");
    assert.equal(result.items[0].gitCommitHash, "abc");
    assert.deepEqual(calls[0], {
      method: "GET",
      url: "/mobile-app-builds",
      value: {
        params: {
          appKey: "pos-handheld",
          profile: "preview",
          page: 2,
          pageSize: 20,
        },
      },
    });
  }
});

test("空的最新版本返回 null，不能生成假版本", async () => {
  assert.equal(
    await harness({ success: true, data: null }).api.getLatestBuild(
      "mobile",
      "production",
    ),
    null,
  );
});

test("Mobile OTA 候选严格隔离 app、environment、platform、clientChannel", async () => {
  const good = {
    id: "good",
    appKey: "mobile",
    environment: "preview",
    platform: "ios",
    clientChannel: "preview",
    runtimeVersion: "runtime",
    updateGroupId: "group",
  };
  const rows = [
    good,
    { ...good, id: "wrong-app", appKey: "pos-handheld" },
    { ...good, environment: "production" },
    { ...good, platform: "android" },
    { ...good, clientChannel: "production" },
  ];
  assert.deepEqual(
    (
      await harness({
        success: true,
        data: { items: rows },
      }).api.getMobileOtaReleases("preview", "ios")
    ).map((item) => item.id),
    ["good"],
  );
});

test("POS Handheld 四个候选通道与 iOS App Store 链接", async () => {
  for (const platform of ["android", "ios"] as const)
    for (const kind of ["native", "ota"] as const) {
      const row = {
        id: "candidate",
        platform,
        kind,
        artifactUrl: "https://apps.apple.com/au/app/id1",
        activatable: true,
      };
      const { api, calls } = harness({
        success: true,
        data: {
          items: [
            row,
            {
              ...row,
              id: "wrong-lane",
              lane: platform === "ios" ? "android-native" : "ios-native",
            },
          ],
        },
      });
      const result = await api.getHandheldCandidates(platform, kind);
      assert.equal(result.length, 1);
      assert.equal(result[0].lane, `${platform}-${kind}`);
      assert.equal(result[0].activatable, true);
      if (platform === "ios" && kind === "native")
        assert.equal(result[0].appStoreUrl, row.artifactUrl);
      assert.equal(
        calls[0].url,
        kind === "native"
          ? `/app-update-policies/pos-handheld/candidates/native/${platform}`
          : "/app-update-policies/pos-handheld/candidates/ota",
      );
    }
});

test("Handheld 策略与历史保留 lane 和 policyVersion", async () => {
  const policies = [
    {
      lane: "ios-ota",
      enabled: true,
      required: true,
      policyVersion: 12,
      candidateId: "candidate",
      candidateValid: false,
      blockedReason: "fingerprint_mismatch",
    },
  ];
  const result = await harness({
    success: true,
    data: { policies },
  }).api.getHandheldPolicies();
  assert.equal(result[0].candidateValid, false);
  assert.equal(result[0].policyVersion, 12);
  const revisions = await harness({
    success: true,
    data: {
      items: [
        {
          id: "revision",
          lane: "ios-ota",
          policyVersion: 12,
          snapshot: { enabled: true },
        },
      ],
    },
  }).api.getHandheldRevisions("ios-ota");
  assert.equal(revisions[0].lane, "ios-ota");
  assert.deepEqual(JSON.parse(revisions[0].snapshotJson), { enabled: true });
});

test("版本登记仅发登记接口，不触碰策略", async () => {
  const { api, calls } = harness({
    success: true,
    data: { id: "ios", app: "pos-ipad", buildNumber: "47" },
  });
  await api.registerIosRelease({
    app: "pos-ipad",
    appStoreId: "1",
    buildNumber: "47",
    storefront: "au",
  });
  assert.equal(calls.length, 1);
  assert.equal(calls[0].url, "/app-update-releases/ios");
  assert.equal(calls[0].method, "POST");
});

test("所有策略写入保留 expectedPolicyVersion 并使用对应通道", async () => {
  const payload = { expectedPolicyVersion: 7, enabled: false, releaseId: null };
  const { api, calls } = harness({ success: true, data: {} });
  await api.saveNativePolicy("mobile-ios", payload);
  await api.saveNativePolicy("pos-ipad", payload);
  await api.saveIpadOtaRollout(payload);
  await api.saveMobileOtaPolicy("preview", "ios", payload);
  await api.saveHandheldPolicy("ios-ota", payload);
  assert.deepEqual(
    calls.map((call) => call.url),
    [
      "/app-update-policies/mobile-ios",
      "/app-update-policies/pos-ipad/native",
      "/pos-ipad/ota-rollout",
      "/app-update-policies/mobile-ota/preview/ios",
      "/app-update-policies/pos-handheld/ios-ota",
    ],
  );
  assert.ok(
    calls.every((call) => call.method === "PUT" && call.value === payload),
  );
});

test("HTTP 200 的业务失败仍抛出，并保留并发冲突错误码", async () => {
  const { api } = harness({
    success: false,
    message: "conflict",
    errorCode: "APP_UPDATE_POLICY_VERSION_CONFLICT",
    data: null,
  });
  await assert.rejects(
    api.saveNativePolicy("mobile-ios", {}),
    (error: unknown) =>
      (error as { code?: string }).code ===
      "APP_UPDATE_POLICY_VERSION_CONFLICT",
  );
  await assert.rejects(api.getBuilds("mobile", "production"), /conflict/);
});

test("OTA 安装历史支持第二页查询", async () => {
  const { api, calls } = harness({
    success: true,
    data: { items: [], total: 21, page: 2, pageSize: 20 },
  });
  const result = await api.getOtaBuildHistory(
    "pos-handheld",
    "production",
    "runtime",
    2,
  );
  assert.equal(result.page, 2);
  assert.deepEqual(calls[0].value, {
    params: {
      page: 2,
      pageSize: 20,
      appKey: "pos-handheld",
      channel: "pos-handheld-production",
      runtimeVersion: "runtime",
    },
  });
});
