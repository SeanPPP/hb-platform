// 侧栏：网站会话连接、门店选择、供应商授权、采购周期混排/筛选/分页、zh/en 切换
import { resolveInitialLocale, t } from '../lib/i18n.js';
import { normalizeApiOrigin, toApiHostPattern } from '../lib/api-origin.js';
import { createGenerationGuard } from '../lib/dats-state.js';
import { selectVisibleSupplierEntries } from '../lib/supplier-list.js';
import { getPendingLocateChange } from '../lib/storage-compat.js';
import { createSingleFlight } from '../lib/session-handoff.js';
import {
  buildProductImageCandidates,
  formatAverageSellingPrice,
  normalizeRankingDays,
  normalizeSupplierOptions,
  normalizeStoreOptions,
  paginateRanking,
  shouldPreserveManualSupplier,
} from '../lib/ranking.js';
import {
  normalizeCycles,
  buildTimeline,
  filterTimeline,
  paginate,
} from '../lib/pagination.js';

const PAGE_SIZE = 20;

function getPreferredLanguages() {
  const languages = globalThis.navigator?.languages;
  if (languages?.length) {
    return Array.from(languages);
  }

  const language = globalThis.navigator?.language;
  return typeof language === 'string' ? [language] : [];
}

let locale = resolveInitialLocale(null, getPreferredLanguages());
let user = null;
let authState = 'checking';
let authReason = null;
let profiles = [];
let storeOptions = [];
let selectedStoreCode = null;
let timeline = [];
let filter = 'all';
let page = 1;
let currentItem = null;
let currentSupplier = null;
let activeView = 'ranking';
let rankingDays = 60;
let rankingPage = 1;
let rankingData = null;
let rankingApiOrigin = null;
let rankingLoading = false;
let apiOrigin = null;
let defaultApiOrigin = null;
let localApiOrigin = null;
let supplierListExpanded = false;
let manuallySelectedSupplierCode = null;
let lastDetectedSupplierCode = null;
let activeSupplierRefreshTimer = null;
const itemRequestGeneration = createGenerationGuard(0);
const rankingRequestGeneration = createGenerationGuard(0);
const activeSupplierRequestGeneration = createGenerationGuard(0);

const el = (id) => document.getElementById(id);
const send = (msg) => chrome.runtime.sendMessage(msg);

function setStatus(text) {
  el('status').textContent = text || '';
}

function formatMessage(key, values = {}) {
  return Object.entries(values).reduce(
    (message, [name, value]) => message.replaceAll(`{${name}}`, String(value)),
    t(locale, key),
  );
}

function applyI18n() {
  document.documentElement.lang = locale === 'zh' ? 'zh-CN' : 'en';
  document.title = t(locale, 'title');
  el('pageTitle').textContent = t(locale, 'title');
  el('openShopBtn').textContent = t(locale, 'openShop');
  el('recheckBtn').textContent = t(locale, 'recheckSession');
  el('connectedTitle').textContent = t(locale, 'sessionConnectedTitle');
  el('disconnectBtn').textContent = t(locale, 'disconnectExtension');
  el('storeSaveBtn').textContent = t(locale, 'save');
  el('apiTitle').textContent = t(locale, 'apiTitle');
  el('apiRemoteBtn').textContent = t(locale, 'apiRemote');
  el('apiLocalBtn').textContent = t(locale, 'apiLocal');
  el('apiSaveBtn').textContent = t(locale, 'apiApply');
  el('apiOriginInput').placeholder = t(locale, 'apiPlaceholder');
  el('apiOriginInput').setAttribute('aria-label', t(locale, 'apiTitle'));
  el('apiOriginHint').textContent = t(locale, 'apiHint');
  el('storeLabel').textContent = t(locale, 'store');
  el('storeEmpty').textContent = t(locale, 'noPosStore');
  el('supplierTitle').textContent = t(locale, 'supplier');
  el('historyTab').textContent = t(locale, 'historyTab');
  el('rankingTab').textContent = t(locale, 'rankingTab');
  el('rankingTitle').textContent = t(locale, 'rankingTitle');
  el('rankingSupplierLabel').textContent = t(locale, 'rankingSupplier');
  el('rankingPeriodLabel').textContent = t(locale, 'rankingPeriod');
  document.querySelectorAll('[data-ranking-days]').forEach((button) => {
    button.textContent = `${button.dataset.rankingDays} ${t(locale, 'days')}`;
  });
  el('typeHeading').textContent = t(locale, 'type');
  el('dateHeading').textContent = t(locale, 'date');
  el('orderNoHeading').textContent = t(locale, 'orderNo');
  el('quantityHeading').textContent = t(locale, 'quantity');
  el('priceHeading').textContent = t(locale, 'price');
  el('prevBtn').textContent = t(locale, 'prev');
  el('nextBtn').textContent = t(locale, 'next');
  el('rankingPrevBtn').textContent = t(locale, 'prev');
  el('rankingNextBtn').textContent = t(locale, 'next');
  document.querySelector('.filters [data-filter="all"]').textContent = t(locale, 'all');
  document.querySelector('.filters [data-filter="order"]').textContent = t(locale, 'order');
  document.querySelector('.filters [data-filter="sales"]').textContent = t(locale, 'sales');
  el('localeBtn').textContent = locale === 'zh' ? 'EN' : '中文';
}

function renderApiSettings() {
  el('apiOriginInput').value = apiOrigin || defaultApiOrigin || '';
  el('apiRemoteBtn').classList.toggle('active', !!apiOrigin && apiOrigin === defaultApiOrigin);
  el('apiLocalBtn').classList.toggle('active', !!apiOrigin && apiOrigin === localApiOrigin);
}

function renderAuth() {
  const connected = authState === 'connected' && !!user;
  const authSection = el('authSection');
  authSection.hidden = connected;
  authSection.classList.toggle('checking', authState === 'checking');
  authSection.classList.toggle('needs-website', authState === 'needsWebsite');
  el('userSection').hidden = !connected;
  el('storeSection').hidden = !connected;
  el('supplierSection').hidden = !connected;
  el('openShopBtn').disabled = authState === 'checking';
  el('recheckBtn').disabled = authState === 'checking';

  if (authState === 'checking') {
    el('authStatusTitle').textContent = t(locale, 'sessionCheckingTitle');
    el('authStatusDescription').textContent = t(locale, 'sessionCheckingDescription');
  } else {
    el('authStatusTitle').textContent = t(locale, 'sessionNeedsWebsiteTitle');
    el('authStatusDescription').textContent = authReason === 'API_ORIGIN_MISMATCH'
      ? t(locale, 'apiOriginMismatch')
      : t(locale, 'sessionNeedsWebsiteDescription');
  }

  if (connected) {
    el('userInfo').textContent = (
      user.fullName
      || user.username
      || user.name
      || user.displayName
    ) || t(locale, 'title');
  }
}

function renderStore() {
  const hasStores = storeOptions.length > 0;
  el('storeSelect').hidden = !hasStores;
  el('storeEmpty').hidden = hasStores;
  el('storeSaveBtn').disabled = !hasStores;
  if (hasStores) {
    el('storeSelect').replaceChildren();
    for (const s of storeOptions) {
      const opt = document.createElement('option');
      opt.value = s.code;
      opt.textContent = s.name ? `${s.name} (${s.code})` : s.code;
      el('storeSelect').appendChild(opt);
    }
    if (!storeOptions.some((store) => store.code === selectedStoreCode)) {
      selectedStoreCode = storeOptions[0].code;
    }
    el('storeSelect').value = selectedStoreCode;
  } else {
    selectedStoreCode = null;
  }
}

async function renderSuppliers() {
  const list = el('supplierList');
  list.replaceChildren();
  const entries = [];
  for (const p of profiles) {
    for (const pattern of p.origins || []) {
      let granted = false;
      try {
        granted = await chrome.permissions.contains({ origins: [pattern] });
      } catch {
        granted = false;
      }
      entries.push({ profile: p, pattern, granted });
    }
  }

  const {
    visibleEntries,
    grantedCount,
    hiddenGrantedCount,
  } = selectVisibleSupplierEntries(entries, supplierListExpanded);
  const toggle = el('supplierToggleBtn');
  const hint = el('supplierCollapseHint');
  toggle.hidden = grantedCount === 0;
  toggle.setAttribute('aria-expanded', String(supplierListExpanded));
  toggle.setAttribute('aria-controls', 'supplierList');
  toggle.textContent = supplierListExpanded
    ? t(locale, 'supplierCollapse')
    : formatMessage('supplierExpand', { count: grantedCount });
  hint.hidden = hiddenGrantedCount === 0;
  hint.textContent = formatMessage('supplierCollapsedHint', { count: hiddenGrantedCount });

  for (const { profile: p, pattern, granted } of visibleEntries) {
    const li = document.createElement('li');
    const meta = document.createElement('div');
    meta.className = 'supplier-meta';
    const name = document.createElement('span');
    name.textContent = `${p.displayName} (${p.supplierCode})`;
    const origin = document.createElement('small');
    origin.textContent = pattern;
    meta.append(name, origin);
    const btn = document.createElement('button');
    btn.type = 'button';
    btn.textContent = granted ? t(locale, 'granted') : t(locale, 'grant');
    btn.disabled = granted;
    if (!granted) {
      btn.addEventListener('click', () => grantOrigin(pattern));
    }
    li.append(meta, btn);
    list.appendChild(li);
  }
}

async function grantOrigin(pattern) {
  if (!pattern) return;
  try {
    const granted = await chrome.permissions.request({ origins: [pattern] });
    if (granted) {
      const res = await send({ type: 'REGISTER_ORIGIN', originPattern: pattern });
      setStatus(res && res.ok ? `${t(locale, 'grantSuccess')}: ${pattern}` : (res && res.error) || t(locale, 'grantFailed'));
    } else {
      setStatus(t(locale, 'grantDenied'));
    }
    void renderSuppliers();
  } catch (e) {
    setStatus(`${t(locale, 'grantFailed')}: ${String((e && e.message) || e)}`);
  }
}

function renderDataTabs() {
  el('dataTabs').hidden = !user;
  el('historyTab').disabled = false;
  el('rankingTab').disabled = false;
  el('historyTab').setAttribute('aria-selected', String(activeView === 'history'));
  el('rankingTab').setAttribute('aria-selected', String(activeView === 'ranking'));
}

async function copyText(value, button) {
  const text = String(value || '').trim();
  if (!text) return;
  try {
    await navigator.clipboard.writeText(text);
  } catch {
    const input = document.createElement('textarea');
    input.value = text;
    input.style.position = 'fixed';
    input.style.opacity = '0';
    document.body.appendChild(input);
    input.select();
    document.execCommand('copy');
    input.remove();
  }
  const previous = button.textContent;
  button.textContent = t(locale, 'copied');
  setTimeout(() => {
    button.textContent = previous;
  }, 1200);
}

function attachImageCandidates(image, placeholder, item) {
  const candidates = buildProductImageCandidates(item, rankingApiOrigin || apiOrigin);
  if (candidates.length === 0) {
    image.hidden = true;
    placeholder.hidden = false;
    return;
  }
  let index = 0;
  placeholder.hidden = true;
  image.src = candidates[index];
  image.addEventListener('error', () => {
    index += 1;
    if (index < candidates.length) image.src = candidates[index];
    else {
      image.hidden = true;
      placeholder.hidden = false;
    }
  });
}

function renderRanking() {
  const visible = !!user && activeView === 'ranking';
  el('rankingSection').hidden = !visible;
  if (!visible) return;

  el('rankingSupplier').textContent = currentSupplier
    ? `${currentSupplier.displayName || currentSupplier.supplierCode} (${currentSupplier.supplierCode})`
    : t(locale, 'rankingNoSupplier');
  const supplierSelect = el('rankingSupplierSelect');
  const supplierOptions = normalizeSupplierOptions(profiles);
  supplierSelect.replaceChildren();
  if (!currentSupplier) {
    const placeholder = document.createElement('option');
    placeholder.value = '';
    placeholder.textContent = t(locale, 'rankingChooseSupplier');
    placeholder.disabled = true;
    placeholder.selected = true;
    supplierSelect.appendChild(placeholder);
  }
  for (const supplier of supplierOptions) {
    const option = document.createElement('option');
    option.value = supplier.code;
    option.textContent = `${supplier.name} (${supplier.code})`;
    supplierSelect.appendChild(option);
  }
  supplierSelect.disabled = supplierOptions.length === 0;
  if (currentSupplier) supplierSelect.value = currentSupplier.supplierCode;
  document.querySelectorAll('[data-ranking-days]').forEach((button) => {
    button.classList.toggle('active', Number(button.dataset.rankingDays) === rankingDays);
  });

  const list = el('rankingList');
  list.replaceChildren();
  const items = rankingData && Array.isArray(rankingData.items) ? rankingData.items : [];
  const pagedItems = paginateRanking(items, rankingPage);
  rankingPage = pagedItems.page;
  const hasSupplier = !!currentSupplier;
  const empty = !rankingLoading && items.length === 0;
  el('rankingEmpty').hidden = !empty;
  el('rankingEmpty').textContent = hasSupplier
    ? t(locale, 'rankingNoData')
    : t(locale, 'rankingNoSupplier');

  if (rankingData) {
    const scope = formatMessage('rankingScope', {
      stores: rankingData.enabledStoreCount ?? 0,
      products: rankingData.totalProductCount ?? 0,
    });
    const dates = rankingData.startDate && rankingData.endDate
      ? ` · ${rankingData.startDate}–${rankingData.endDate}`
      : '';
    el('rankingScope').textContent = `${scope}${dates}`;
  } else {
    el('rankingScope').textContent = '';
  }

  for (const item of pagedItems.items) {
    const li = document.createElement('li');
    li.className = 'ranking-item';

    const rank = document.createElement('span');
    rank.className = 'ranking-rank';
    rank.textContent = `#${item.rank}`;

    const image = document.createElement('img');
    image.className = 'ranking-image';
    image.alt = item.productName || item.itemNumber || '';
    image.loading = 'lazy';
    const imageFrame = document.createElement('div');
    imageFrame.className = 'ranking-image-frame';
    const placeholder = document.createElement('span');
    placeholder.className = 'ranking-image-placeholder';
    placeholder.textContent = '—';
    placeholder.setAttribute('aria-hidden', 'true');
    imageFrame.append(image, placeholder);
    attachImageCandidates(image, placeholder, item);

    const product = document.createElement('div');
    product.className = 'ranking-product';
    const name = document.createElement('strong');
    name.textContent = item.productName || item.itemNumber || item.productCode || '—';
    const codeRow = document.createElement('div');
    codeRow.className = 'ranking-code-row';
    const code = document.createElement('span');
    code.className = 'ranking-code';
    code.title = item.itemNumber || item.productCode || '';
    code.textContent = item.itemNumber || item.productCode || '—';
    const copy = document.createElement('button');
    copy.type = 'button';
    copy.className = 'copy-button';
    copy.textContent = t(locale, 'copy');
    copy.setAttribute('aria-label', `${t(locale, 'copy')} ${code.textContent}`);
    copy.addEventListener('click', () => copyText(code.textContent, copy));
    codeRow.append(code, copy);
    product.append(name, codeRow);

    const metrics = document.createElement('div');
    metrics.className = 'ranking-metrics';
    const sales = document.createElement('div');
    sales.className = 'ranking-sales';
    const salesLabel = document.createElement('span');
    salesLabel.textContent = t(locale, 'sales');
    const salesValue = document.createElement('strong');
    const numericQuantity = Number(item.salesQuantity ?? 0);
    salesValue.textContent = Number.isFinite(numericQuantity)
      ? numericQuantity.toLocaleString(locale === 'zh' ? 'zh-CN' : 'en-AU')
      : String(item.salesQuantity ?? 0);
    sales.append(salesLabel, salesValue);

    const averagePrice = document.createElement('div');
    averagePrice.className = 'ranking-average-price';
    const averagePriceLabel = document.createElement('span');
    averagePriceLabel.textContent = t(locale, 'averageSellingPrice');
    const averagePriceValue = document.createElement('strong');
    averagePriceValue.textContent = formatAverageSellingPrice(item.averageSellingPrice, locale);
    averagePrice.append(averagePriceLabel, averagePriceValue);
    metrics.append(sales, averagePrice);

    li.append(rank, imageFrame, product, metrics);
    list.appendChild(li);
  }

  const showPager = !rankingLoading && items.length > 0;
  el('rankingPager').hidden = !showPager;
  el('rankingPageInfo').textContent = showPager
    ? `${t(locale, 'page')} ${pagedItems.page} / ${pagedItems.totalPages} · ${pagedItems.pageSize}`
    : '';
  el('rankingPrevBtn').disabled = pagedItems.page <= 1;
  el('rankingNextBtn').disabled = pagedItems.page >= pagedItems.totalPages;
}

function renderItem() {
  document.querySelectorAll('.filters button').forEach((button) => {
    button.classList.toggle('active', button.dataset.filter === filter);
    button.disabled = !currentItem;
  });
  el('itemSection').hidden = !user || activeView !== 'history';
  if (!user || activeView !== 'history') return;

  const tbody = el('itemBody');
  tbody.replaceChildren();
  if (!currentItem) {
    el('itemTitle').textContent = t(locale, 'historyTab');
    const tr = document.createElement('tr');
    const td = document.createElement('td');
    td.colSpan = 5;
    td.className = 'muted';
    td.textContent = t(locale, 'historyNoItem');
    tr.appendChild(td);
    tbody.appendChild(tr);
    el('pageInfo').textContent = '';
    el('prevBtn').disabled = true;
    el('nextBtn').disabled = true;
    return;
  }
  el('itemTitle').textContent = `${currentItem.supplierCode} / ${currentItem.itemNumber}`;

  const filtered = filterTimeline(timeline, filter);
  const p = paginate(filtered, page, PAGE_SIZE);

  for (const c of p.items) {
    const tr = document.createElement('tr');
    const tdType = document.createElement('td');
    const typeTag = document.createElement('span');
    typeTag.className = `type-tag ${c.type === 'order' ? 'type-order' : 'type-sales'}`;
    typeTag.textContent = t(locale, c.type === 'order' ? 'order' : 'sales');
    tdType.appendChild(typeTag);
    const tdDate = document.createElement('td');
    tdDate.textContent = c.dateRange || c.date || '—';
    const tdNo = document.createElement('td');
    tdNo.textContent = c.orderNo || '—';
    const tdQty = document.createElement('td');
    tdQty.textContent = c.quantity != null ? c.quantity : 0;
    tdQty.className = c.type === 'order' ? 'qty-order' : 'qty-sales';
    const tdPrice = document.createElement('td');
    tdPrice.textContent = c.price != null ? c.price : '—';
    tr.append(tdType, tdDate, tdNo, tdQty, tdPrice);
    tbody.appendChild(tr);
  }

  if (p.items.length === 0) {
    const tr = document.createElement('tr');
    const td = document.createElement('td');
    td.colSpan = 5;
    td.className = 'muted';
    td.textContent = t(locale, 'noData');
    tr.appendChild(td);
    tbody.appendChild(tr);
  }

  el('pageInfo').textContent = `${t(locale, 'page')} ${p.page} / ${p.totalPages}`;
  el('prevBtn').disabled = p.page <= 1;
  el('nextBtn').disabled = p.page >= p.totalPages;
}

function render() {
  applyI18n();
  renderApiSettings();
  renderAuth();
  renderStore();
  void renderSuppliers();
  renderDataTabs();
  renderItem();
  renderRanking();
}

async function loadProfiles() {
  const response = await send({ type: 'GET_PROFILES' });
  profiles = response && response.ok ? response.profiles || [] : [];
}

async function loadStores() {
  const response = await send({ type: 'GET_STORES' });
  storeOptions = response && response.ok ? normalizeStoreOptions(response.data) : [];
  const previousStoreCode = selectedStoreCode;
  if (!storeOptions.some((store) => store.code === selectedStoreCode)) {
    selectedStoreCode = storeOptions[0]?.code || null;
  }
  if (selectedStoreCode !== previousStoreCode) {
    if (selectedStoreCode) await chrome.storage.local.set({ selectedStoreCode });
    else await chrome.storage.local.remove('selectedStoreCode');
  }
}

async function loadActiveSupplier() {
  const requestGeneration = activeSupplierRequestGeneration.advance();
  const response = await send({ type: 'ACTIVE_SUPPLIER' });
  if (!activeSupplierRequestGeneration.isCurrent(requestGeneration)) return false;
  const detectedSupplier = response && response.ok ? response.supplier || null : null;
  const previousDetectedSupplierCode = lastDetectedSupplierCode;
  const detectedSupplierCode = detectedSupplier?.supplierCode || null;
  // 离开供应商网站时保留最近一次自动识别结果，返回同一网站后仍尊重手动选择。
  if (detectedSupplierCode) lastDetectedSupplierCode = detectedSupplierCode;
  if (
    shouldPreserveManualSupplier({
      manualSupplierCode: manuallySelectedSupplierCode,
      detectedSupplierCode,
      previousDetectedSupplierCode,
    })
  ) {
    return false;
  }

  if (detectedSupplier) manuallySelectedSupplierCode = null;
  const previousCode = currentSupplier?.supplierCode || null;
  const nextCode = detectedSupplier?.supplierCode || null;
  if (previousCode === nextCode) return false;

  rankingRequestGeneration.advance();
  currentSupplier = detectedSupplier;
  rankingData = null;
  rankingLoading = false;
  rankingPage = 1;
  return true;
}

function selectSupplier(supplierCode, { manual = false } = {}) {
  activeSupplierRequestGeneration.advance();
  const profile = profiles.find((item) => item.supplierCode === supplierCode);
  manuallySelectedSupplierCode = manual ? supplierCode : null;
  const changed = currentSupplier?.supplierCode !== supplierCode;
  currentSupplier = {
    supplierCode,
    displayName: profile?.displayName || supplierCode,
  };
  if (changed) {
    rankingRequestGeneration.advance();
    rankingData = null;
    rankingLoading = false;
    rankingPage = 1;
  }
  return changed;
}

async function refreshActiveSupplierAndRanking() {
  if (!user) return;
  const changed = await loadActiveSupplier();
  if (!changed) return;
  render();
  if (activeView === 'ranking' && currentSupplier) await loadRanking();
}

function scheduleActiveSupplierRefresh() {
  if (activeSupplierRefreshTimer != null) clearTimeout(activeSupplierRefreshTimer);
  activeSupplierRefreshTimer = setTimeout(() => {
    activeSupplierRefreshTimer = null;
    void refreshActiveSupplierAndRanking().catch((error) => {
      setStatus(String((error && error.message) || error));
    });
  }, 120);
}

async function loadRanking() {
  if (!currentSupplier) await loadActiveSupplier();
  if (!currentSupplier) {
    rankingData = null;
    rankingLoading = false;
    setStatus('');
    render();
    return;
  }

  const requestGeneration = rankingRequestGeneration.advance();
  const requestedSupplierCode = currentSupplier.supplierCode;
  rankingLoading = true;
  rankingData = null;
  rankingPage = 1;
  setStatus(t(locale, 'loading'));
  renderRanking();
  const response = await send({
    type: 'SUPPLIER_TOP_SALES',
    supplierCode: requestedSupplierCode,
    days: rankingDays,
  });
  if (!rankingRequestGeneration.isCurrent(requestGeneration)) return;
  rankingLoading = false;
  if (!response || !response.ok) {
    setStatus((response && response.error) || t(locale, 'error'));
    renderRanking();
    return;
  }
  rankingData = response.data || null;
  rankingApiOrigin = response.apiOrigin || apiOrigin;
  setStatus('');
  renderRanking();
}

async function loadItem(item) {
  const requestGeneration = itemRequestGeneration.advance();
  currentItem = item;
  selectSupplier(item.supplierCode);
  activeView = 'history';
  timeline = [];
  filter = 'all';
  page = 1;
  // 只使用服务端筛选后的当前门店，避免沿用内容脚本或旧缓存中的停用 POS 门店。
  const storeCode = selectedStoreCode;
  if (!storeCode) {
    setStatus(t(locale, 'noStore'));
    renderItem();
    return;
  }
  setStatus(t(locale, 'loading'));
  const resp = await send({
    type: 'PURCHASE_CYCLES',
    storeCode,
    supplierCode: item.supplierCode,
    itemNumber: item.itemNumber,
  });
  if (!itemRequestGeneration.isCurrent(requestGeneration)) return;
  if (!resp || !resp.ok) {
    setStatus((resp && resp.error) || t(locale, 'error'));
    renderItem();
    return;
  }
  const { orders, sales } = normalizeCycles(resp.data);
  timeline = buildTimeline({ orders, sales });
  setStatus('');
  renderItem();
}

function resetAuthenticatedData() {
  itemRequestGeneration.advance();
  rankingRequestGeneration.advance();
  activeSupplierRequestGeneration.advance();
  user = null;
  profiles = [];
  storeOptions = [];
  timeline = [];
  rankingData = null;
  currentSupplier = null;
  manuallySelectedSupplierCode = null;
  lastDetectedSupplierCode = null;
  rankingPage = 1;
  if (activeSupplierRefreshTimer != null) {
    clearTimeout(activeSupplierRefreshTimer);
    activeSupplierRefreshTimer = null;
  }
}

const connectFromWebsiteSession = createSingleFlight(async ({ loadView = false } = {}) => {
  authState = 'checking';
  authReason = null;
  setStatus('');
  render();

  const response = await send({ type: 'CURRENT' });
  if (!response?.ok || !response.user) {
    resetAuthenticatedData();
    authState = 'needsWebsite';
    authReason = response?.reason || 'WEBSITE_SESSION_REQUIRED';
    setStatus(authReason === 'API_ORIGIN_MISMATCH'
      ? t(locale, 'apiOriginMismatch')
      : response?.error || '');
    render();
    return false;
  }

  user = response.user;
  authState = 'connected';
  await loadProfiles();
  await loadStores();
  await loadActiveSupplier();
  setStatus('');
  render();

  if (loadView) {
    if (currentItem) await loadItem(currentItem);
    else if (activeView === 'ranking') await loadRanking();
  }
  return true;
});

async function init() {
  const stored = await chrome.storage.local.get(['locale', 'selectedStoreCode']);
  locale = resolveInitialLocale(stored.locale, getPreferredLanguages());
  if (stored.locale !== locale) {
    await chrome.storage.local.set({ locale });
  }
  applyI18n();
  selectedStoreCode = stored.selectedStoreCode || null;

  const apiConfig = await send({ type: 'GET_API_ORIGIN' });
  if (apiConfig && apiConfig.ok) {
    apiOrigin = apiConfig.apiOrigin;
    defaultApiOrigin = apiConfig.defaultApiOrigin;
    localApiOrigin = apiConfig.localApiOrigin;
  }

  const connected = await connectFromWebsiteSession();

  const { pendingLocate } = await chrome.storage.session.get('pendingLocate');
  if (connected && pendingLocate) {
    await chrome.storage.session.remove('pendingLocate');
    await loadItem(pendingLocate);
  } else if (connected && activeView === 'ranking') {
    await loadRanking();
  }

  render();
}

async function applyApiOrigin(value) {
  const normalized = normalizeApiOrigin(value, defaultApiOrigin);
  if (!normalized) {
    setStatus(t(locale, 'apiInvalid'));
    return;
  }

  const pattern = toApiHostPattern(normalized);
  let granted = false;
  try {
    granted = pattern ? await chrome.permissions.contains({ origins: [pattern] }) : false;
    if (!granted && pattern) {
      granted = await chrome.permissions.request({ origins: [pattern] });
    }
  } catch {
    setStatus(t(locale, 'apiPermissionDenied'));
    return;
  }
  if (!granted) {
    setStatus(t(locale, 'apiPermissionDenied'));
    return;
  }

  const response = await send({ type: 'SET_API_ORIGIN', apiOrigin: normalized });
  if (!response || !response.ok) {
    setStatus((response && response.error) || t(locale, 'error'));
    return;
  }

  apiOrigin = response.apiOrigin;
  if (response.changed) {
    resetAuthenticatedData();
    authState = 'checking';
    authReason = null;
    setStatus(t(locale, 'apiSwitched'));
    render();
    await connectFromWebsiteSession({ loadView: true });
  } else {
    setStatus(t(locale, 'apiSaved'));
    render();
  }
}

el('localeBtn').addEventListener('click', async () => {
  locale = locale === 'zh' ? 'en' : 'zh';
  await chrome.storage.local.set({ locale });
  render();
});

el('apiRemoteBtn').addEventListener('click', () => {
  void applyApiOrigin('/');
});

el('apiLocalBtn').addEventListener('click', () => {
  void applyApiOrigin(localApiOrigin);
});

el('apiSaveBtn').addEventListener('click', () => {
  void applyApiOrigin(el('apiOriginInput').value);
});

el('apiOriginInput').addEventListener('keydown', (event) => {
  if (event.key !== 'Enter') return;
  event.preventDefault();
  void applyApiOrigin(el('apiOriginInput').value);
});

el('openShopBtn').addEventListener('click', async () => {
  authState = 'checking';
  authReason = null;
  setStatus('');
  render();
  const response = await send({ type: 'OPEN_HB_SHOP' });
  if (response?.connected) {
    await connectFromWebsiteSession({ loadView: true });
    return;
  }
  authState = 'needsWebsite';
  authReason = response?.reason || 'WEBSITE_TAB_REQUIRED';
  setStatus(response?.ok ? '' : response?.error || t(locale, 'error'));
  render();
});

el('recheckBtn').addEventListener('click', () => {
  void connectFromWebsiteSession({ loadView: true });
});

el('disconnectBtn').addEventListener('click', async () => {
  await send({ type: 'DISCONNECT' });
  resetAuthenticatedData();
  authState = 'needsWebsite';
  authReason = 'WEBSITE_SESSION_REQUIRED';
  setStatus('');
  render();
});

el('storeSaveBtn').addEventListener('click', async () => {
  if (storeOptions.length === 0) {
    setStatus(t(locale, 'noPosStore'));
    return;
  }
  selectedStoreCode = el('storeSelect').value;
  await chrome.storage.local.set({ selectedStoreCode });
  setStatus(selectedStoreCode ? `${t(locale, 'storeSaved')}: ${selectedStoreCode}` : '');
  if (currentItem && selectedStoreCode) {
    currentItem = { ...currentItem, storeCode: selectedStoreCode };
    await loadItem(currentItem);
  }
});

el('supplierToggleBtn').addEventListener('click', () => {
  supplierListExpanded = !supplierListExpanded;
  void renderSuppliers();
});

el('historyTab').addEventListener('click', () => {
  activeView = 'history';
  setStatus('');
  render();
});

el('rankingTab').addEventListener('click', async () => {
  activeView = 'ranking';
  await loadActiveSupplier();
  render();
  await loadRanking();
});

el('rankingSupplierSelect').addEventListener('change', async () => {
  const supplierCode = el('rankingSupplierSelect').value;
  if (!supplierCode) return;
  selectSupplier(supplierCode, { manual: true });
  activeView = 'ranking';
  render();
  await loadRanking();
});

document.querySelectorAll('[data-ranking-days]').forEach((button) => {
  button.addEventListener('click', async () => {
    const nextDays = normalizeRankingDays(button.dataset.rankingDays);
    if (nextDays === rankingDays && rankingData) return;
    rankingDays = nextDays;
    rankingPage = 1;
    await loadRanking();
  });
});

el('rankingPrevBtn').addEventListener('click', () => {
  if (rankingPage > 1) {
    rankingPage--;
    renderRanking();
  }
});

el('rankingNextBtn').addEventListener('click', () => {
  const items = rankingData && Array.isArray(rankingData.items) ? rankingData.items : [];
  const current = paginateRanking(items, rankingPage);
  if (rankingPage < current.totalPages) {
    rankingPage++;
    renderRanking();
  }
});

document.querySelectorAll('.filters button').forEach((btn) => {
  btn.addEventListener('click', () => {
    filter = btn.dataset.filter;
    page = 1;
    document.querySelectorAll('.filters button').forEach((b) => b.classList.toggle('active', b === btn));
    renderItem();
  });
});

el('prevBtn').addEventListener('click', () => {
  if (page > 1) {
    page--;
    renderItem();
  }
});

el('nextBtn').addEventListener('click', () => {
  page++;
  renderItem();
});

chrome.storage.onChanged.addListener((changes, areaName) => {
  const tokenChange = areaName === 'session' ? changes.websiteAccessToken : null;
  if (tokenChange?.newValue && authState !== 'connected') {
    void connectFromWebsiteSession({ loadView: true });
  } else if (tokenChange?.oldValue && !tokenChange.newValue) {
    resetAuthenticatedData();
    authState = 'needsWebsite';
    authReason = 'WEBSITE_SESSION_REQUIRED';
    setStatus('');
    render();
  }

  const item = getPendingLocateChange(changes, areaName);
  if (!item) return;
  void chrome.storage.session
    .remove('pendingLocate')
    .then(() => loadItem(item))
    .catch((error) => setStatus(String((error && error.message) || error)));
});

chrome.tabs.onActivated.addListener(() => {
  scheduleActiveSupplierRefresh();
});

chrome.tabs.onUpdated.addListener((_tabId, changeInfo, tab) => {
  if (!tab.active || (!changeInfo.url && changeInfo.status !== 'complete')) return;
  scheduleActiveSupplierRefresh();
});

void init().catch((error) => {
  setStatus(String((error && error.message) || error));
  render();
});
