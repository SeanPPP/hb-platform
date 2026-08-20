import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const read = (path) => readFileSync(path, 'utf8');

test('Web 与扩展 /shop 消息常量保持一致', () => {
  const web = read('../web/src/components/SupplierOrderingExtensionEntry/supplierOrderingExtensionLogic.ts');
  const extension = read('src/lib/handshake.js');
  const constants = [
    'hb-platform',
    'hb-supplier-ordering-extension',
    'HB_SUPPLIER_ASSISTANT_PING',
    'HB_SUPPLIER_ASSISTANT_OPEN',
    'HB_SUPPLIER_ASSISTANT_STATUS',
  ];

  for (const value of constants) {
    assert.ok(web.includes(value), `Web 缺少消息常量 ${value}`);
    assert.ok(extension.includes(value), `扩展缺少消息常量 ${value}`);
  }
});

test('扩展归一化字段覆盖后端正式摘要和采购周期 DTO', () => {
  const dto = read('../../services/backend/BlazorApp.Shared/DTOs/BrowserExtensionDtos.cs');
  const summary = read('src/lib/dats-state.js');
  const cycles = read('src/lib/pagination.js');

  for (const field of [
    'MatchStatus',
    'LatestPurchaseDate',
    'LatestPurchaseQuantity',
    'SalesSinceLatestPurchase',
  ]) {
    assert.ok(dto.includes(field), `后端 DTO 缺少 ${field}`);
    const camel = field[0].toLowerCase() + field.slice(1);
    assert.ok(summary.includes(camel), `扩展摘要归一化缺少 ${camel}`);
  }

  for (const field of [
    'PurchaseDate',
    'InvoiceNumbers',
    'PurchaseQuantity',
    'AveragePurchasePrice',
    'SalesStartDate',
    'SalesEndDate',
    'SalesQuantity',
    'AverageSalePrice',
  ]) {
    assert.ok(dto.includes(field), `后端 DTO 缺少 ${field}`);
    const camel = field[0].toLowerCase() + field.slice(1);
    assert.ok(cycles.includes(camel), `扩展周期归一化缺少 ${camel}`);
  }
});

test('扩展源码无远程代码执行入口且 manifest 不申请 tabs 权限', () => {
  const manifest = read('src/manifest.template.json');
  const profiles = read('src/lib/profiles.js');
  const transforms = read('src/lib/transforms.js');

  assert.ok(!manifest.includes('"tabs"'));
  assert.ok(!profiles.includes('eval('));
  assert.ok(!profiles.includes('new Function'));
  assert.ok(!transforms.includes('eval('));
  assert.ok(!transforms.includes('new Function'));
});

test('构建分别配置 HB Web 与 API 源，/shop 桥接不依赖 API 同源', () => {
  const build = read('build.mjs');
  const config = read('src/config.template.js');
  const manifest = read('src/manifest.template.json');

  assert.ok(build.includes('HB_WEB_ORIGIN'));
  assert.ok(build.includes('HB_API_ORIGIN'));
  assert.ok(config.includes('__HB_WEB_ORIGIN__'));
  assert.ok(config.includes('__HB_API_ORIGIN__'));
  assert.ok(manifest.includes('__WEB_ORIGIN__/*'));
  assert.ok(manifest.includes('__API_ORIGIN__/*'));
});

test('供应商目录请求携带扩展版本，旧客户端可由后端安全降级', () => {
  const worker = read('src/background/service-worker.js');
  assert.ok(worker.includes("'X-HB-Extension-Version': EXTENSION_VERSION"));
});

test('供应商页注入按钮保留可见键盘焦点', () => {
  const list = read('src/content/list.js');
  assert.ok(list.includes('.hb-btn:focus-visible'));
  assert.ok(list.includes("attachShadow({ mode: 'closed' })"));
});

test('供应商页摘要按钮完整显示并自动换行', () => {
  const list = read('src/content/list.js');
  const buttonStyle = list.match(/'\.hb-btn\{([^']+)\}'/)?.[1] ?? '';

  assert.ok(buttonStyle.includes('white-space:normal'));
  assert.ok(buttonStyle.includes('overflow-wrap:anywhere'));
  assert.ok(!buttonStyle.includes('white-space:nowrap'));
  assert.ok(!buttonStyle.includes('text-overflow:ellipsis'));
  assert.ok(!buttonStyle.includes('overflow:hidden'));
});

test('侧栏商品请求使用 generation guard，旧响应不能覆盖新商品', () => {
  const sidepanel = read('src/sidepanel/sidepanel.js');
  assert.ok(sidepanel.includes('createGenerationGuard'));
  assert.ok(sidepanel.includes('itemRequestGeneration.isCurrent'));
});

test('退出重试在请求闭包内重新读取旋转后的 refresh token', () => {
  const worker = read('src/background/service-worker.js');
  const start = worker.indexOf('async function handleLogout()');
  const end = worker.indexOf('async function handleGetProfiles()');
  const logout = worker.slice(start, end);
  assert.ok(start >= 0 && end > start);
  assert.ok(logout.includes('authExecutor.withRefresh(async () =>'));
  assert.ok(logout.indexOf('authExecutor.withRefresh') < logout.indexOf('getRefreshToken()'));
});

test('侧栏提供远端和本地 5002 后端地址快捷设置', () => {
  const html = read('src/sidepanel/sidepanel.html');
  const sidepanel = read('src/sidepanel/sidepanel.js');
  const worker = read('src/background/service-worker.js');
  const manifest = read('src/manifest.template.json');

  for (const id of ['apiOriginInput', 'apiRemoteBtn', 'apiLocalBtn', 'apiSaveBtn']) {
    assert.ok(html.includes(`id="${id}"`), `侧栏缺少 ${id}`);
  }
  assert.ok(sidepanel.includes("type: 'GET_API_ORIGIN'"));
  assert.ok(sidepanel.includes("type: 'SET_API_ORIGIN'"));
  assert.ok(worker.includes("case 'GET_API_ORIGIN':"));
  assert.ok(worker.includes("case 'SET_API_ORIGIN':"));
  assert.ok(worker.includes('removeSession([ACCESS_KEY])'));
  assert.ok(worker.includes('removeLocal([REFRESH_KEY])'));
  assert.ok(manifest.includes('http://localhost/*'));
  assert.ok(manifest.includes('http://127.0.0.1/*'));
});

test('TXK 明文 HTTP 权限仅开放给已核验的精确域名', () => {
  const manifest = read('src/manifest.template.json');

  assert.ok(manifest.includes('http://txkorders.inzantsales.com/*'));
  assert.ok(!manifest.includes('"http://*/*"'));
});

test('Top 10% 商品图片、名称和货号完整显示并允许窄屏换行', () => {
  const css = read('src/sidepanel/sidepanel.css');
  const sidepanel = read('src/sidepanel/sidepanel.js');
  const nameRule = css.match(/\.ranking-product strong\s*\{([^}]+)\}/)?.[1] ?? '';
  const codeRule = css.match(/\.ranking-code\s*\{([^}]+)\}/)?.[1] ?? '';
  const imageRule = css.match(/\.ranking-image\s*\{([^}]+)\}/)?.[1] ?? '';

  assert.ok(!nameRule.includes('-webkit-line-clamp'));
  assert.ok(!nameRule.includes('overflow: hidden'));
  assert.ok(nameRule.includes('white-space: normal'));
  assert.ok(codeRule.includes('white-space: normal'));
  assert.ok(codeRule.includes('overflow-wrap: anywhere'));
  assert.ok(!codeRule.includes('text-overflow: ellipsis'));
  assert.ok(imageRule.includes('width: 72px'));
  assert.ok(imageRule.includes('height: 72px'));
  assert.ok(sidepanel.includes("placeholder.className = 'ranking-image-placeholder'"));
  assert.ok(sidepanel.includes('placeholder.hidden = false'));
});

test('Top 10% 支持供应商自动与手动切换、均价和每页 50 条分页', () => {
  const html = read('src/sidepanel/sidepanel.html');
  const sidepanel = read('src/sidepanel/sidepanel.js');

  for (const id of [
    'rankingSupplierSelect',
    'rankingPrevBtn',
    'rankingPageInfo',
    'rankingNextBtn',
  ]) {
    assert.ok(html.includes(`id="${id}"`), `排名区缺少 ${id}`);
  }

  assert.ok(sidepanel.includes('chrome.tabs.onActivated.addListener'));
  assert.ok(sidepanel.includes('chrome.tabs.onUpdated.addListener'));
  assert.ok(sidepanel.includes("el('rankingSupplierSelect').addEventListener('change'"));
  assert.ok(sidepanel.includes('formatAverageSellingPrice(item.averageSellingPrice'));
  assert.ok(sidepanel.includes('paginateRanking(items, rankingPage)'));
  assert.ok(sidepanel.includes('const activeSupplierRequestGeneration = createGenerationGuard(0)'));
  assert.ok(sidepanel.includes('activeSupplierRequestGeneration.isCurrent(requestGeneration)'));
  assert.ok(sidepanel.includes("placeholder.value = ''"));
});

test('没有当前商品时仍可切换到商品记录空状态', () => {
  const sidepanel = read('src/sidepanel/sidepanel.js');
  const tabRenderStart = sidepanel.indexOf('function renderDataTabs()');
  const tabRenderEnd = sidepanel.indexOf('async function copyText', tabRenderStart);
  const tabClickStart = sidepanel.indexOf("el('historyTab').addEventListener");
  const tabClickEnd = sidepanel.indexOf("el('rankingTab').addEventListener", tabClickStart);
  const tabRender = sidepanel.slice(tabRenderStart, tabRenderEnd);
  const tabClick = sidepanel.slice(tabClickStart, tabClickEnd);

  assert.ok(tabRenderStart >= 0 && tabRenderEnd > tabRenderStart);
  assert.ok(tabClickStart >= 0 && tabClickEnd > tabClickStart);
  assert.ok(!tabRender.includes("el('historyTab').disabled = !hasHistory"));
  assert.ok(!tabClick.includes('if (!currentItem) return'));
  assert.ok(sidepanel.includes("t(locale, 'historyNoItem')"));
});
