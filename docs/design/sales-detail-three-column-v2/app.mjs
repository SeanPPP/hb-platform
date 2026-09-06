import { suppliers, branches, products, computeReport } from './model.mjs';

const icons = {
  calendar: '<rect x="3" y="4" width="18" height="17" rx="2"/><path d="M7 2v4m10-4v4M3 10h18"/>',
  refresh: '<path d="M20 7v5h-5M4 17v-5h5"/><path d="M6 7a7 7 0 0 1 12-1l2 6M4 12l2 6a7 7 0 0 0 12-1"/>',
  search: '<circle cx="10" cy="10" r="6.5"/><path d="m15 15 5 5"/>',
  expand: '<path d="M8 3H3v5m13-5h5v5M3 16v5h5m13-5v5h-5M3 3l6 6m12-6-6 6M3 21l6-6m12 6-6-6"/>',
  collapse: '<path d="M9 3v6H3m12-6v6h6M3 15h6v6m12-6h-6v6"/>',
  arrow: '<path d="M4 12h16m-5-5 5 5-5 5"/>',
  chevron: '<path d="m9 5 7 7-7 7"/>',
  left: '<path d="m15 5-7 7 7 7"/>',
  close: '<path d="m6 6 12 12M6 18 18 6"/>',
  sort: '<path d="m8 4-3 3 3 3M5 7h14m-3 7 3 3-3 3M19 17H5"/>',
  box: '<path d="m3 7 9-5 9 5v10l-9 5-9-5zM3 7l9 5 9-5M12 12v10M8 4l9 5v5"/>',
  card: '<rect x="4" y="3" width="16" height="18" rx="1"/><path d="m7 15 4-5 6 7M8 6h3"/>',
  empty: '<path d="m3 9 4-6h10l4 6v11H3zM3 9h5l2 4h4l2-4h5"/>',
  info: '<circle cx="12" cy="12" r="9"/><path d="M12 10v7m0-11v1"/>',
};
const icon = (name) => `<svg viewBox="0 0 24 24" aria-hidden="true">${icons[name] ?? icons.box}</svg>`;
const escape = (value) => String(value ?? '').replace(/[&<>"']/g, (char) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[char]));
const format = (value, digits = 0, prefix = '') => value == null || !Number.isFinite(value) ? '—' : `${prefix}${value.toLocaleString('en-AU', { minimumFractionDigits: digits, maximumFractionDigits: digits })}`;
const money = (value, digits = 0) => format(value, digits, '$');
const percent = (value) => value == null ? '—' : `${format(value, 1)}%`;
const kindLabel = (kind) => kind === 'china' ? 'HB 仓库 · 国内供应商' : '澳洲供应商';
const ranges = { today: ['今天', '2026-09-06', '2026-09-06'], yesterday: ['昨天', '2026-09-05', '2026-09-05'], thisWeek: ['本周', '2026-08-31', '2026-09-06'], lastWeek: ['上周', '2026-08-24', '2026-08-30'], thisMonth: ['本月', '2026-09-01', '2026-09-06'], lastMonth: ['上月', '2026-08-01', '2026-08-31'] };
const defaultState = { kind: new URLSearchParams(location.search).get('kind') === 'china' ? 'china' : 'australia', metricView: 'profit', range: 'thisWeek', compareMode: 'week', autoCompare: true, selectedSupplier: null, selectedBranch: null, selectedProduct: null, search: '', page: 1, pageSize: 10, supplierSearch: '', branchSearch: '', expanded: null, sort: { suppliers: ['revenue', 'desc'], branches: ['revenue', 'desc'], products: ['revenue', 'desc'] } };
let state = structuredClone(defaultState);
let report;
let widths = [28, 27, 45];
let toastTimer;
let composing = false;
const app = document.querySelector('#app');

function trend(current, previous) {
  if (!state.autoCompare || previous == null) return '<span class="growth-cell neutral">—</span>';
  if (previous === 0) return current > 0 ? '<span class="growth-cell positive">新增</span>' : '<span class="growth-cell neutral">—</span>';
  const value = (current - previous) / Math.abs(previous) * 100;
  return `<span class="growth-cell ${value > 0 ? 'positive' : value < 0 ? 'negative' : 'neutral'}">${value > 0 ? '+' : ''}${format(value, 1)}%</span>`;
}

function pair(current, previous, type = 'money', context = {}) {
  const renderValue = (value) => type === 'percent' || type === 'margin' ? percent(value) : type === 'count' ? format(value) : money(value, type === 'price' || type === 'profit' ? 2 : 0);
  const pending = current == null && (type === 'profit' || type === 'margin');
  const comparePending = previous == null && state.autoCompare && (type === 'profit' || type === 'margin');
  // 对齐移动端的空值展示，真实零值正常格式化；无销售不误标为缺成本。
  const currentReason = context.revenue === 0 ? '无销售' : '成本待补全';
  const compareReason = context.compareRevenue === 0 ? '无同期销售' : '成本待补全';
  const renderCurrent = pending ? type === 'margin' && context.revenue !== 0 ? '成本待补全' : '—' : renderValue(current);
  const renderPrevious = !state.autoCompare ? '—' : comparePending ? type === 'margin' && context.compareRevenue !== 0 ? '成本待补全' : '—' : renderValue(previous);
  return `<span class="cell-primary"${pending ? ` title="${currentReason}"` : ''}><span class="sr-only">本期${pending ? currentReason : ''} </span><span class="value${pending && type === 'margin' ? ' pending-value' : ''}${current < 0 && (type === 'profit' || type === 'margin') ? ' negative' : ''}">${renderCurrent}</span></span><span class="cell-secondary"${comparePending ? ` title="${compareReason}"` : ''}><span class="sr-only">同期${!state.autoCompare ? '对比已关闭' : comparePending ? compareReason : ''} </span><span class="value${comparePending && type === 'margin' ? ' pending-value' : ''}">${renderPrevious}</span></span>`;
}

function compareDates() {
  const range = ranges[state.range];
  return range.slice(1).map((date) => {
    const value = new Date(`${date}T00:00:00Z`);
    if (state.compareMode === 'week') value.setUTCDate(value.getUTCDate() - 364);
    else value.setUTCFullYear(value.getUTCFullYear() - 1);
    return value.toISOString().slice(0, 10);
  });
}

function sorted(rows, panel) {
  const [field, direction] = state.sort[panel];
  return [...rows].sort((a, b) => {
    if (a[field] == null) return 1;
    if (b[field] == null) return -1;
    return (a[field] - b[field]) * (direction === 'asc' ? 1 : -1);
  });
}

function header(label, width, panel, field = null, title = '') {
  const active = field && state.sort[panel][0] === field;
  return `<th scope="col" style="width:${width}px"${active ? ` aria-sort="${state.sort[panel][1] === 'asc' ? 'ascending' : 'descending'}"` : ''}${title ? ` title="${escape(title)}"` : ''}>${field ? `<button id="sort-${panel}-${field}" data-action="sort" data-panel="${panel}" data-field="${field}" aria-label="按${label}排序">${label}${icon('sort')}</button>` : label}</th>`;
}

function financeCells(row, { supplier = false } = {}) {
  const sales = `<td>${pair(row.revenue, row.compareRevenue)}</td><td>${trend(row.revenue, row.compareRevenue)}</td>${supplier ? `<td>${pair(row.share, row.compareShare, 'percent')}</td>${state.kind === 'china' ? `<td>${pair(row.chinaShare, row.compareChinaShare, 'percent')}</td>` : ''}` : ''}<td>${pair(row.orders, row.compareOrders, 'count')}</td><td>${pair(row.aov, row.compareAov, 'price')}</td>`;
  const profit = profitCells(row);
  return state.metricView === 'profit' ? profit + sales : sales + profit;
}

function profitCells(row) {
  return `<td data-metric="grossProfit">${pair(row.grossProfit, row.compareGrossProfit, 'profit', row)}</td><td data-metric="margin">${pair(row.margin, row.compareMargin, 'margin', row)}</td>`;
}

function financeHeaders(panel, { supplier = false } = {}) {
  const branch = branches.find((item) => item.id === state.selectedBranch);
  const scope = branch ? `${branch.name} 分店内` : '全部分店内';
  const numerator = state.selectedProduct ? '该供应商的已选商品营业额' : '该供应商营业额';
  const sales = `${header('营业额', 94, panel, 'revenue')}${header('增长率', 70, panel)}${supplier ? `${header('营业额占比 ⓘ', 99, panel, null, `${scope}：${numerator} ÷ ${state.kind === 'china' ? '全部国内供应商' : '全部供应商'}营业额；分母不随商品选择缩小`)}${state.kind === 'china' ? header('中国货占比 ⓘ', 99, panel, null, `${scope}：${numerator} ÷ 全部供应商营业额；分母不随商品选择缩小`) : ''}` : ''}${header('客单数', 78, panel, 'orders')}${header('客单价', 87, panel, 'aov')}`;
  const profit = `${header('毛利额', 92, panel, 'grossProfit', '本期 / 同期毛利额，金额保留两位小数')}${header('毛利率', 78, panel, 'margin', '毛利额 ÷ 营业额；本期 / 同期分别计算，不取商品毛利率的平均值')}`;
  return state.metricView === 'profit' ? profit + sales : sales + profit;
}

function emptyState(message) {
  return `<div class="empty-state">${icon('empty')}<strong>${message}</strong>调整筛选或清除当前选择后重试</div>`;
}

function panelTop(panel, index, title, count, subtitle) {
  return `<header class="panel-top"><div class="panel-title-group"><div class="panel-heading"><span class="step-number">0${index}</span><h2 id="title-${panel}">${title}</h2><span class="panel-count">${count}</span></div><div class="panel-subtitle" title="${escape(subtitle)}">${escape(subtitle)}</div></div><button id="expand-${panel}" class="expand-button" data-action="expand" data-panel="${panel}" aria-expanded="${state.expanded === panel}" aria-label="${state.expanded === panel ? '返回三栏' : `展开${title}`}">${icon(state.expanded === panel ? 'collapse' : 'expand')}</button></header>`;
}

function supplierPanel() {
  const branch = branches.find((item) => item.id === state.selectedBranch);
  const product = products.find((item) => item.id === state.selectedProduct);
  const scope = [branch?.name, product?.name].filter(Boolean).join(' / ');
  const rows = sorted(report.supplierRows, 'suppliers').filter((row) => `${row.name} ${row.code}`.toLowerCase().includes(state.supplierSearch.toLowerCase()));
  return `<section class="report-panel ${state.expanded === 'suppliers' ? 'expanded' : ''}" data-panel="suppliers" aria-labelledby="title-suppliers">
    ${panelTop('suppliers', 1, '供应商', report.supplierRows.length, scope ? `反查范围：${scope}` : '点击供应商，联动分店与商品')}
    <label class="panel-filter">${icon('search')}<input id="supplier-search" type="search" placeholder="搜索供应商名称 / 编码" aria-label="搜索供应商名称或编码" value="${escape(state.supplierSearch)}" data-search="supplierSearch"/></label>
    <div class="panel-scroll" tabindex="0" aria-label="供应商表，可横向滚动">${rows.length ? `<table class="data-table"><thead><tr>${header('供应商 / 编码', state.metricView === 'profit' ? 128 : 147, 'suppliers')}${financeHeaders('suppliers', { supplier: true })}</tr></thead><tbody>${rows.map((row, index) => `<tr class="${state.selectedSupplier === row.id ? 'selected' : ''}" data-id="${escape(row.id)}"><td><button id="supplier-${escape(row.id)}" class="name-button" data-action="supplier" data-id="${escape(row.id)}" aria-pressed="${state.selectedSupplier === row.id}" title="${escape(row.name)} · 筛选分店及商品"><span class="rank">${String(index + 1).padStart(2, '0')}</span><span class="name-body"><span class="name-main">${escape(row.name)}</span><span class="name-code">${escape(row.code)}</span></span></button></td>${financeCells(row, { supplier: true })}</tr>`).join('')}</tbody></table>` : emptyState('没有匹配的供应商')}</div>
    <footer class="panel-bottom"><span>${rows.length} 家供应商</span><span class="scroll-hint">左右滑动查看更多 ${icon('arrow')}</span></footer>
  </section>`;
}

function branchPanel() {
  const supplier = suppliers.find((item) => item.id === state.selectedSupplier);
  const product = products.find((item) => item.id === state.selectedProduct);
  const scope = [supplier?.name, product?.name].filter(Boolean).join(' / ') || '当前标签全部供应商';
  const rows = sorted(report.branchRows, 'branches').filter((row) => row.name.toLowerCase().includes(state.branchSearch.toLowerCase()));
  return `<section class="report-panel ${state.expanded === 'branches' ? 'expanded' : ''}" data-panel="branches" aria-labelledby="title-branches">
    ${panelTop('branches', 2, '分店表现', report.branchRows.length, '点击分店，反查供应商与商品')}
    <div class="source-caption">${icon('arrow')}<span>统计范围</span><strong title="${escape(scope)}">${escape(scope)}</strong></div>
    <div class="panel-scroll" tabindex="0" aria-label="分店表，可横向滚动">${rows.length ? `<table class="data-table"><thead><tr>${header('分店名称', state.metricView === 'profit' ? 128 : 147, 'branches')}${financeHeaders('branches')}</tr></thead><tbody>${rows.map((row, index) => `<tr class="${state.selectedBranch === row.id ? 'selected' : ''}" data-id="${escape(row.id)}"><td><button id="branch-${escape(row.id)}" class="name-button" data-action="branch" data-id="${escape(row.id)}" aria-pressed="${state.selectedBranch === row.id}" title="${escape(row.name)} · 反查供应商及商品"><span class="rank">${String(index + 1).padStart(2, '0')}</span><span class="name-body"><span class="name-main">${escape(row.name)}</span><span class="name-code">${product ? '当前商品' : supplier ? '当前供应商' : '当前标签全部供应商'}</span></span></button></td>${financeCells(row)}</tr>`).join('')}</tbody></table>` : emptyState('暂无分店数据')}</div>
    <footer class="panel-bottom"><span>${rows.length} 家分店</span>${state.selectedBranch ? '<button id="clear-branch-footer" data-action="clear-branch">清除分店</button>' : `<span class="scroll-hint">左右滑动查看更多 ${icon('arrow')}</span>`}</footer>
  </section>`;
}

function productPanel() {
  const supplier = suppliers.find((item) => item.id === state.selectedSupplier);
  const branch = branches.find((item) => item.id === state.selectedBranch);
  const product = products.find((item) => item.id === state.selectedProduct);
  const rows = sorted(report.productRows, 'products');
  const pageSummary = report.pageSummary;
  const keyword = state.search.trim();
  const scope = [supplier?.name, branch?.name].filter(Boolean).join(' / ') || '全部供应商 / 全部分店';
  const salesHeaders = `${header('金额', 93, 'products')}${header('数量', 64, 'products')}${header('均价', 74, 'products')}${header('增长率', 70, 'products')}`;
  const profitHeaders = `${header('毛利额', 97, 'products', null, '本期 / 同期毛利额')}${header('毛利率', 85, 'products', null, '本期 / 同期毛利率；毛利额 ÷ 销售金额')}`;
  const productCells = (row) => {
    const sales = `<td>${pair(row.revenue, row.compareRevenue)}</td><td>${pair(row.quantity, row.compareQuantity, 'count')}</td><td>${pair(row.aov, row.compareAov, 'price')}</td><td>${trend(row.revenue, row.compareRevenue)}</td>`;
    return state.metricView === 'profit' ? profitCells(row) + sales : sales + profitCells(row);
  };
  return `<section class="report-panel ${state.expanded === 'products' ? 'expanded' : ''}" data-panel="products" aria-labelledby="title-products">
    ${panelTop('products', 3, '商品明细', report.productTotal, product ? `已选 ${product.name} · 可继续切换商品` : `${scope} · 点击商品反查`)}
    <div class="panel-filter keyword-filter">${icon('search')}<input id="product-search" type="search" placeholder="关键字：名称 / 货号 / 条码 / 供应商" aria-label="商品关键字过滤：名称、货号、条码或供应商，多词用空格分隔" title="支持名称、货号、条码和供应商；多个关键字用空格分隔，同时匹配" value="${escape(state.search)}" data-search="search"/>${keyword ? `<button id="clear-keyword" class="keyword-clear" data-action="clear-keyword" aria-label="清除商品关键字" title="清除关键字">${icon('close')}</button>` : ''}<span class="filter-count">${report.productTotal} 件</span></div>
    <div class="panel-scroll" tabindex="0" aria-label="商品表，可横向滚动">${rows.length ? `<table class="data-table product-table"><thead><tr>${header('货号 / 商品名称', 193, 'products')}${state.metricView === 'profit' ? profitHeaders + salesHeaders : salesHeaders + profitHeaders}</tr></thead><tbody>${rows.map((row) => `<tr class="${state.selectedProduct === row.id ? 'selected' : ''}" data-id="${escape(row.id)}"><td><button id="product-${escape(row.id)}" class="name-button product-info product-select-button" data-action="product" data-id="${escape(row.id)}" aria-pressed="${state.selectedProduct === row.id}" title="${escape(row.name)} · 反查供应商及分店"><span class="product-thumb ${escape(['sage', 'sand', 'rose', 'blue'].includes(row.imageTone) ? row.imageTone : 'blue')}" role="img" aria-label="商品示意图">${icon(/card|卡/i.test(row.name) ? 'card' : 'box')}</span><span class="product-text"><span class="name-code">${escape(row.code)}</span><span class="name-main">${escape(row.name)}</span></span></button></td>${productCells(row)}</tr>`).join('')}</tbody></table>` : emptyState('没有匹配的商品')}</div>
    <div class="page-summary"><div class="page-summary-label"><b>本页商品汇总</b>${rows.length} 件 · 本期 / 同期</div><div class="page-summary-item" data-metric="revenue"><span class="page-metric-label">金额</span>${pair(pageSummary.revenue, pageSummary.compareRevenue, 'price')}</div><div class="page-summary-item" data-metric="grossProfit"><span class="page-metric-label">毛利额</span>${pair(pageSummary.grossProfit, pageSummary.compareGrossProfit, 'profit', pageSummary)}</div><div class="page-summary-item" data-metric="margin"><span class="page-metric-label">毛利率</span>${pair(pageSummary.margin, pageSummary.compareMargin, 'margin', pageSummary)}</div></div>
    <footer class="panel-bottom"><span class="pagination-info">${report.productTotal ? (report.page - 1) * state.pageSize + 1 : 0}–${Math.min(report.page * state.pageSize, report.productTotal)} / ${report.productTotal} 件</span><div class="pagination"><select id="page-size" aria-label="每页商品数量">${[10, 20, 50].map((size) => `<option value="${size}"${size === state.pageSize ? ' selected' : ''}>${size} / 页</option>`).join('')}</select><button id="previous-page" data-action="page" data-page="${report.page - 1}" aria-label="上一页" ${report.page <= 1 ? 'disabled' : ''}>${icon('left')}</button><span>${report.page} / ${Math.max(1, report.pageCount)}</span><button id="next-page" data-action="page" data-page="${report.page + 1}" aria-label="下一页" ${report.page >= report.pageCount ? 'disabled' : ''}>${icon('chevron')}</button></div></footer>
  </section>`;
}

function resizer(index) {
  return `<div class="resizer" id="resize-${index}" role="separator" tabindex="0" aria-orientation="vertical" aria-label="调整${index === 0 ? '供应商与分店' : '分店与商品'}栏宽" aria-valuemin="18" aria-valuemax="${Math.round(widths[index] + widths[index + 1] - 18)}" aria-valuenow="${Math.round(widths[index])}" data-resize="${index}" title="拖动调整栏宽；方向键微调；双击恢复默认"></div>`;
}

function metric(label, value, meta, small = false) {
  return `<div class="metric"><div class="metric-label">${label}</div><div class="metric-value${small ? ' metric-small' : ''}">${value}</div><div class="metric-meta">${meta}</div></div>`;
}

function renderSalesDetailV2(focusId = null) {
  // 重绘前记录各栏滚动位置，切换联动条件时保持操作上下文。
  const scrollPositions = [...document.querySelectorAll('.report-panel')].map((panel) => [panel.dataset.panel, panel.querySelector('.panel-scroll')?.scrollLeft ?? 0, panel.querySelector('.panel-scroll')?.scrollTop ?? 0]);
  const active = document.activeElement;
  const targetId = focusId ?? active?.id;
  const selection = active instanceof HTMLInputElement ? [active.selectionStart, active.selectionEnd] : null;
  report = computeReport(state);
  state.page = report.page;
  const supplier = suppliers.find((item) => item.id === state.selectedSupplier);
  const branch = branches.find((item) => item.id === state.selectedBranch);
  const product = products.find((item) => item.id === state.selectedProduct);
  const scopeLabel = [supplier?.name, branch?.name, product?.name].filter(Boolean).join(' / ');
  const keyword = state.search.trim();
  const summary = report.summary;
  const dates = compareDates();
  app.classList.toggle('compare-off', !state.autoCompare);
  app.classList.toggle('profit-view', state.metricView === 'profit');
  app.innerHTML = `
    <div class="page-heading"><div><div class="eyebrow">SALES EXPLORER</div><h1>销售明细</h1><p>供应商、分店与商品双向联动，从任意一栏开始分析。</p></div><div class="heading-actions"><button id="reset" class="button quiet" data-action="reset">重置筛选</button><button id="refresh" class="button" data-action="refresh">${icon('refresh')}刷新数据</button></div></div>
    <section class="filters" aria-label="日期及对比筛选"><span class="filter-label">日期范围</span><div class="date-range" title="使用右侧快捷日期切换范围">${icon('calendar')}<b>${ranges[state.range][1]}</b><span class="date-arrow">—</span><b>${ranges[state.range][2]}</b></div><div class="quick-ranges" role="group" aria-label="快捷日期">${Object.entries(ranges).map(([key, value]) => `<button id="range-${key}" data-action="range" data-range="${key}" aria-pressed="${state.range === key}">${value[0]}</button>`).join('')}</div><div class="compare-controls"><select id="compare-mode" aria-label="同比方式" ${!state.autoCompare ? 'disabled' : ''}><option value="week"${state.compareMode === 'week' ? ' selected' : ''}>按周同比</option><option value="date"${state.compareMode === 'date' ? ' selected' : ''}>按日期同比</option></select><button id="auto-compare" class="auto-switch" data-action="auto" aria-pressed="${state.autoCompare}"><span class="switch-track" aria-hidden="true"><i></i></span>自动对比</button></div></section>
    <div class="tabs-line"><div class="report-tabs" role="tablist" aria-label="供应商来源">${['australia', 'china'].map((kind) => `<button id="tab-${kind}" class="report-tab" role="tab" aria-selected="${state.kind === kind}" aria-controls="report-content" tabindex="${state.kind === kind ? 0 : -1}" data-action="kind" data-kind="${kind}">${kindLabel(kind)}<span>${suppliers.filter((item) => item.kind === kind).length}</span></button>`).join('')}</div><div class="view-controls"><span>优先展示</span><div class="metric-view-switch" role="group" aria-label="列表指标显示">${[['sales', '销售指标'], ['profit', '毛利指标']].map(([view, label]) => `<button id="view-${view}" data-action="view" data-view="${view}" aria-pressed="${state.metricView === view}" title="优先显示${label}，其他字段仍可横向滚动查看">${label}</button>`).join('')}</div></div></div>
    <div class="summary-strip" aria-label="当前筛选范围汇总">
      ${metric(scopeLabel || keyword ? '筛选范围营业额' : '当前标签营业额', money(summary.revenue), `同期 ${money(state.autoCompare ? summary.compareRevenue : null)} ${trend(summary.revenue, summary.compareRevenue)}`)}
      ${metric('客单数', format(summary.orders), `同期 ${format(state.autoCompare ? summary.compareOrders : null)}`)}
      ${metric('客单价', money(summary.aov, 2), `同期 ${money(state.autoCompare ? summary.compareAov : null, 2)}`)}
      ${metric('毛利额', money(summary.grossProfit, 2), summary.grossProfit == null ? '成本待补全' : `同期 ${money(state.autoCompare ? summary.compareGrossProfit : null, 2)}`)}
      ${metric('毛利率', percent(summary.margin), summary.margin == null ? summary.revenue === 0 ? '无销售，暂不可计算' : '成本待补全' : `同期 ${percent(state.autoCompare ? summary.compareMargin : null)}`)}
    </div>
    <div class="scope-bar"><div class="scope-path"><span class="scope-label">筛选条件</span><span>${state.kind === 'china' ? '国内供应商' : '澳洲供应商'}</span>${supplier ? `<button id="scope-supplier" class="scope-chip" data-action="clear-supplier" aria-label="清除供应商 ${escape(supplier.name)}" title="清除供应商：${escape(supplier.name)}"><span>${escape(supplier.name)}</span>${icon('close')}</button>` : ''}${branch ? `<button id="scope-branch" class="scope-chip" data-action="clear-branch" aria-label="清除分店 ${escape(branch.name)}" title="清除分店：${escape(branch.name)}"><span>${escape(branch.name)}</span>${icon('close')}</button>` : ''}${product ? `<button id="scope-product" class="scope-chip" data-action="clear-product" aria-label="清除商品 ${escape(product.name)}" title="清除商品：${escape(product.name)}"><span>商品：${escape(product.name)}</span>${icon('close')}</button>` : ''}${keyword ? `<button id="scope-keyword" class="scope-chip keyword-chip" data-action="clear-keyword" aria-label="清除关键字 ${escape(keyword)}" title="清除关键字：${escape(keyword)}"><span>关键字：${escape(keyword)}</span>${icon('close')}</button>` : ''}${supplier || branch || product || keyword ? '<button id="clear-scope" class="clear-all" data-action="clear">清除全部</button>' : '<span>· 全部供应商 / 全部分店 / 全部商品</span>'}</div><div class="table-legend"><span class="legend-period">本期</span><span class="legend-period compare">同期</span><span class="divider">|</span><span>${state.autoCompare ? `${dates[0]} — ${dates[1]}` : '已暂停自动对比'}</span><span class="divider">|</span><span>AUD</span></div></div>
    <div id="report-content" role="tabpanel" aria-labelledby="tab-${state.kind}" class="report-grid ${state.expanded ? 'is-expanded' : ''}" style="--supplier-width:${widths[0]}fr;--branch-width:${widths[1]}fr;--product-width:${widths[2]}fr">${supplierPanel()}${resizer(0)}${branchPanel()}${resizer(1)}${productPanel()}</div>`;
  for (const [panel, left, top] of scrollPositions) {
    const scroll = document.querySelector(`[data-panel="${panel}"] .panel-scroll`);
    if (scroll) {
      // 指标切换只调整字段优先级，回到栏首展示所选指标；筛选与纵向位置不变。
      scroll.scrollLeft = focusId?.startsWith('view-') ? 0 : left;
      scroll.scrollTop = top;
    }
  }
  const target = targetId ? document.getElementById(targetId) : null;
  if (target && !target.disabled) {
    target.focus({ preventScroll: true });
    if (selection?.[0] != null && target instanceof HTMLInputElement) target.setSelectionRange(...selection);
  } else if (focusId) app.focus({ preventScroll: true });
}

function notify(message) {
  clearTimeout(toastTimer);
  const toast = document.querySelector('#toast');
  toast.textContent = message;
  toast.hidden = false;
  toastTimer = setTimeout(() => { toast.hidden = true; }, 2200);
}

app.addEventListener('click', (event) => {
  const control = event.target.closest('[data-action]');
  if (!control || control.disabled) return;
  let focusId = control.id;
  switch (control.dataset.action) {
    case 'view':
      if (state.metricView === control.dataset.view) return;
      state.metricView = control.dataset.view;
      break;
    case 'kind':
      if (state.kind === control.dataset.kind) return;
      state.kind = control.dataset.kind;
      state.selectedSupplier = null; state.selectedBranch = null; state.selectedProduct = null; state.search = ''; state.supplierSearch = ''; state.branchSearch = ''; state.page = 1; state.expanded = null;
      history.replaceState(null, '', `?kind=${state.kind}`);
      break;
    case 'supplier':
      state.selectedSupplier = state.selectedSupplier === control.dataset.id ? null : control.dataset.id;
      // 三栏选择是独立条件，更换供应商时保留分店及商品，支持反向追查。
      state.page = 1;
      break;
    case 'branch': state.selectedBranch = state.selectedBranch === control.dataset.id ? null : control.dataset.id; state.page = 1; break;
    case 'product': state.selectedProduct = state.selectedProduct === control.dataset.id ? null : control.dataset.id; break;
    case 'clear-supplier': state.selectedSupplier = null; state.page = 1; focusId = 'supplier-search'; break;
    case 'clear-branch': state.selectedBranch = null; state.page = 1; focusId = 'product-search'; break;
    case 'clear-product': state.selectedProduct = null; focusId = 'product-search'; break;
    case 'clear-keyword': state.search = ''; state.page = 1; focusId = 'product-search'; break;
    case 'clear': state.selectedSupplier = null; state.selectedBranch = null; state.selectedProduct = null; state.search = ''; state.page = 1; focusId = 'supplier-search'; break;
    case 'range': state.range = control.dataset.range; state.page = 1; break;
    case 'auto': state.autoCompare = !state.autoCompare; break;
    case 'page': {
      state.page = Math.min(Math.max(1, Number(control.dataset.page)), report.pageCount);
      // 到达首尾页时落到仍可操作的相邻按钮，其余翻页保留原按钮焦点。
      if (control.id === 'next-page' && state.page >= report.pageCount) focusId = 'previous-page';
      if (control.id === 'previous-page' && state.page <= 1) focusId = 'next-page';
      break;
    }
    case 'expand': state.expanded = state.expanded === control.dataset.panel ? null : control.dataset.panel; break;
    case 'sort': {
      const previous = state.sort[control.dataset.panel];
      state.sort[control.dataset.panel] = [control.dataset.field, previous[0] === control.dataset.field && previous[1] === 'desc' ? 'asc' : 'desc'];
      break;
    }
    case 'reset': {
      const kind = state.kind;
      state = structuredClone(defaultState); state.kind = kind; widths = [28, 27, 45];
      notify('筛选已重置');
      break;
    }
    case 'refresh': notify('样例数据已刷新'); break;
    default: return;
  }
  renderSalesDetailV2(focusId);
});

function applySearchInput(event) {
  const key = event.target.dataset.search;
  if (!key) return;
  // 关键字变更后解除商品反查，避免被过滤隐藏的商品继续限定其他栏。
  if (key === 'search' && state[key] !== event.target.value) {
    state.selectedProduct = null;
    state.page = 1;
  }
  state[key] = event.target.value;
  renderSalesDetailV2(event.target.id);
}

// 中文输入法组词期间保留输入节点，确认候选后才更新联动结果。
app.addEventListener('compositionstart', () => { composing = true; });
app.addEventListener('compositionend', (event) => {
  composing = false;
  applySearchInput(event);
});
app.addEventListener('input', (event) => {
  if (composing || event.isComposing) return;
  applySearchInput(event);
});

app.addEventListener('change', (event) => {
  if (event.target.id === 'page-size') { state.pageSize = Number(event.target.value); state.page = 1; }
  else if (event.target.id === 'compare-mode') state.compareMode = event.target.value;
  else return;
  renderSalesDetailV2(event.target.id);
});

function updateWidths(index, change) {
  const total = widths[index] + widths[index + 1];
  // 相邻两栏共享宽度，保留每栏至少 18%，避免拖动后无法操作。
  widths[index] = Math.max(18, Math.min(total - 18, widths[index] + change));
  widths[index + 1] = total - widths[index];
  const grid = document.querySelector('.report-grid');
  ['supplier', 'branch', 'product'].forEach((panel, i) => grid.style.setProperty(`--${panel}-width`, `${widths[i]}fr`));
  document.querySelectorAll('[data-resize]').forEach((separator) => {
    const current = Number(separator.dataset.resize);
    separator.setAttribute('aria-valuenow', Math.round(widths[current]));
    separator.setAttribute('aria-valuemax', Math.round(widths[current] + widths[current + 1] - 18));
  });
}

app.addEventListener('pointerdown', (event) => {
  const separator = event.target.closest('[data-resize]');
  if (!separator || event.button !== 0) return;
  separator.setPointerCapture(event.pointerId);
  const index = Number(separator.dataset.resize);
  let previousX = event.clientX;
  const onMove = (move) => {
    const gridWidth = document.querySelector('.report-grid').getBoundingClientRect().width - 20;
    updateWidths(index, (move.clientX - previousX) / gridWidth * 100);
    previousX = move.clientX;
  };
  const onEnd = () => {
    separator.removeEventListener('pointermove', onMove);
    separator.removeEventListener('pointerup', onEnd);
    separator.removeEventListener('pointercancel', onEnd);
  };
  separator.addEventListener('pointermove', onMove);
  separator.addEventListener('pointerup', onEnd);
  separator.addEventListener('pointercancel', onEnd);
});

app.addEventListener('dblclick', (event) => {
  if (!event.target.closest('[data-resize]')) return;
  widths = [28, 27, 45];
  renderSalesDetailV2();
});

app.addEventListener('keydown', (event) => {
  if (event.target.matches('[role="tab"]') && ['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) {
    event.preventDefault();
    const kind = event.key === 'Home' ? 'australia' : event.key === 'End' ? 'china' : state.kind === 'australia' ? 'china' : 'australia';
    document.getElementById(`tab-${kind}`).click();
  }
  if (event.target.matches('[data-resize]') && ['ArrowLeft', 'ArrowRight'].includes(event.key)) {
    event.preventDefault();
    updateWidths(Number(event.target.dataset.resize), event.key === 'ArrowLeft' ? -2 : 2);
  }
  if (event.key === 'Escape' && state.expanded) {
    const panel = state.expanded;
    state.expanded = null;
    renderSalesDetailV2(`expand-${panel}`);
  }
});

renderSalesDetailV2();
