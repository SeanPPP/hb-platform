import assert from "node:assert/strict";
import test from "node:test";

import { establishDeviceAccountSession } from "./device-account-session";

test("设备账号 exchange 不创建 refresh token，并以真实账号加载权限、菜单和分店", async () => {
  const calls: string[] = [];
  const user = {
    userGuid: "user-guid",
    userGUID: "user-guid",
    username: "alice",
    email: "",
    permissions: ["Order.View"],
    roleNames: ["Manager"],
    storeNames: ["Brisbane", "Gold Coast"],
    stores: [
      { storeCode: "BNE01", storeName: "Brisbane" },
      { storeCode: "OOL01", storeName: "Gold Coast" },
    ],
  };

  const result = await establishDeviceAccountSession(
    {
      apiHost: "api.example.com",
      hardwareId: "hardware-1",
      credential: "private-credential",
    },
    {
      exchange: async () => {
        calls.push("exchange");
        return {
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
        };
      },
      saveAccessToken: async () => calls.push("save-access"),
      removeRefreshToken: async () => calls.push("remove-refresh"),
      loadCurrentUser: async () => {
        calls.push("load-user");
        return user;
      },
      saveCurrentUser: async () => calls.push("save-user"),
      markDeviceAccountSession: async () => calls.push("mark-kind"),
      loadNavigationMenu: async () => calls.push("load-menu"),
    },
  );

  assert.deepEqual(result, user);
  assert.deepEqual(calls, [
    "exchange",
    "save-access",
    "remove-refresh",
    "mark-kind",
    "load-user",
    "save-user",
    "load-menu",
  ]);
});
