// 侧栏：登录/退出、门店选择、供应商授权、采购周期混排/筛选/分页、zh/en 切换
import { normalizeLocale, t } from '../lib/i18n.js';
import { createGenerationGuard } from '../lib/dats-state.js';
import {
  normalizeCycles,
  buildTimeline,
  filterTimeline,
  paginate,
} from '../lib/pagination.js';

const PAGE_SIZE = 20;

let locale = 'zh';
let user = null;
let profiles = [];
let storeOptions = [];
let selectedStoreCode = null;
let timeline = [];
let filter = 'all';
let page = 1;
let currentItem = null;
const itemRequestGeneration = createGenerationGuard(0);

const el = (id) => document.getElementById(id);
const send = (msg) => chrome.runtime.sendMessage(msg);

function setStatus(text) {
  el('status').textContent = text || '';
}

function normalizeStores(userObj) {
  const stores = userObj && userObj.stores;
  if (!Array.isArray(stores)) return [];
  return stores
    .map((s) => {
      if (typeof s === 'string') return { code: s, name: s };
      if (s && typeof s === 'object') {
        const code = String(s.code ?? s.storeCode ?? s.value ?? '');
        const name = String(s.name ?? s.storeName ?? s.label ?? code);
        return { code, name };
      }
      return null;
    })
    .filter((s) => s && s.code);
}

function applyI18n() {
  document.documentElement.lang = locale === 'zh' ? 'zh-CN' : 'en';
  document.title = t(locale, 'title');
  el('pageTitle').textContent = t(locale, 'title');
  el('loginBtn').textContent = t(locale, 'login');
  el('logoutBtn').textContent = t(locale, 'logout');
  el('storeSaveBtn').textContent = t(locale, 'save');
  el('username').placeholder = t(locale, 'username');
  el('password').placeholder = t(locale, 'password');
  el('storeCodeInput').placeholder = t(locale, 'storeCode');
  el('username').setAttribute('aria-label', t(locale, 'username'));
  el('password').setAttribute('aria-label', t(locale, 'password'));
  el('storeCodeInput').setAttribute('aria-label', t(locale, 'storeCode'));
  el('storeLabel').textContent = t(locale, 'store');
  el('supplierTitle').textContent = t(locale, 'supplier');
  el('typeHeading').textContent = t(locale, 'type');
  el('dateHeading').textContent = t(locale, 'date');
  el('orderNoHeading').textContent = t(locale, 'orderNo');
  el('quantityHeading').textContent = t(locale, 'quantity');
  el('priceHeading').textContent = t(locale, 'price');
  el('prevBtn').textContent = t(locale, 'prev');
  el('nextBtn').textContent = t(locale, 'next');
  document.querySelector('.filters [data-filter="all"]').textContent = t(locale, 'all');
  document.querySelector('.filters [data-filter="order"]').textContent = t(locale, 'order');
  document.querySelector('.filters [data-filter="sales"]').textContent = t(locale, 'sales');
  el('localeBtn').textContent = locale === 'zh' ? 'EN' : '中文';
}

function renderAuth() {
  const loggedIn = !!user;
  el('authSection').hidden = loggedIn;
  el('userSection').hidden = !loggedIn;
  el('storeSection').hidden = !loggedIn;
  el('supplierSection').hidden = !loggedIn;
  if (loggedIn) {
    el('userInfo').textContent = (user.username || user.name || user.displayName) || t(locale, 'title');
  }
}

function renderStore() {
  const hasStores = storeOptions.length > 0;
  el('storeSelect').hidden = !hasStores;
  el('storeCodeInput').hidden = hasStores;
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
    el('storeCodeInput').value = selectedStoreCode || '';
  }
}

async function renderSuppliers() {
  const list = el('supplierList');
  list.replaceChildren();
  for (const p of profiles) {
    for (const pattern of p.origins || []) {
      let granted = false;
      try {
        granted = await chrome.permissions.contains({ origins: [pattern] });
      } catch {
        granted = false;
      }
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

function renderItem() {
  document.querySelectorAll('.filters button').forEach((button) => {
    button.classList.toggle('active', button.dataset.filter === filter);
  });
  el('itemSection').hidden = !currentItem || !user;
  if (!currentItem || !user) return;
  el('itemTitle').textContent = `${currentItem.supplierCode} / ${currentItem.itemNumber}`;

  const filtered = filterTimeline(timeline, filter);
  const p = paginate(filtered, page, PAGE_SIZE);
  const tbody = el('itemBody');
  tbody.replaceChildren();

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
  renderAuth();
  renderStore();
  void renderSuppliers();
  renderItem();
}

async function loadProfiles() {
  const response = await send({ type: 'GET_PROFILES' });
  profiles = response && response.ok ? response.profiles || [] : [];
}

async function loadItem(item) {
  const requestGeneration = itemRequestGeneration.advance();
  currentItem = item;
  timeline = [];
  filter = 'all';
  page = 1;
  const storeCode = item.storeCode || selectedStoreCode;
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

async function init() {
  const stored = await chrome.storage.local.get(['locale', 'selectedStoreCode']);
  locale = normalizeLocale(stored.locale);
  selectedStoreCode = stored.selectedStoreCode || null;

  const cur = await send({ type: 'CURRENT' });
  if (cur && cur.ok && cur.user) {
    user = cur.user;
    storeOptions = normalizeStores(user);
  }

  await loadProfiles();

  const { pendingLocate } = await chrome.storage.session.get('pendingLocate');
  if (pendingLocate) {
    await chrome.storage.session.remove('pendingLocate');
    await loadItem(pendingLocate);
  }

  render();
}

el('localeBtn').addEventListener('click', async () => {
  locale = locale === 'zh' ? 'en' : 'zh';
  await chrome.storage.local.set({ locale });
  render();
});

el('loginBtn').addEventListener('click', async () => {
  const username = el('username').value.trim();
  const password = el('password').value;
  if (!username || !password) {
    setStatus(t(locale, 'emptyCredentials'));
    return;
  }
  setStatus(t(locale, 'loading'));
  const res = await send({ type: 'LOGIN', username, password });
  if (res && res.ok) {
    user = res.user || null;
    storeOptions = normalizeStores(user);
    el('password').value = '';
    const cur = await send({ type: 'CURRENT' });
    if (cur && cur.ok && cur.user) {
      user = cur.user;
      storeOptions = normalizeStores(user);
    }
    await loadProfiles();
    setStatus('');
    if (currentItem) await loadItem(currentItem);
  } else {
    setStatus((res && res.error) || t(locale, 'error'));
  }
  render();
});

el('logoutBtn').addEventListener('click', async () => {
  itemRequestGeneration.advance();
  await send({ type: 'LOGOUT' });
  user = null;
  storeOptions = [];
  timeline = [];
  setStatus('');
  render();
});

el('storeSaveBtn').addEventListener('click', async () => {
  const hasStores = storeOptions.length > 0;
  selectedStoreCode = hasStores ? el('storeSelect').value : el('storeCodeInput').value.trim();
  await chrome.storage.local.set({ selectedStoreCode });
  setStatus(selectedStoreCode ? `${t(locale, 'storeSaved')}: ${selectedStoreCode}` : '');
  if (currentItem && selectedStoreCode) {
    currentItem = { ...currentItem, storeCode: selectedStoreCode };
    await loadItem(currentItem);
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
  if (areaName !== 'session' || !changes.pendingLocate?.newValue) return;
  const item = changes.pendingLocate.newValue;
  void chrome.storage.session
    .remove('pendingLocate')
    .then(() => loadItem(item))
    .catch((error) => setStatus(String((error && error.message) || error)));
});

void init().catch((error) => {
  setStatus(String((error && error.message) || error));
  render();
});
