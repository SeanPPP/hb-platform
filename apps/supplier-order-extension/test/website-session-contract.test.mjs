import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const read = (path) => readFileSync(path, 'utf8');

test('三种浏览器共享网站会话源码且 manifest 不申请 Cookie 权限', () => {
  const build = read('build.mjs');
  const chromeManifest = read('src/manifest.template.json');
  const safariManifest = read('src/manifest.safari.template.json');

  assert.ok(build.includes("const TARGETS = ['chrome', 'edge', 'safari']"));
  assert.ok(build.includes("join('content', 'shop-bridge.js')"));
  assert.ok(!chromeManifest.includes('"cookies"'));
  assert.ok(!safariManifest.includes('"cookies"'));
});

test('侧栏不包含凭据输入，只呈现网站会话操作', () => {
  const html = read('src/sidepanel/sidepanel.html');
  const sidepanel = read('src/sidepanel/sidepanel.js');
  const readme = read('README.md');

  assert.ok(!html.includes('type="password"'));
  assert.ok(!html.includes('autocomplete="username"'));
  assert.ok(!html.includes('id="loginBtn"'));
  for (const id of ['authStatusTitle', 'authStatusDescription', 'openShopBtn', 'recheckBtn', 'disconnectBtn']) {
    assert.ok(html.includes(`id="${id}"`), `侧栏缺少 ${id}`);
  }
  assert.ok(sidepanel.includes("type: 'OPEN_HB_SHOP'"));
  assert.ok(sidepanel.includes("type: 'DISCONNECT'"));
  assert.ok(!sidepanel.includes("type: 'LOGIN'"));
  assert.match(html, /<section id="apiSection"[^>]* hidden>/u);
  assert.ok(!readme.includes('“本地 5002”'));
});

test('后台只保存 session access token，不再登录、刷新或退出网站会话', () => {
  const worker = read('src/background/service-worker.js');

  assert.ok(worker.includes("const ACCESS_KEY = 'websiteAccessToken'"));
  assert.match(worker, /setSession\(\{\s*\[ACCESS_KEY\]/u);
  assert.ok(worker.includes("case 'WEBSITE_SESSION_GRANT':"));
  assert.ok(worker.includes("case 'OPEN_HB_SHOP':"));
  assert.ok(worker.includes('createSingleFlight'));
  for (const forbidden of [
    '/api/Auth/login',
    '/api/Auth/refresh',
    '/api/Auth/session/refresh',
    '/api/Auth/logout',
    '/api/Auth/session/logout',
  ]) {
    assert.ok(!worker.includes(forbidden), `后台仍包含禁用端点 ${forbidden}`);
  }
  assert.ok(!worker.includes('setLocal({ [REFRESH_KEY]'));
  assert.ok(!worker.includes('passwordFormat'));
});

test('CURRENT 使用网站 /shop 交接并在 401 时清理扩展会话', () => {
  const worker = read('src/background/service-worker.js');
  const currentStart = worker.indexOf('async function handleCurrent()');
  const currentEnd = worker.indexOf('async function handleGetProfiles()', currentStart);
  const current = worker.slice(currentStart, currentEnd);

  assert.ok(currentStart >= 0 && currentEnd > currentStart);
  assert.ok(current.includes('ensureWebsiteSession'));
  assert.ok(current.includes('getStoredSessionUser'));
  assert.ok(worker.includes("'/api/Auth/extension/token'"));
  assert.ok(worker.includes('chrome.tabs.sendMessage'));
  assert.ok(worker.includes('clearAccessSession'));
  assert.ok(worker.includes('isAuthFailure'));
  assert.ok(worker.includes("const USER_KEY = 'websiteSessionUser'"));
  assert.ok(!worker.includes("'/api/Auth/current'"));
});

test('所有受保护业务请求在短期 token 缺失时先执行 single-flight 网站授权', () => {
  const worker = read('src/background/service-worker.js');
  const requestStart = worker.indexOf('async function apiRequest(');
  const requestEnd = worker.indexOf('function validateGrantMessage', requestStart);
  const request = worker.slice(requestStart, requestEnd);

  assert.ok(request.includes('ensureWebsiteSession()'));
  assert.ok(request.includes('accessRequestExecutor'));
  for (const handler of [
    'handleGetProfiles',
    'handleRelease',
    'handleSummaryBatch',
    'handlePurchaseCycles',
    'handleStores',
    'handleSupplierTopSales',
  ]) {
    const start = worker.indexOf(`async function ${handler}`);
    const end = worker.indexOf('\n}', start);
    assert.ok(start >= 0 && worker.slice(start, end).includes('apiRequest('), `${handler} 未走统一授权请求`);
  }
});

test('/shop 桥在顶层同源页面以 Cookie 授权，秘密仅走 runtime 内部消息', () => {
  const bridge = read('src/content/shop-bridge.js');

  assert.ok(bridge.includes('/api/Auth/extension/authorize'));
  assert.ok(bridge.includes('/api/Auth/session/refresh'));
  assert.ok(bridge.includes("credentials: 'include'"));
  assert.ok(bridge.includes('window === window.top'));
  assert.ok(bridge.includes("type: 'WEBSITE_SESSION_GRANT'"));
  assert.ok(bridge.includes("message?.type !== 'REQUEST_WEBSITE_SESSION'"));
  assert.ok(!bridge.includes('codeVerifier,' + '\n' + '          expectedOrigin'));

  const postMessageCalls = [...bridge.matchAll(/postMessage\(([\s\S]*?)expectedOrigin,?\s*\)/g)];
  assert.ok(postMessageCalls.length >= 2);
  for (const call of postMessageCalls) {
    assert.ok(!/codeVerifier|accessToken|refreshToken|username|fullName/.test(call[1]));
  }

  const worker = read('src/background/service-worker.js');
  assert.ok(!worker.includes('/api/Auth/session/refresh'));
});

test('API 地址切换清理扩展令牌并要求重新检查网站会话', () => {
  const worker = read('src/background/service-worker.js');
  const sidepanel = read('src/sidepanel/sidepanel.js');

  assert.ok(worker.includes('await clearAccessSession()'));
  assert.ok(sidepanel.includes('connectFromWebsiteSession'));
  assert.ok(sidepanel.includes("t(locale, 'apiOriginMismatch')"));
});

test('短期 token 被 401 清除后侧栏立即退出已连接状态', () => {
  const sidepanel = read('src/sidepanel/sidepanel.js');
  const listenerStart = sidepanel.indexOf('chrome.storage.onChanged.addListener');
  const listenerEnd = sidepanel.indexOf('chrome.tabs.onActivated.addListener', listenerStart);
  const listener = sidepanel.slice(listenerStart, listenerEnd);

  assert.ok(listener.includes('tokenChange?.oldValue && !tokenChange.newValue'));
  assert.ok(listener.includes('resetAuthenticatedData()'));
  assert.ok(listener.includes("authState = 'needsWebsite'"));
});
