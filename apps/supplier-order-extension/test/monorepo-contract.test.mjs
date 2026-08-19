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

test('供应商页注入按钮保留可见键盘焦点', () => {
  const list = read('src/content/list.js');
  assert.ok(list.includes('.hb-btn:focus-visible'));
  assert.ok(list.includes("attachShadow({ mode: 'closed' })"));
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
