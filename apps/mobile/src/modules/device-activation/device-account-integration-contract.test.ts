import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import path from "node:path";
import test from "node:test";

const mobileRoot = path.resolve(__dirname, "../../..");

function read(relativePath: string) {
  return readFileSync(path.join(mobileRoot, relativePath), "utf8");
}

test("设备账号绑定桥接旧 X-Device 认证，并在解绑时一起清理", () => {
  const operation = read("src/modules/device-activation/device-activation-operation.ts");
  const deviceStore = read("src/store/device-store.ts");

  assert.match(operation, /saveLegacyDeviceSession/);
  assert.match(operation, /authCode:\s*pending\.credential/);
  assert.match(deviceStore, /unbindAccountBinding[\s\S]*DeviceStorage\.clearSession\(\)/);
});

test("绑定服务器随安全凭据持久化，exchange 不读取当前可变服务器", () => {
  const tokenExchange = read("src/modules/device-activation/device-account-token.ts");
  const runtime = read("src/modules/device-activation/device-activation-runtime.ts");

  assert.match(tokenExchange, /binding\.apiHost/);
  assert.doesNotMatch(tokenExchange, /getStoredApiHost/);
  assert.match(runtime, /currentBinding\.apiHost/);
});

test("Shell 优先恢复设备账号，不把兼容桥接降级成固定 device 会话", () => {
  const shell = read("app/(shell)/_layout.tsx");

  assert.match(shell, /accountBinding/);
  assert.match(shell, /hasStoredDeviceAccountBinding/);
  assert.match(shell, /hasStoredDeviceSession\s*&&\s*!hasStoredDeviceAccountBinding/);
  assert.match(
    shell,
    /useEffect\(\(\) => \{\s*shellMounted\.current = true;[\s\S]*return \(\) => \{\s*shellMounted\.current = false;/,
  );
});

test("共享 API client 对设备账号固定绑定服务器，并在 host mismatch 时剥离设备头", () => {
  const client = read("src/shared/api/client.ts");

  assert.match(client, /resolveDeviceAccountRequestPolicy/);
  assert.match(client, /deriveEffectiveAuthSessionKind/);
  assert.match(client, /isRelativeApiClientUrl/);
  assert.match(client, /ABSOLUTE_API_URL_NOT_ALLOWED/);
  assert.match(client, /DeviceAccountStorage\.loadBinding/);
  assert.match(client, /SecureStorage\.getRefreshToken/);
  assert.match(client, /allowDeviceHeaders/);
  assert.match(client, /removeRequestHeader/);
  assert.match(
    client,
    /!requestPolicy\.allowBearerToken[\s\S]*DEVICE_ACCOUNT_BINDING_NOT_FOUND/,
  );
  assert.match(
    client,
    /!requestPolicy\.allowDeviceHeaders[\s\S]*removeRequestHeader\(config\.headers, "X-Device-Id"\)[\s\S]*removeRequestHeader\(config\.headers, "X-Auth-Code"\)/,
  );
});

test("rebind API 始终通过可降级的双凭据请求构建器提交", () => {
  const activationApi = read("src/modules/device-activation/device-activation-api.ts");

  assert.match(activationApi, /prepareMobileDeviceActivationCommitRequest/);
  assert.match(activationApi, /prepared\.skipAuthentication/);
  assert.match(activationApi, /prepared\.accessToken/);
  assert.doesNotMatch(
    activationApi,
    /recoveryOnly\s*\?\s*\{[\s\S]*currentHardwareId/,
  );
});

test("rebind pending 沿用旧 binding hardwareId", () => {
  const runtime = read("src/modules/device-activation/device-activation-runtime.ts");

  assert.match(runtime, /resolveActivationHardwareId/);
  assert.match(runtime, /hardwareId:\s*activationHardwareId/);
});

test("登录页检测到账号绑定时不再调用旧设备 profile 接口", () => {
  const login = read("app/(auth)/login.tsx");
  const identifyStart = login.indexOf("async function identifyRegisteredDevice");
  const identifyEnd = login.indexOf("async function handleSaveApiHost", identifyStart);
  const identifySource = login.slice(identifyStart, identifyEnd);

  assert.match(identifySource, /if \(accountBinding\)/);
  assert.match(identifySource, /setLoginMode\("deviceAccount"\)/);
  assert.match(identifySource, /return;/);
  assert.ok(
    identifySource.indexOf("if (accountBinding)") <
      identifySource.indexOf("getDeviceProfileApi"),
  );
  assert.match(
    login,
    /const selectedSessionKind = accountBinding \? "deviceAccount" : "device";/,
  );
  assert.match(login, /beginStandardAuth\(selectedSessionKind\)/);
  assert.match(
    login,
    /if \(accountBinding\) \{[\s\S]*?deviceLookupGeneration\.current \+= 1;[\s\S]*?setRegisteredDevice\(null\);[\s\S]*?setLoginMode\("deviceAccount"\);/,
  );
});

test("restoreSession 从 access 无 refresh 的崩溃窗口恢复设备账号", () => {
  const authStore = read("src/store/auth-store.ts");

  assert.match(authStore, /deriveEffectiveAuthSessionKind/);
  assert.match(authStore, /SecureStorage\.getRefreshToken/);
  assert.match(
    authStore,
    /effectiveSessionKind === "deviceAccount" && deviceAccountBinding/,
  );
});

test("loginDeviceAccount 在 exchange 前先恢复部分落盘的 pending", () => {
  const authStore = read("src/store/auth-store.ts");

  assert.match(authStore, /loadDeviceAccountBindingForLogin/);
  assert.match(authStore, /recoverStoredMobileDeviceActivation/);
  assert.ok(
    authStore.indexOf("loadDeviceAccountBindingForLogin") <
      authStore.indexOf("establishDeviceAccountSession(binding"),
  );
});
