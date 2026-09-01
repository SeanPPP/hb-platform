import assert from "node:assert/strict";
import test from "node:test";

import {
  normalizeMobileDeviceAccountToken,
  normalizeMobileDeviceActivationCommitResult,
} from "./device-activation-contract";

test("绑定响应严格把 bindingId 解析为 GUID 字符串", () => {
  const result = normalizeMobileDeviceActivationCommitResult({
    isAllowed: true,
    reasonCode: "OK",
    message: "bound",
    binding: {
      bindingId: "8b82e1d8-c435-4c1f-98fe-a4e513f4cc39",
      deviceRegistrationId: 11,
      deviceCode: "MOB-001",
      storeCode: "BNE01",
      storeName: "Brisbane",
      deviceSystem: "iOS",
      targetUserGuid: "legacy-user-00042",
      targetUsername: "alice",
      targetFullName: "Alice",
      boundAtUtc: "2026-08-31T10:00:00Z",
    },
  });

  assert.equal(result.binding?.bindingId, "8b82e1d8-c435-4c1f-98fe-a4e513f4cc39");
  assert.equal(
    normalizeMobileDeviceActivationCommitResult({
      isAllowed: true,
      binding: { bindingId: 123 },
    }).binding,
    null,
  );
});

test("设备账号 exchange 严格要求短令牌和 deviceAccount 会话类型", () => {
  assert.deepEqual(
    normalizeMobileDeviceAccountToken({
      accessToken: "short-jwt",
      expiresAtUtc: "2026-08-31T10:15:00Z",
      tokenType: "Bearer",
      sessionKind: "deviceAccount",
      user: {
        userGuid: "3ff60594-e237-4a80-8642-4dbb0d915b4d",
        username: "alice",
        fullName: "Alice",
        roles: ["Manager"],
        stores: [
          {
            storeGuid: "07d9b040-f3bc-48a9-9d40-ea847f8f61b8",
            storeCode: "BNE01",
            storeName: "Brisbane",
            isPrimary: true,
          },
        ],
      },
    }),
    {
      accessToken: "short-jwt",
      expiresAtUtc: "2026-08-31T10:15:00Z",
      tokenType: "Bearer",
      sessionKind: "deviceAccount",
      user: {
        userGuid: "3ff60594-e237-4a80-8642-4dbb0d915b4d",
        username: "alice",
        fullName: "Alice",
        roles: ["Manager"],
        stores: [
          {
            storeGuid: "07d9b040-f3bc-48a9-9d40-ea847f8f61b8",
            storeCode: "BNE01",
            storeName: "Brisbane",
            isPrimary: true,
          },
        ],
      },
    },
  );

  assert.throws(
    () => normalizeMobileDeviceAccountToken({ accessToken: "jwt", sessionKind: "account" }),
    /DEVICE_ACCOUNT_EXCHANGE_INVALID_RESPONSE/,
  );
});

test("历史非 UUID user/store 标识可建立设备账号会话", () => {
  const result = normalizeMobileDeviceAccountToken({
    accessToken: "short-jwt",
    expiresAtUtc: "2026-08-31T10:15:00Z",
    tokenType: "Bearer",
    sessionKind: "deviceAccount",
    user: {
      userGuid: "legacy-user-00042",
      username: "legacy.user",
      roles: ["StoreStaff"],
      stores: [
        {
          storeGuid: "legacy-store-BNE01",
          storeCode: "BNE01",
          storeName: "Brisbane",
          isPrimary: true,
        },
      ],
    },
  });

  assert.equal(result.user.userGuid, "legacy-user-00042");
  assert.equal(result.user.stores[0]?.storeGuid, "legacy-store-BNE01");
});
