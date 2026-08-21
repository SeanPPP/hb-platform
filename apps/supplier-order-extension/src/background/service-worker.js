// 后台 Service Worker：统一请求、token 存储、single-flight refresh、消息路由、动态内容脚本注册
import { isAuthFailure } from '../lib/api-response.js';
import {
  LOCAL_API_ORIGIN,
  normalizeApiOrigin,
  resolveApiOrigin,
  toApiHostPattern,
} from '../lib/api-origin.js';
import { createAuthExecutor } from '../lib/refresh-flow.js';
import { resolveGrantedProfileOrigins } from '../lib/origin-registration.js';
import { DEFAULT_PROFILES } from '../lib/profiles-default.js';
import { migrateProfileConfig } from '../lib/profile-cache.js';
import { matchProfile, validateProfiles } from '../lib/profiles.js';
import { createAssistantPanelController } from '../lib/assistant-panel.js';
import { API_BASE, BUILD_TARGET, EXTENSION_VERSION } from '../config.js';

const ACCESS_KEY = 'accessToken';
const REFRESH_KEY = 'refreshToken';
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
  const r = await getSession(ACCESS_KEY);
  return r[ACCESS_KEY];
}

async function getRefreshToken() {
  const r = await getLocal(REFRESH_KEY);
  return r[REFRESH_KEY];
}

async function getApiOrigin() {
  const stored = await getLocal(API_ORIGIN_KEY);
  return resolveApiOrigin(stored[API_ORIGIN_KEY], API_BASE);
}

// 直接 fetch 并解析 ApiResponse 信封，返回统一结构
async function rawFetch(path, options = {}) {
  const [accessToken, apiOrigin] = await Promise.all([getAccessToken(), getApiOrigin()]);
  const headers = {
    'X-HB-Extension-Version': EXTENSION_VERSION,
    ...(options.body ? { 'Content-Type': 'application/json' } : {}),
    ...(options.headers || {}),
  };
  if (accessToken) headers.Authorization = `Bearer ${accessToken}`;
  const res = await fetch(`${apiOrigin}${path}`, { ...options, headers });
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
    return { ok: true, apiOrigin: normalized, changed: false, requiresLogin: false };
  }

  // 环境切换时清除旧环境令牌和供应商缓存，避免跨环境传递凭据或沿用错误配置。
  await setLocal({ [API_ORIGIN_KEY]: normalized, [PROFILES_KEY]: DEFAULT_PROFILES });
  await Promise.all([removeSession([ACCESS_KEY]), removeLocal([REFRESH_KEY])]);
  await syncContentScripts();
  return { ok: true, apiOrigin: normalized, changed: true, requiresLogin: true };
}

// 用 refreshToken 换取新令牌（不经过 withRefresh，避免递归刷新）
async function doRefresh() {
  try {
    const [accessToken, refreshToken] = await Promise.all([getAccessToken(), getRefreshToken()]);
    if (!refreshToken) throw new Error('no refresh token');
    const res = await rawFetch('/api/Auth/refresh', {
      method: 'POST',
      body: JSON.stringify({ accessToken, refreshToken }),
    });
    if (!res.success || !res.data || !res.data.accessToken || !res.data.refreshToken) {
      throw new Error(res.message || res.errorCode || 'refresh failed');
    }
    await Promise.all([
      setSession({ [ACCESS_KEY]: res.data.accessToken }),
      setLocal({ [REFRESH_KEY]: res.data.refreshToken }),
    ]);
  } catch (error) {
    await Promise.all([removeSession([ACCESS_KEY]), removeLocal([REFRESH_KEY])]);
    throw error;
  }
}

const authExecutor = createAuthExecutor({
  isAuthFailure: (r) => isAuthFailure(r, r.httpStatus),
  refresh: doRefresh,
});

// 统一请求：401/业务鉴权失败时 single-flight refresh 后重试一次
function apiRequest(path, options = {}) {
  return authExecutor.withRefresh(() => rawFetch(path, options));
}

async function handleLogin({ username, password }) {
  if (typeof username !== 'string' || typeof password !== 'string' || !username || !password) {
    return { ok: false, error: '用户名或密码为空' };
  }
  const res = await rawFetch('/api/Auth/login', {
    method: 'POST',
    body: JSON.stringify({ username, password, passwordFormat: 'raw' }),
  });
  if (!res.success || !res.data || !res.data.accessToken) {
    return { ok: false, error: res.message || res.errorCode || '登录失败' };
  }
  // 不保存密码；access token 进 session，refresh token 进 local
  await setSession({ [ACCESS_KEY]: res.data.accessToken });
  if (res.data.refreshToken) await setLocal({ [REFRESH_KEY]: res.data.refreshToken });
  return { ok: true, user: res.data.user || res.data };
}

async function handleCurrent() {
  const res = await apiRequest('/api/Auth/current', { method: 'GET' });
  if (!res.success) return { ok: false, error: res.message || res.errorCode || '获取用户失败' };
  return { ok: true, user: res.data };
}

async function handleLogout() {
  try {
    // 每次请求都重新读取 refresh token：若 access 已过期，single-flight 刷新会先旋转
    // refresh token，重试退出时必须撤销旋转后的当前 token，不能继续提交旧值。
    await authExecutor.withRefresh(async () => {
      const refreshToken = await getRefreshToken();
      if (!refreshToken) return { httpStatus: 200, success: true };
      return rawFetch('/api/Auth/logout', {
        method: 'POST',
        body: JSON.stringify({ refreshToken }),
      });
    });
  } catch {
    // 忽略退出请求失败，仍清理本地令牌
  }
  await removeSession([ACCESS_KEY]);
  await removeLocal([REFRESH_KEY]);
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

async function handleSummaryBatch({ storeCode, supplierCode, itemNumbers }) {
  if (!storeCode || !supplierCode || !Array.isArray(itemNumbers)) {
    return { ok: false, error: '参数缺失' };
  }
  const res = await apiRequest('/api/react/v1/browser-extension/product-purchase-cycle-summary/batch', {
    method: 'POST',
    body: JSON.stringify({ storeCode, supplierCode, itemNumbers }),
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

async function handleSupplierTopSales({ supplierCode, days }) {
  if (!supplierCode) return { ok: false, error: '供应商代码缺失' };
  const normalizedDays = Number(days) === 90 ? 90 : 60;
  const res = await apiRequest('/api/react/v1/browser-extension/supplier-top-sales', {
    method: 'POST',
    body: JSON.stringify({ supplierCode, days: normalizedDays }),
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
  await migrateStoredProfiles();
  await syncContentScripts();
});

chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  const type = message && message.type;
  const run = async () => {
    switch (type) {
      case 'LOGIN':
        return handleLogin(message);
      case 'GET_API_ORIGIN':
        return handleGetApiOrigin();
      case 'SET_API_ORIGIN':
        return handleSetApiOrigin(message);
      case 'CURRENT':
        return handleCurrent();
      case 'LOGOUT':
        return handleLogout();
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
