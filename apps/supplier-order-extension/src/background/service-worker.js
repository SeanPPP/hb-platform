// 后台 Service Worker：网站会话交接、短期令牌存储、消息路由、动态内容脚本注册
import { isAuthFailure } from '../lib/api-response.js';
import {
  LOCAL_API_ORIGIN,
  normalizeApiOrigin,
  resolveApiOrigin,
  toApiHostPattern,
} from '../lib/api-origin.js';
import { resolveGrantedProfileOrigins } from '../lib/origin-registration.js';
import { DEFAULT_PROFILES } from '../lib/profiles-default.js';
import { migrateProfileConfig } from '../lib/profile-cache.js';
import { matchProfile, validateProfiles } from '../lib/profiles.js';
import { createAssistantPanelController } from '../lib/assistant-panel.js';
import { normalizeRankingDays, normalizeTopSalesRequest } from '../lib/ranking.js';
import {
  createAccessRequestExecutor,
  createSingleFlight,
  parseTokenResponse,
  validateWebsiteSessionContext,
  WEBSITE_SESSION_CLIENT_ID,
} from '../lib/session-handoff.js';
import {
  API_BASE,
  BUILD_TARGET,
  EXTENSION_VERSION,
  HB_WEB_ORIGIN,
} from '../config.js';

const ACCESS_KEY = 'websiteAccessToken';
const ACCESS_EXPIRY_KEY = 'websiteAccessTokenExpiry';
const USER_KEY = 'websiteSessionUser';
const PENDING_HANDOFF_KEY = 'pendingWebsiteSessionHandoff';
const LEGACY_ACCESS_KEY = 'accessToken';
const LEGACY_REFRESH_KEY = 'refreshToken';
const PROFILES_KEY = 'supplierProfiles';
const GRANTED_KEY = 'grantedOrigins';
const API_ORIGIN_KEY = 'apiOrigin';
const assistantPanel = createAssistantPanelController({ browserApi: chrome, buildTarget: BUILD_TARGET });
assistantPanel.registerListeners();

const getSession = (keys) => chrome.storage.session.get(keys);
const setSession = (obj) => chrome.storage.session.set(obj);
const removeSession = (keys) => chrome.storage.session.remove(keys);
const getLocal = (keys) => chrome.storage.local.get(keys);
const setLocal = (obj) => chrome.storage.local.set(obj);
const removeLocal = (keys) => chrome.storage.local.remove(keys);

async function getAccessToken() {
  const stored = await getSession([ACCESS_KEY, ACCESS_EXPIRY_KEY]);
  const token = stored[ACCESS_KEY];
  const expiry = Date.parse(stored[ACCESS_EXPIRY_KEY] || '');
  if (!token || !Number.isFinite(expiry) || expiry <= Date.now() + 5_000) {
    if (token || stored[ACCESS_EXPIRY_KEY]) await clearAccessSession();
    return null;
  }
  return token;
}

async function clearAccessSession() {
  await removeSession([ACCESS_KEY, ACCESS_EXPIRY_KEY, USER_KEY]);
}

async function getStoredSessionUser() {
  const stored = await getSession(USER_KEY);
  const value = stored[USER_KEY];
  if (
    !value
    || typeof value !== 'object'
    || typeof value.userGuid !== 'string'
    || !value.userGuid.trim()
    || !(
      (typeof value.username === 'string' && value.username.trim())
      || (typeof value.fullName === 'string' && value.fullName.trim())
    )
  ) {
    return null;
  }

  return {
    userGuid: value.userGuid.trim(),
    ...(typeof value.username === 'string' && value.username.trim()
      ? { username: value.username.trim() }
      : {}),
    ...(typeof value.fullName === 'string' && value.fullName.trim()
      ? { fullName: value.fullName.trim() }
      : {}),
  };
}

async function clearLegacyCredentials() {
  // 旧版曾保存 refresh token；升级后只删除，后续代码不再读取或写入。
  await Promise.all([
    removeSession([LEGACY_ACCESS_KEY]),
    removeLocal([LEGACY_REFRESH_KEY]),
  ]);
}

async function getApiOrigin() {
  const stored = await getLocal(API_ORIGIN_KEY);
  return resolveApiOrigin(stored[API_ORIGIN_KEY], API_BASE);
}

// 直接 fetch 并解析 ApiResponse 信封，返回统一结构
async function rawFetch(path, options = {}, { anonymous = false } = {}) {
  const [accessToken, apiOrigin] = await Promise.all([
    anonymous ? null : getAccessToken(),
    getApiOrigin(),
  ]);
  const headers = {
    'X-HB-Extension-Version': EXTENSION_VERSION,
    ...(options.body ? { 'Content-Type': 'application/json' } : {}),
    ...(options.headers || {}),
  };
  if (accessToken) headers.Authorization = `Bearer ${accessToken}`;
  const res = await fetch(`${apiOrigin}${path}`, {
    ...options,
    credentials: 'omit',
    headers,
  });
  let body = null;
  try {
    body = await res.json();
  } catch {
    // 非 JSON 响应按业务失败处理
  }
  return {
    httpStatus: res.status,
    ok: res.ok,
    success: body && body.success,
    data: body && body.data,
    message: body && body.message,
    errorCode: body && body.errorCode,
  };
}

async function handleGetApiOrigin() {
  return {
    ok: true,
    apiOrigin: await getApiOrigin(),
    defaultApiOrigin: API_BASE,
    localApiOrigin: LOCAL_API_ORIGIN,
  };
}

async function handleSetApiOrigin({ apiOrigin }) {
  const normalized = normalizeApiOrigin(apiOrigin, API_BASE);
  if (!normalized) return { ok: false, error: '接口地址无效' };

  const pattern = toApiHostPattern(normalized);
  if (!pattern || !(await chrome.permissions.contains({ origins: [pattern] }))) {
    return { ok: false, error: '接口地址尚未获得浏览器授权' };
  }

  const current = await getApiOrigin();
  if (normalized === current) {
    return { ok: true, apiOrigin: normalized, changed: false, requiresWebsiteSession: false };
  }

  // 环境切换时清除旧环境短期令牌和供应商缓存，避免跨环境传递授权。
  await setLocal({ [API_ORIGIN_KEY]: normalized, [PROFILES_KEY]: DEFAULT_PROFILES });
  await Promise.all([clearAccessSession(), removeSession(PENDING_HANDOFF_KEY)]);
  await syncContentScripts();
  return { ok: true, apiOrigin: normalized, changed: true, requiresWebsiteSession: true };
}

const accessRequestExecutor = createAccessRequestExecutor({
  isAuthFailure: (r) => isAuthFailure(r, r.httpStatus),
  clearAccessSession,
});

// 统一请求：401/业务鉴权失败只清理扩展会话，不刷新或退出网站会话。
async function apiRequest(path, options = {}) {
  if (!(await getAccessToken())) {
    const handoff = await ensureWebsiteSession();
    if (!handoff.ok) {
      return {
        httpStatus: 401,
        ok: false,
        success: false,
        message: handoff.error,
        errorCode: handoff.reason || 'WEBSITE_SESSION_REQUIRED',
      };
    }
  }
  return accessRequestExecutor(() => rawFetch(path, options));
}

function validateGrantMessage(message) {
  return message?.clientId === WEBSITE_SESSION_CLIENT_ID
    && typeof message.code === 'string'
    && message.code.length >= 16
    && message.code.length <= 512
    && typeof message.codeVerifier === 'string'
    && /^[A-Za-z0-9_-]{43,128}$/u.test(message.codeVerifier)
    && typeof message.state === 'string'
    && /^[A-Za-z0-9_-]{32,128}$/u.test(message.state);
}

async function exchangeWebsiteSessionGrant(message, sender) {
  const apiOrigin = await getApiOrigin();
  const senderUrl = sender?.tab?.url || sender?.url;
  const context = validateWebsiteSessionContext({
    pageUrl: senderUrl,
    webOrigin: HB_WEB_ORIGIN,
    apiOrigin,
    isTopLevel: sender?.frameId == null || sender.frameId === 0,
  });
  if (!context.ok || !validateGrantMessage(message)) {
    return {
      ok: false,
      reason: context.reason || 'INVALID_WEBSITE_SESSION_GRANT',
      error: '网站会话授权来源无效',
    };
  }

  const res = await rawFetch('/api/Auth/extension/token', {
    method: 'POST',
    body: JSON.stringify({
      code: message.code,
      codeVerifier: message.codeVerifier,
      state: message.state,
      clientId: WEBSITE_SESSION_CLIENT_ID,
    }),
  }, { anonymous: true });
  const parsed = parseTokenResponse({
    httpOk: res.ok,
    body: {
      success: res.success,
      data: res.data,
      message: res.message,
      errorCode: res.errorCode,
    },
  });
  if (!parsed.ok) {
    await clearAccessSession();
    return { ok: false, reason: parsed.reason, error: parsed.error };
  }

  await setSession({
    [ACCESS_KEY]: parsed.accessToken,
    [ACCESS_EXPIRY_KEY]: parsed.accessTokenExpiry,
    [USER_KEY]: parsed.user,
  });
  await removeSession(PENDING_HANDOFF_KEY);
  return { ok: true, user: parsed.user, accessTokenExpiry: parsed.accessTokenExpiry };
}

const acceptWebsiteSessionGrant = createSingleFlight(exchangeWebsiteSessionGrant);

async function findTrustedShopTabs() {
  const tabs = await chrome.tabs.query({ url: `${HB_WEB_ORIGIN}/shop*` });
  return tabs.filter((tab) => {
    const context = validateWebsiteSessionContext({
      pageUrl: tab.url,
      webOrigin: HB_WEB_ORIGIN,
      apiOrigin: HB_WEB_ORIGIN,
      isTopLevel: true,
    });
    return tab.id != null && context.ok;
  });
}

async function requestWebsiteSessionFromTab() {
  const apiOrigin = await getApiOrigin();
  if (apiOrigin !== HB_WEB_ORIGIN) {
    return {
      ok: false,
      reason: 'API_ORIGIN_MISMATCH',
      error: '当前接口与 HB SHOP 网页不同源',
      loginUrl: `${HB_WEB_ORIGIN}/shop`,
    };
  }

  const tabs = await findTrustedShopTabs();
  if (!tabs.length) {
    return {
      ok: false,
      reason: 'WEBSITE_TAB_REQUIRED',
      error: '请打开或登录 HB SHOP',
      loginUrl: `${HB_WEB_ORIGIN}/shop`,
    };
  }

  let lastFailure = null;
  for (const tab of tabs) {
    try {
      const result = await chrome.tabs.sendMessage(tab.id, {
        type: 'REQUEST_WEBSITE_SESSION',
        apiOrigin,
      });
      if (result?.ok) return result;
      lastFailure = result;
    } catch (error) {
      lastFailure = { error: String(error?.message || error) };
    }
  }
  return {
    ok: false,
    reason: lastFailure?.reason || 'WEBSITE_BRIDGE_UNAVAILABLE',
    error: lastFailure?.error || 'HB SHOP 授权桥尚未就绪',
    loginUrl: `${HB_WEB_ORIGIN}/shop`,
  };
}

const ensureWebsiteSession = createSingleFlight(async () => {
  if (await getAccessToken()) return { ok: true };
  return requestWebsiteSessionFromTab();
});

async function handleCurrent() {
  if (await getAccessToken()) {
    const existingUser = await getStoredSessionUser();
    if (existingUser) return { ok: true, user: existingUser };

    // 升级或异常存储状态缺少最小身份时，重新执行完整的一次性交接。
    await clearAccessSession();
  }

  const handoff = await ensureWebsiteSession();
  if (!handoff.ok) return handoff;

  const currentUser = handoff.user || await getStoredSessionUser();
  if (!currentUser) {
    await clearAccessSession();
    return {
      ok: false,
      reason: 'INVALID_TOKEN_RESPONSE',
      error: '网站会话返回的账号信息无效',
      loginUrl: `${HB_WEB_ORIGIN}/shop`,
    };
  }
  return { ok: true, user: currentUser };
}

async function handleDisconnect() {
  await Promise.all([clearAccessSession(), removeSession(PENDING_HANDOFF_KEY)]);
  return { ok: true };
}

async function handleGetProfiles() {
  const storedConfig = await migrateStoredProfiles();
  const storedValidation = validateProfiles(storedConfig);
  let config = storedValidation.valid
    ? {
        configVersion: storedConfig.configVersion ?? '1',
        profiles: storedValidation.profiles,
      }
    : DEFAULT_PROFILES;
  let source = storedValidation.valid ? 'cache' : 'default';
  try {
    const res = await apiRequest('/api/react/v1/browser-extension/supplier-profiles', { method: 'GET' });
    if (res.success && res.data && Array.isArray(res.data.profiles)) {
      const v = validateProfiles(res.data);
      if (v.valid) {
        // 空数组是正式的后台停用信号，绝不能回退内置 DATS。
        config = {
          configVersion: res.data.configVersion ?? '1',
          profiles: v.profiles,
        };
        source = 'server';
      } else {
        // 非法远程配置采取 fail-closed，不继续使用可能已被后台停用的旧配置。
        config = { configVersion: res.data.configVersion ?? 'invalid', profiles: [] };
        source = 'invalid-server';
      }
    }
  } catch {
    // 未登录或服务端暂不可用时沿用最近一次已验证配置。
  }
  await setLocal({ [PROFILES_KEY]: config });
  await syncContentScripts();
  return { ok: true, profiles: config.profiles, configVersion: config.configVersion, source };
}

async function migrateStoredProfiles() {
  const { [PROFILES_KEY]: storedConfig } = await getLocal(PROFILES_KEY);
  const migrated = migrateProfileConfig(storedConfig);
  if (migrated !== storedConfig) await setLocal({ [PROFILES_KEY]: migrated });
  return migrated;
}

async function handleRelease() {
  const res = await apiRequest('/api/react/v1/browser-extension/release', { method: 'GET' });
  if (!res.success) return { ok: false, error: res.message || res.errorCode || '获取版本失败' };
  return { ok: true, release: res.data };
}

async function handleSummaryBatch({ storeCode, supplierCode, itemNumbers, salesRankingDays }) {
  if (!storeCode || !supplierCode || !Array.isArray(itemNumbers)) {
    return { ok: false, error: '参数缺失' };
  }
  const res = await apiRequest('/api/react/v1/browser-extension/product-purchase-cycle-summary/batch', {
    method: 'POST',
    body: JSON.stringify({
      storeCode,
      supplierCode,
      itemNumbers,
      salesRankingDays: normalizeRankingDays(salesRankingDays),
    }),
  });
  if (!res.success) return { ok: false, error: res.message || res.errorCode || '摘要获取失败' };
  return { ok: true, data: res.data };
}

async function handlePurchaseCycles({ storeCode, supplierCode, itemNumber }) {
  if (!storeCode || !supplierCode || !itemNumber) {
    return { ok: false, error: '参数缺失' };
  }
  const res = await apiRequest('/api/react/v1/browser-extension/product-purchase-cycles', {
    method: 'POST',
    body: JSON.stringify({ storeCode, supplierCode, itemNumber }),
  });
  if (!res.success) return { ok: false, error: res.message || res.errorCode || '采购周期获取失败' };
  return { ok: true, data: res.data };
}

async function handleStores() {
  const res = await apiRequest('/api/react/v1/browser-extension/stores', { method: 'GET' });
  if (!res.success) return { ok: false, error: res.message || res.errorCode || '门店获取失败' };
  return { ok: true, data: res.data };
}

async function handleSupplierTopSales({ supplierCode, days, topPercent, page, pageSize }) {
  if (!supplierCode) return { ok: false, error: '供应商代码缺失' };
  let pagination;
  try {
    pagination = normalizeTopSalesRequest({ topPercent, page, pageSize });
  } catch (error) {
    return { ok: false, error: error.message };
  }
  const res = await apiRequest('/api/react/v1/browser-extension/supplier-top-sales', {
    method: 'POST',
    body: JSON.stringify({
      supplierCode,
      days: normalizeRankingDays(days),
      ...(pagination || {}),
    }),
  });
  if (!res.success) return { ok: false, error: res.message || res.errorCode || '热销排行获取失败' };
  return { ok: true, data: res.data, apiOrigin: await getApiOrigin() };
}

async function handleActiveSupplier() {
  let tabs = [];
  try {
    tabs = await assistantPanel.queryActiveTabs();
  } catch {
    return { ok: true, supplier: null };
  }
  const href = tabs[0] && tabs[0].url;
  if (!href) return { ok: true, supplier: null };

  let url;
  try {
    url = new URL(href);
  } catch {
    return { ok: true, supplier: null };
  }
  const storedConfig = await migrateStoredProfiles();
  const validation = validateProfiles(storedConfig);
  const profile = validation.valid
    ? matchProfile(validation.profiles, { origin: url.origin, pathname: url.pathname })
    : null;
  return {
    ok: true,
    supplier: profile
      ? { supplierCode: profile.supplierCode, displayName: profile.displayName }
      : null,
  };
}

// 根据已授权 origin 同步动态内容脚本
async function syncContentScripts() {
  const stored = await getLocal([GRANTED_KEY, PROFILES_KEY]);
  const granted = Array.isArray(stored[GRANTED_KEY]) ? stored[GRANTED_KEY] : [];
  const validation = validateProfiles(stored[PROFILES_KEY]);
  const origins = await resolveGrantedProfileOrigins(
    validation.valid ? validation.profiles : [],
    (origin) => chrome.permissions.contains({ origins: [origin] }),
  );
  if (origins.length !== granted.length || origins.some((origin, index) => origin !== granted[index])) {
    await setLocal({ [GRANTED_KEY]: origins });
  }
  try {
    await chrome.scripting.unregisterContentScripts({ ids: ['hb-supplier-list'] });
  } catch {
    // 无动态脚本时忽略
  }
  if (origins.length) {
    await chrome.scripting.registerContentScripts([
      {
        id: 'hb-supplier-list',
        matches: origins,
        js: ['content/list.js'],
        runAt: 'document_idle',
        allFrames: false,
      },
    ]);
  }
}

async function handleRegisterOrigin({ originPattern }) {
  if (typeof originPattern !== 'string' || !originPattern) {
    return { ok: false, error: 'origin 缺失' };
  }
  const stored = await getLocal([GRANTED_KEY, PROFILES_KEY]);
  const validation = validateProfiles(stored[PROFILES_KEY]);
  const allowedOrigins = new Set(
    (validation.valid ? validation.profiles : [])
      .filter((profile) => profile.enabled !== false)
      .flatMap((profile) => profile.origins || []),
  );
  if (!allowedOrigins.has(originPattern)) {
    return { ok: false, error: 'origin 不在已启用供应商配置中' };
  }
  if (!(await chrome.permissions.contains({ origins: [originPattern] }))) {
    return { ok: false, error: 'origin 尚未获得浏览器授权' };
  }
  const granted = stored[GRANTED_KEY];
  const set = new Set(Array.isArray(granted) ? granted : []);
  set.add(originPattern);
  const grantedList = [...set];
  await setLocal({ [GRANTED_KEY]: grantedList });
  await syncContentScripts();
  return { ok: true, granted: grantedList };
}

function friendlySidePanelError(e) {
  const msg = String((e && e.message) || e);
  if (/user gesture/i.test(msg)) return '需要用户操作';
  if (/tab/i.test(msg)) return '未找到标签页';
  return msg || '打开侧栏失败';
}

async function focusTab(tab) {
  if (tab.windowId != null && chrome.windows?.update) {
    try {
      await chrome.windows.update(tab.windowId, { focused: true });
    } catch {
      // Safari 或受限窗口环境不支持聚焦时，激活标签页仍可继续。
    }
  }
  return chrome.tabs.update(tab.id, { active: true });
}

async function handleOpenHbShop() {
  await setSession({ [PENDING_HANDOFF_KEY]: true });

  const shopTabs = await findTrustedShopTabs();
  if (shopTabs.length) {
    await focusTab(shopTabs[0]);
    const handoff = await ensureWebsiteSession();
    return { ok: true, connected: !!handoff.ok, reason: handoff.reason };
  }

  const webTabs = await chrome.tabs.query({ url: `${HB_WEB_ORIGIN}/*` });
  const existing = webTabs.find((tab) => tab.id != null);
  if (existing) {
    await chrome.tabs.update(existing.id, { url: `${HB_WEB_ORIGIN}/shop`, active: true });
    if (existing.windowId != null && chrome.windows?.update) {
      try {
        await chrome.windows.update(existing.windowId, { focused: true });
      } catch {
        // 标签页导航已经成功，窗口聚焦失败不影响授权。
      }
    }
    return { ok: true, connected: false, pending: true };
  }

  await chrome.tabs.create({ url: `${HB_WEB_ORIGIN}/shop`, active: true });
  return { ok: true, connected: false, pending: true };
}

const openHbShop = createSingleFlight(handleOpenHbShop);

async function handleShopBridgeReady(sender) {
  const senderUrl = sender?.tab?.url || sender?.url;
  const source = validateWebsiteSessionContext({
    pageUrl: senderUrl,
    webOrigin: HB_WEB_ORIGIN,
    apiOrigin: HB_WEB_ORIGIN,
    isTopLevel: sender?.frameId == null || sender.frameId === 0,
  });
  if (!source.ok) return { ok: false, reason: source.reason };

  const [{ [PENDING_HANDOFF_KEY]: pending }, apiOrigin] = await Promise.all([
    getSession(PENDING_HANDOFF_KEY),
    getApiOrigin(),
  ]);
  return {
    ok: true,
    shouldAuthorize: pending === true,
    apiOrigin,
  };
}

function openSidePanel(sender, pendingLocate) {
  const tabId = sender && sender.tab && sender.tab.id;
  if (tabId == null) return Promise.resolve({ ok: false, error: '缺少标签页' });
  // 先同步调用 open 保留用户手势，再异步返回状态
  const openPromise = assistantPanel.open({ tabId });
  const locatePromise = pendingLocate
    ? chrome.storage.session.set({ pendingLocate })
    : Promise.resolve();
  return Promise.all([openPromise, locatePromise])
    .then(() => ({ ok: true }))
    .catch((e) => ({ ok: false, error: friendlySidePanelError(e) }));
}

chrome.runtime.onInstalled.addListener(async () => {
  await clearLegacyCredentials();
  try {
    await assistantPanel.configureAction();
  } catch {
    // 某些环境不支持，忽略
  }
  const existing = await migrateStoredProfiles();
  if (!existing) await setLocal({ [PROFILES_KEY]: DEFAULT_PROFILES });
  await syncContentScripts();
});

chrome.runtime.onStartup.addListener(async () => {
  await clearLegacyCredentials();
  await migrateStoredProfiles();
  await syncContentScripts();
});

// Service Worker 被浏览器直接唤醒时也执行幂等迁移，确保旧长期凭据立即消失。
void clearLegacyCredentials().catch(() => {});

chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  const type = message && message.type;
  const run = async () => {
    switch (type) {
      case 'GET_API_ORIGIN':
        return handleGetApiOrigin();
      case 'SET_API_ORIGIN':
        return handleSetApiOrigin(message);
      case 'CURRENT':
        return handleCurrent();
      case 'WEBSITE_SESSION_GRANT':
        return acceptWebsiteSessionGrant(message, sender);
      case 'SHOP_BRIDGE_READY':
        return handleShopBridgeReady(sender);
      case 'DISCONNECT':
        return handleDisconnect();
      case 'OPEN_HB_SHOP':
        return openHbShop();
      case 'RELEASE':
        return handleRelease();
      case 'GET_PROFILES':
        return handleGetProfiles();
      case 'SUMMARY_BATCH':
        return handleSummaryBatch(message);
      case 'PURCHASE_CYCLES':
        return handlePurchaseCycles(message);
      case 'GET_STORES':
        return handleStores();
      case 'SUPPLIER_TOP_SALES':
        return handleSupplierTopSales(message);
      case 'ACTIVE_SUPPLIER':
        return handleActiveSupplier();
      case 'REGISTER_ORIGIN':
        return handleRegisterOrigin(message);
      case 'OPEN_SIDE_PANEL':
        return openSidePanel(sender);
      case 'LOCATE_ITEM':
        return openSidePanel(sender, {
          storeCode: message.storeCode,
          supplierCode: message.supplierCode,
          itemNumber: message.itemNumber,
        });
      default:
        return { ok: false, error: '未知消息类型' };
    }
  };
  run()
    .then(sendResponse)
    .catch((err) => sendResponse({ ok: false, error: String((err && err.message) || err) }));
  return true;
});
