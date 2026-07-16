import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const moduleDir = dirname(fileURLToPath(import.meta.url));

async function readMobileSource(relativePath: string) {
  return readFile(resolve(moduleDir, "../../..", relativePath), "utf8").catch(
    (error: NodeJS.ErrnoException) => {
      if (error.code === "ENOENT") return "";
      throw error;
    }
  );
}

function assertOrdered(source: string, first: string, second: string, message: string) {
  const firstIndex = source.indexOf(first);
  const secondIndex = source.indexOf(second);
  assert.notEqual(firstIndex, -1, `缺少前置守卫：${first}`);
  assert.notEqual(secondIndex, -1, `缺少受保护操作：${second}`);
  assert.ok(firstIndex < secondIndex, message);
}

async function run() {
  const [authStore, loginScreen, apiClient, rootLayout, tabsLayout, printerHook, banner, deviceStore, helpers] =
    await Promise.all([
      readMobileSource("src/store/auth-store.ts"),
      readMobileSource("app/(auth)/login.tsx"),
      readMobileSource("src/shared/api/client.ts"),
      readMobileSource("app/_layout.tsx"),
      readMobileSource("app/(tabs)/_layout.tsx"),
      readMobileSource("src/modules/printer/use-printer-auto-connect.ts"),
      readMobileSource("src/modules/ios-review/IosReviewBanner.tsx"),
      readMobileSource("src/store/device-store.ts"),
      readMobileSource("src/modules/ios-review/helpers.ts"),
    ]);

  assert.match(authStore, /sessionKind:\s*AuthSessionKind/);
  assertOrdered(
    authStore,
    "tryAuthenticateIosReview",
    "loginApi(",
    "本地审核认证必须先于真实登录请求"
  );
  assert.match(authStore, /hydrateIosReviewSession:\s*\(\)\s*=>\s*Promise<boolean>/);
  assert.match(authStore, /iosReviewOfflineGuardActive:\s*boolean/);
  assert.match(authStore, /beginStandardAuthentication/);
  assert.match(authStore, /clearAccountSessionForDeviceLogin/);
  assert.match(authStore, /rearmIosReviewPreAuth/);
  assert.match(authStore, /clearLocalSessionInFlight/);
  assert.match(authStore, /export async function waitForLocalSessionClear/);
  assert.match(authStore, /SecureStorage\.getToken\(\)/);
  assert.match(authStore, /DeviceStorage\.getSession\(\)/);
  assert.match(authStore, /IOS_REVIEW_MENU_ITEMS/);
  assert.equal(
    authStore.match(/stopAttendanceLocationTracking\(\{ force: true }\)/g)?.length,
    2,
    "审核登录和会话恢复都必须强制停止遗留后台定位"
  );
  assert.match(authStore, /wasIosReview \? \{ force: true } : undefined/);

  const loginHandler = loginScreen.slice(loginScreen.indexOf("async function handleLogin"));
  assertOrdered(
    loginHandler,
    "isReviewLoginAttempt",
    "collectOptionalLoginDeviceLocation",
    "审核账号提交必须先于定位采集分流"
  );
  assert.match(loginScreen, /shouldRunLoginSideEffects/);
  assert.match(loginScreen, /!iosReviewOfflineGuardActive/);
  assert.match(loginScreen, /beginStandardAuth\(\)/);
  assert.match(loginScreen, /clearAccountSessionForDeviceLogin/);
  assert.match(loginScreen, /rearmIosReviewPreAuth\(\)/);
  assert.match(loginScreen, /deviceLookupGeneration/);
  assert.match(loginScreen, /handleUsernameChange/);
  assert.match(loginScreen, /onChangeText={handleUsernameChange}/);
  assert.match(loginScreen, /handleSelectUserMode/);
  assert.doesNotMatch(
    loginScreen,
    /onPress={\(\) => setLoginMode\("user"\)}/,
    "切回用户模式必须统一经过 rearm 与设备查询失效逻辑"
  );
  assert.match(loginScreen, /handleSelectDeviceMode/);

  const authLoginSource = authStore.slice(
    authStore.indexOf("async login(payload)"),
    authStore.indexOf("async logout()")
  );
  assertOrdered(
    authLoginSource,
    "isIosReviewUsername(payload.username)",
    "await waitForLocalSessionClear()",
    "review 用户名必须在第一个 await 前同步恢复离线守卫"
  );
  assertOrdered(
    authLoginSource,
    "await waitForLocalSessionClear()",
    "tryAuthenticateIosReview",
    "认证必须等待旧本地清理完成后再建立新会话"
  );
  assert.match(
    authLoginSource,
    /status === "authenticated"[\s\S]*useDeviceStore\.setState\(\{[\s\S]*session:\s*null/,
    "审核登录成功必须只清设备内存展示态"
  );

  const screenLoginSource = loginScreen.slice(
    loginScreen.indexOf("async function handleLogin"),
    loginScreen.indexOf("async function handleDeviceLogin")
  );
  assertOrdered(
    screenLoginSource,
    "rearmIosReviewPreAuth()",
    "await waitForLocalSessionClear()",
    "有效或错误审核登录都必须先同步隔离，再等待清理锁"
  );
  assertOrdered(
    screenLoginSource,
    "await waitForLocalSessionClear()",
    "collectOptionalLoginDeviceLocation",
    "普通账号定位和登录必须等待旧清理完成"
  );

  const deviceLoginSource = loginScreen.slice(
    loginScreen.indexOf("async function handleDeviceLogin"),
    loginScreen.indexOf("const deviceStatusText")
  );
  assertOrdered(
    deviceLoginSource,
    "await waitForLocalSessionClear()",
    "beginStandardAuth(\"device\")",
    "设备登录必须等待旧清理完成后才解除网络守卫"
  );

  const deviceLookupSource = loginScreen.slice(
    loginScreen.indexOf("async function identifyRegisteredDevice"),
    loginScreen.indexOf("async function handleSaveApiHost")
  );
  assert.match(deviceLookupSource, /requestGeneration !== deviceLookupGeneration\.current/g);
  assert.match(
    deviceLookupSource,
    /await getDeviceProfileApi[\s\S]*if \(requestGeneration !== deviceLookupGeneration\.current\)/,
    "设备查询返回后必须先检查 generation 再写 UI"
  );
  const loadApiHostSource = loginScreen.slice(
    loginScreen.indexOf("async function loadApiHost"),
    loginScreen.indexOf("async function identifyRegisteredDevice")
  );
  assert.doesNotMatch(
    loadApiHostSource,
    /identifyRegisteredDevice/,
    "读取本地 API host 时不能自动触发设备网络探测"
  );
  const requestInterceptor = apiClient.slice(
    apiClient.indexOf("apiClient.interceptors.request.use")
  );
  assertOrdered(
    requestInterceptor,
    "isIosReviewSessionActive()",
    "syncApiBaseUrl()",
    "审核 adapter 必须在 base URL、token 和设备认证之前短路"
  );
  assert.match(apiClient, /config\.adapter\s*=\s*iosReviewAxiosAdapter/);

  assert.match(rootLayout, /hydrateIosReviewSession\(\)/);
  const prepareAppSource = rootLayout.slice(rootLayout.indexOf("async function prepareApp"));
  assertOrdered(
    prepareAppSource,
    "await hydrateIosReviewSession()",
    "await useDeviceStore.getState().hydrate()",
    "启动时必须先恢复审核 marker，再读取普通设备会话"
  );
  assert.match(rootLayout, /sideEffectsEnabled/);
  assert.match(rootLayout, /!iosReviewOfflineGuardActive/);
  assert.match(rootLayout, /usePrinterAutoConnect\(\{\s*enabled:\s*sideEffectsEnabled\s*}\)/);
  assert.match(rootLayout, /<IosReviewBanner\s*\/>/);

  assert.match(tabsLayout, /sessionKind\s*===\s*"iosReview"/);
  assert.match(tabsLayout, /enabled:\s*!isIosReviewSession\s*&&\s*heartbeatReady/);
  assertOrdered(
    tabsLayout,
    "if (isIosReviewSession)",
    "validateStoredDeviceForHeartbeat",
    "审核会话必须先短路设备验证和心跳"
  );

  assert.match(printerHook, /enabled\s*=\s*true/);
  assert.match(printerHook, /if\s*\(!enabled\)\s*{\s*return;/);

  const clearLocalSessionSource = authStore.slice(
    authStore.indexOf("async clearLocalSession()"),
    authStore.indexOf("setSessionKind(kind)")
  );
  assertOrdered(
    clearLocalSessionSource,
    "clearIosReviewSession()",
    "beginStandardAuthentication()",
    "设备登录清理 marker 后必须再次解除 pre-auth 守卫"
  );
  assertOrdered(
    clearLocalSessionSource,
    "queryClient.clear()",
    "clearIosReviewSession()",
    "审核退出必须先清查询与认证 UI，再删除 marker"
  );
  assert.match(
    clearLocalSessionSource,
    /finally\s*{[\s\S]*SecureStorage\.clearAll\(\)/,
    "marker 删除失败时 finally 仍必须清真实 token"
  );
  assert.doesNotMatch(
    clearLocalSessionSource,
    /DeviceStorage\.clearSession\(\)/,
    "审核退出必须保留既有真实设备绑定"
  );
  const performLocalClearInitialState = clearLocalSessionSource.slice(
    clearLocalSessionSource.indexOf("async performLocalSessionClear()"),
    clearLocalSessionSource.indexOf("await stopAttendanceLocationTracking"),
  );
  assert.match(
    performLocalClearInitialState,
    /iosReviewOfflineGuardActive:\s*iosReviewBuildEnabled\s*\|\|\s*isIosReviewSessionActive\(\)/,
    "已保存设备清理账号阶段必须保持 Root 副作用守卫直到验证成功",
  );
  assert.match(
    authStore,
    /if \(!token\) \{[\s\S]*?rearmIosReviewPreAuth\(\)[\s\S]*?return false;/,
    "普通会话恢复无 token 时必须重新锁定 review build 的全局网络 gate",
  );

  assert.match(
    deviceStore,
    /isIosReviewAuthenticatedSessionActive\(\)/,
    "设备本地 hydrate 必须区分 pre-auth 守卫与已认证审核会话"
  );

  const beginStandardAuthSource = authStore.slice(
    authStore.indexOf("beginStandardAuth(kind"),
    authStore.indexOf("setSessionKind(kind)")
  );
  assert.match(
    beginStandardAuthSource,
    /iosReviewBuildEnabled\s*\|\|\s*isIosReviewSessionActive\(\)/,
    "review 构建的显式登录意图不能提前启动 Root 后台副作用"
  );
  assert.match(
    tabsLayout,
    /if \(isReady && !cancelled\) \{[\s\S]*setSessionKind\("device"\)/,
    "已保存设备必须验证成功后才标记普通设备会话完成"
  );

  assert.match(
    banner,
    /IOS_REVIEW_BANNER\.(title|description)/
  );
  assert.match(banner, /accessibilityRole="alert"/);
  assert.match(banner, /IOS_REVIEW_BANNER\.accessibilityLabel/);
  assert.match(helpers, /App Review Demo \/ 审核演示/);
  assert.match(helpers, /Local sample data \/ 本地样例数据/);
  assert.match(helpers, /Resets on restart or sign-out \/ 重启或退出后重置/);
}

run().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
