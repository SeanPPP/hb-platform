import assert from "node:assert/strict";
import test from "node:test";

import { SecurityApiCredentialProvider } from "./api-credential-provider";
import { CashierSessionInvalidationBus } from "./cashier-session-invalidation";
import { DeviceSessionCoordinator, type DeviceSessionApi } from "./device-session";
import {
  CashierAuthorizationStore,
  DeviceCredentialStore,
  InMemorySecureStore,
  InstallationIdentityStore,
} from "./secure-storage";

function createProvider() {
  const secureStore = new InMemorySecureStore();
  const installation = new InstallationIdentityStore(secureStore, () => "INSTALL-001");
  const credentials = new DeviceCredentialStore(secureStore);
  const api: DeviceSessionApi = {
    async register() {
      throw new Error("not used");
    },
    async verify() {
      throw new Error("not used");
    },
    async reregister() {
      throw new Error("not used");
    },
  };
  const invalidation = new CashierSessionInvalidationBus();
  const authorization = new CashierAuthorizationStore(secureStore);
  const deviceSession = new DeviceSessionCoordinator(api, installation, credentials);
  return {
    authorization,
    credentials,
    deviceSession,
    invalidation,
    provider: new SecurityApiCredentialProvider(
      deviceSession,
      authorization,
      invalidation,
    ),
  };
}

test("401 只清除收银员授权，不移除设备授权或本地账本状态", async () => {
  const { authorization, credentials, provider } = createProvider();
  await credentials.save({ deviceCode: "POS-1", storeCode: "S1", hardwareId: "INSTALL-001", authorizationCode: "device-token" });
  await setAuthorization(authorization, "cashier-token");

  await provider.onUnauthorized();

  const result = await provider.getCredentials();
  assert.equal(result.cashierAuthorization, undefined);
  assert.equal(result.device?.authorizationCode, "device-token");
});

test("403 清除收银员授权并锁设备，后续不再提供设备认证头", async () => {
  const { authorization, credentials, provider } = createProvider();
  await credentials.save({ deviceCode: "POS-1", storeCode: "S1", hardwareId: "INSTALL-001", authorizationCode: "device-token" });
  await setAuthorization(authorization, "cashier-token");

  await provider.onForbidden();

  assert.deepEqual(await provider.getCredentials(), {});
});

test("401/403 只广播无秘密失效原因，监听器异常不影响安全清理", async () => {
  const { authorization, invalidation, provider } = createProvider();
  const reasons: string[] = [];
  invalidation.subscribe((reason) => reasons.push(reason));
  invalidation.subscribe(() => {
    throw new Error("broken UI listener");
  });

  await setAuthorization(authorization, "cashier-token-1");
  await provider.onUnauthorized();
  await setAuthorization(authorization, "cashier-token-2");
  await provider.onForbidden();

  assert.deepEqual(reasons, ["unauthorized", "forbidden"]);
  assert.equal(await authorization.get(), null);
});

test("401 清除收银员票据失败时仍广播失效，并保留原始异常", async () => {
  const { authorization, invalidation, provider } = createProvider();
  const clearError = new Error("Keychain clear failed");
  const reasons: string[] = [];
  invalidation.subscribe((reason) => reasons.push(reason));
  authorization.clear = async () => {
    throw clearError;
  };

  await assert.rejects(provider.onUnauthorized(), (error) => error === clearError);

  assert.deepEqual(reasons, ["unauthorized"]);
});

test("403 清除收银员票据失败时仍广播失效，并保留清除异常", async () => {
  const { authorization, invalidation, provider } = createProvider();
  const clearError = new Error("Keychain clear failed");
  const reasons: string[] = [];
  invalidation.subscribe((reason) => reasons.push(reason));
  authorization.clear = async () => {
    throw clearError;
  };

  await assert.rejects(provider.onForbidden(), (error) => error === clearError);

  assert.deepEqual(reasons, ["forbidden"]);
});

function setAuthorization(
  authorization: CashierAuthorizationStore,
  authorizationToken: string,
): Promise<void> {
  return authorization.set({
    authorizationToken,
    expiresAtEpochMs: Date.now() + 60_000,
    source: "online",
  });
}

test("403 设备锁写入失败时仍广播失效，并保留锁定异常", async () => {
  const { deviceSession, invalidation, provider } = createProvider();
  const lockError = new Error("device lock write failed");
  const reasons: string[] = [];
  invalidation.subscribe((reason) => reasons.push(reason));
  deviceSession.lockFromAuthorizationFailure = async () => {
    throw lockError;
  };

  await assert.rejects(provider.onForbidden(), (error) => error === lockError);

  assert.deepEqual(reasons, ["forbidden"]);
});

test("403 锁定和票据清除均失败时，以设备锁定异常为主且只广播一次", async () => {
  const { authorization, deviceSession, invalidation, provider } = createProvider();
  const lockError = new Error("device lock write failed");
  const reasons: string[] = [];
  invalidation.subscribe((reason) => reasons.push(reason));
  deviceSession.lockFromAuthorizationFailure = async () => {
    throw lockError;
  };
  authorization.clear = async () => {
    throw new Error("Keychain clear failed");
  };

  await assert.rejects(provider.onForbidden(), (error) => error === lockError);

  assert.deepEqual(reasons, ["forbidden"]);
});
